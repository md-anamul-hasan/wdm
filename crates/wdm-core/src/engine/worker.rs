use std::sync::Arc;
use std::time::{Duration, Instant};

use reqwest::{Client, Method, StatusCode};
use tokio_util::sync::CancellationToken;

use super::http::{self, ByteRange};
use super::limiter::SpeedLimiter;
use super::prober::parse_content_range;
use super::storage::StorageWriter;
use crate::models::{DownloadRequestOptions, Segment, SegmentState};
use crate::{Error, Result};

const MAX_RETRIES: u32 = 5;

enum Attempt {
    Done,
    /// The server closed the stream before the segment was complete.
    Short,
}

/// Worker responsible for downloading individual byte segments using HTTP range requests.
/// Writes directly to the segment's file offset via the `StorageWriter`.
pub struct HttpConnectionWorker {
    pub client: Client,
    pub writer: Arc<StorageWriter>,
    pub limiter: Arc<SpeedLimiter>,
    pub on_bytes_read: Arc<dyn Fn(i64) + Send + Sync>,
    pub options: Option<DownloadRequestOptions>,
    /// No data for this long: the connection is considered stalled and retried.
    pub stall_timeout: Duration,
    /// Retry delay is `min(2^n, 16)` of these.
    pub backoff_unit: Duration,
}

/// Access revoked, link expired or file gone: asking again changes nothing.
fn is_permanent_client_error(code: StatusCode) -> bool {
    let c = code.as_u16();
    (400..500).contains(&c) && c != 408 && c != 429
}

impl HttpConnectionWorker {
    fn backoff(&self, retry: u32) -> Duration {
        self.backoff_unit * 2u32.pow(retry.min(4)).min(16)
    }

    async fn sleep(&self, d: Duration, ct: &CancellationToken) -> Result<()> {
        tokio::select! {
            _ = ct.cancelled() => Err(Error::Canceled),
            _ = tokio::time::sleep(d) => Ok(()),
        }
    }

    pub async fn download_segment(
        &self,
        url: &str,
        segment: &Arc<Segment>,
        ct: &CancellationToken,
    ) -> Result<()> {
        segment.set_state(SegmentState::Connecting);
        let mut retry = 0u32;

        while segment.remaining() > 0 {
            if ct.is_cancelled() {
                segment.set_state(SegmentState::Idle);
                segment.set_speed(0.0);
                return Err(Error::Canceled);
            }

            let outcome = match self.attempt(url, segment, ct, &mut retry).await {
                Ok(Attempt::Done) => {
                    segment.set_state(SegmentState::Completed);
                    segment.set_speed(0.0);
                    return Ok(());
                }
                Ok(Attempt::Short) => {
                    retry += 1;
                    if retry > MAX_RETRIES {
                        Err(Error::Io(std::io::Error::new(
                            std::io::ErrorKind::UnexpectedEof,
                            "Connection closed before the segment finished.",
                        )))
                    } else {
                        Ok(())
                    }
                }
                Err(e) => Err(e),
            };

            match outcome {
                Ok(()) => self
                    .sleep(self.backoff(retry), ct)
                    .await
                    .inspect_err(|_| self.to_idle(segment))?,
                Err(Error::Canceled) => {
                    self.to_idle(segment);
                    return Err(Error::Canceled);
                }
                Err(e @ (Error::NonRetryable(_) | Error::RangeIgnored(_))) => {
                    self.to_error(segment);
                    return Err(e);
                }
                Err(e) => {
                    retry += 1;
                    if retry > MAX_RETRIES {
                        self.to_error(segment);
                        return Err(e);
                    }
                    self.sleep(self.backoff(retry), ct)
                        .await
                        .inspect_err(|_| self.to_idle(segment))?;
                }
            }
        }

        segment.set_state(SegmentState::Completed);
        segment.set_speed(0.0);
        Ok(())
    }

