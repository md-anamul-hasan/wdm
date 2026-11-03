use std::time::Duration;
use tracing::warn;

use crate::ipc::ExternalDownloadRequest;

const MAX_MESSAGE_BYTES: usize = 1024 * 1024;

pub fn default_pipe_name() -> String {
    let user = std::env::var("USERNAME")
        .or_else(|_| std::env::var("USER"))
        .unwrap_or_else(|_| "DefaultUser".into());
    format!(r"\\.\pipe\WDM.Ipc.{user}")
}

pub fn normalize_pipe_name(name: Option<&str>) -> String {
    match name {
        Some(n) if n.starts_with(r"\\.\pipe\") => n.to_string(),
        Some(n) => format!(r"\\.\pipe\{n}"),
        None => default_pipe_name(),
    }
}

pub struct WdmPipe;

#[cfg(windows)]
mod windows_impl {
    use super::*;
    use std::io;
    use tokio::io::{AsyncReadExt, AsyncWriteExt};
    use tokio::net::windows::named_pipe::{ClientOptions, NamedPipeClient, ServerOptions};

    async fn connect_client(path: &str, timeout_dur: Duration) -> io::Result<NamedPipeClient> {
        let start = tokio::time::Instant::now();
        loop {
            match ClientOptions::new().open(path) {
                Ok(client) => return Ok(client),
                Err(err) => {
                    if start.elapsed() >= timeout_dur {
                        return Err(err);
                    }
                    tokio::time::sleep(Duration::from_millis(30)).await;
                }
            }
        }
    }

    impl WdmPipe {
        /// Sends a request to the running instance. Returns false if none is listening.
        pub async fn try_send(
            request: &ExternalDownloadRequest,
            timeout_dur: Duration,
            pipe_name: Option<&str>,
        ) -> bool {
            let path = normalize_pipe_name(pipe_name);
            let send_op = async {
                let mut client = connect_client(&path, timeout_dur).await?;
                let payload = serde_json::to_vec(request).map_err(|e| io::Error::new(io::ErrorKind::InvalidData, e))?;
                client.write_all(&payload).await?;
                client.flush().await?;
                Ok::<(), io::Error>(())
            };

            matches!(tokio::time::timeout(timeout_dur, send_op).await, Ok(Ok(())))
        }

        /// True if a running WDM is listening. Connects and hangs up without sending anything.
        /// Retries once: between two clients the listener briefly has no free pipe instance.
        pub async fn is_listening(timeout_dur: Duration, pipe_name: Option<&str>) -> bool {
            let path = normalize_pipe_name(pipe_name);
            for attempt in 0..2 {
                if let Ok(Ok(_client)) = tokio::time::timeout(timeout_dur, connect_client(&path, timeout_dur)).await {
                    return true;
                }
                if attempt == 0 {
                    tokio::time::sleep(Duration::from_millis(150)).await;
                }
            }
            false
        }

        /// Accepts requests until `stop_rx` triggers, invoking `on_request` for each valid request.
        pub async fn listen<F>(
            on_request: F,
            mut stop_rx: tokio::sync::watch::Receiver<bool>,
            read_timeout: Option<Duration>,
            pipe_name: Option<&str>,
        ) where
            F: Fn(ExternalDownloadRequest) + Send + Sync + 'static,
        {
            let path = normalize_pipe_name(pipe_name);
            let client_timeout = read_timeout.unwrap_or(Duration::from_secs(5));

            while !*stop_rx.borrow() {
                let server = match ServerOptions::new()
                    .reject_remote_clients(true)
                    .create(&path)
                {
                    Ok(s) => s,
                    Err(e) => {
                        warn!("Failed to create named pipe server instance: {e}");
                        tokio::select! {
                            _ = tokio::time::sleep(Duration::from_millis(100)) => {},
                            _ = stop_rx.changed() => break,
                        }
                        continue;
                    }
                };

                let mut server = server;

                // Wait for client connection or stop
                tokio::select! {
                    res = server.connect() => {
                        if let Err(e) = res {
                            warn!("Error waiting for pipe connection: {e}");
                            continue;
                        }
                    }
                    _ = stop_rx.changed() => break,
                }

                // Read client payload with client_timeout
                let mut buffer = Vec::new();
                let mut chunk = [0u8; 8192];
                let mut read_failed = false;

                let read_fut = async {
                    loop {
                        let n = server.read(&mut chunk).await?;
                        if n == 0 {
                            break;
                        }
                        buffer.extend_from_slice(&chunk[..n]);
                        if buffer.len() > MAX_MESSAGE_BYTES {
                            return Err(io::Error::new(io::ErrorKind::InvalidData, "IPC message too large"));
                        }
                    }
                    Ok::<(), io::Error>(())
                };

                if tokio::time::timeout(client_timeout, read_fut).await.is_err() {
                    read_failed = true;
                }

                if read_failed || buffer.is_empty() {
                    continue;
                }

                if let Ok(mut req) = serde_json::from_slice::<ExternalDownloadRequest>(&buffer) {
                    let mut filtered: Vec<String> = Vec::new();
                    for url in req.urls {
                        if ExternalDownloadRequest::is_http_url(&url) && !filtered.contains(&url) {
                            filtered.push(url);
                        }
                    }
                    req.urls = filtered;
                    if !req.is_empty() || req.activate_only {
                        on_request(req);
                    }
                }
            }
        }
    }
}

#[cfg(not(windows))]
mod non_windows_impl {
    use super::*;

    impl WdmPipe {
        pub async fn try_send(_req: &ExternalDownloadRequest, _t: Duration, _n: Option<&str>) -> bool {
            false
        }
        pub async fn is_listening(_t: Duration, _n: Option<&str>) -> bool {
            false
        }
        pub async fn listen<F>(_f: F, _s: tokio::sync::watch::Receiver<bool>, _r: Option<Duration>, _p: Option<&str>) {}
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn test_pipe_send_and_receive() {
        let test_pipe_name = format!(r"\\.\pipe\WDM.TestPipe.{}", uuid::Uuid::new_v4());
        let (stop_tx, stop_rx) = tokio::sync::watch::channel(false);
        let (received_tx, mut received_rx) = tokio::sync::mpsc::channel(1);

        let pipe_name_clone = test_pipe_name.clone();
        tokio::spawn(async move {
            WdmPipe::listen(
                move |req| {
                    let _ = received_tx.try_send(req);
                },
                stop_rx,
                Some(Duration::from_secs(2)),
                Some(&pipe_name_clone),
            )
            .await;
        });

        // Give the listener a moment to start
        tokio::time::sleep(Duration::from_millis(100)).await;

        assert!(WdmPipe::is_listening(Duration::from_secs(2), Some(&test_pipe_name)).await);

        let req = ExternalDownloadRequest {
            urls: vec!["https://example.com/test.zip".into()],
            silent: true,
            ..Default::default()
        };

        let sent = WdmPipe::try_send(&req, Duration::from_secs(3), Some(&test_pipe_name)).await;
        assert!(sent);

        let received = tokio::time::timeout(Duration::from_secs(3), received_rx.recv())
            .await
            .expect("timeout")
            .expect("should receive");

        assert_eq!(received.urls, vec!["https://example.com/test.zip"]);
        assert!(received.silent);

        let _ = stop_tx.send(true);
    }
}