    fn to_idle(&self, s: &Segment) {
        s.set_state(SegmentState::Idle);
        s.set_speed(0.0);
    }

    fn to_error(&self, s: &Segment) {
        s.set_state(SegmentState::Error);
        s.set_speed(0.0);
    }

    async fn attempt(
        &self,
        url: &str,
        segment: &Arc<Segment>,
        ct: &CancellationToken,
        retry: &mut u32,
    ) -> Result<Attempt> {
        // Range request if the end offset is known, open-ended range when resuming a segment of unknown size
        let range = if !segment.is_open_ended() && segment.end() >= segment.current() {
            Some(ByteRange {
                from: segment.current(),
                to: Some(segment.end()),
            })
        } else if segment.current() > 0 {
            Some(ByteRange {
                from: segment.current(),
                to: None,
            })
        } else {
            None
        };

        let mut response = http::send(
            &self.client,
            Method::GET,
            url,
            self.options.as_ref(),
            range,
            ct,
        )
        .await?;
        let status = response.status();

        if status != StatusCode::PARTIAL_CONTENT && status != StatusCode::OK {
            let message = format!(
                "Server returned HTTP {} {}",
                status.as_u16(),
                status.canonical_reason().unwrap_or("")
            );
            return Err(if is_permanent_client_error(status) {
                Error::NonRetryable(message)
            } else {
                Error::Http(message)
            });
        }

        // Bytes are written at an absolute offset, so a 206 that starts elsewhere than requested would corrupt the file
        if let (Some(r), StatusCode::PARTIAL_CONTENT) = (range, status) {
            let served = response
                .headers()
                .get(reqwest::header::CONTENT_RANGE)
                .and_then(|v| v.to_str().ok())
                .and_then(parse_content_range)
                .and_then(|c| c.from);
            if let Some(served) = served.filter(|&s| s != r.from) {
                return Err(Error::NonRetryable(format!(
                    "The server answered with bytes {served}- instead of the requested {}-.",
                    r.from
                )));
            }
        }

        // A 200 to a request for a non-zero offset means the server ignored the Range header;
        // writing that body at this offset would silently corrupt the file.
        if range.is_some() && status == StatusCode::OK && segment.current() > 0 {
            return Err(Error::RangeIgnored(
                "The server does not support resuming (ignored Range request).".into(),
            ));
        }

        segment.set_state(SegmentState::Downloading);
        let mut window_start = Instant::now();
        let mut bytes_in_period = 0i64;

        while segment.remaining() > 0 {
            let next = tokio::select! {
                _ = ct.cancelled() => return Err(Error::Canceled),
                r = tokio::time::timeout(self.stall_timeout, response.chunk()) => match r {
                    Err(_) => return Err(Error::Http("Connection stalled.".into())),
                    Ok(r) => r?,
                },
            };

            let Some(mut chunk) = next else {
                // Stream closed by server
                if segment.is_open_ended() {
                    segment.close_open_end();
                }
                break;
            };

            *retry = 0;
            chunk.truncate(segment.remaining().min(chunk.len() as i64) as usize);
            let n = chunk.len() as i64;

            tokio::select! {
                _ = ct.cancelled() => return Err(Error::Canceled),
                _ = self.limiter.throttle(chunk.len()) => {}
            }

            // Direct zero-merge write to the destination file at the absolute segment offset
            self.writer.write_at(chunk, segment.current()).await?;
            segment.advance(n);
            bytes_in_period += n;
            (self.on_bytes_read)(n);

            // Speed is recalculated every 250 ms
            let elapsed = window_start.elapsed().as_secs_f64();
            if elapsed >= 0.25 {
                segment.set_speed(bytes_in_period as f64 / elapsed);
                bytes_in_period = 0;
                window_start = Instant::now();
            }
        }

        Ok(if segment.remaining() <= 0 {
            Attempt::Done
        } else {
            Attempt::Short
        })
    }
}
