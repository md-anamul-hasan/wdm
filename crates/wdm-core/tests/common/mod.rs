//! Minimal range-capable HTTP/1.1 server for engine tests (std + tokio only).
#![allow(dead_code)]

use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::Arc;
use std::time::Duration;

use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::{TcpListener, TcpStream};

#[derive(Clone)]
pub struct Cfg {
    pub data: Arc<Vec<u8>>,
    /// Honour `Range` requests.
    pub serve_ranges: bool,
    /// Send `Accept-Ranges: bytes` (a server may advertise ranges and still ignore them).
    pub advertise_ranges: bool,
    pub etag: Option<String>,
    pub disposition: Option<String>,
    /// No `Content-Length`: the body ends when the connection closes.
    pub unknown_length: bool,
    /// Answer every request (HEAD and GET) with this status.
    pub force_status: Option<u16>,
    /// Pause after each 16 KiB written (slows transfers so tests can interrupt them).
    pub chunk_delay: Duration,
}

impl Cfg {
    pub fn new(data: Vec<u8>) -> Self {
        Self {
            data: Arc::new(data),
            serve_ranges: true,
            advertise_ranges: true,
            etag: Some("\"v1\"".into()),
            disposition: None,
            unknown_length: false,
            force_status: None,
            chunk_delay: Duration::ZERO,
        }
    }
}

pub struct Server {
    pub addr: std::net::SocketAddr,
    pub get_requests: Arc<AtomicUsize>,
    pub ranged_get_requests: Arc<AtomicUsize>,
}

impl Server {
    pub fn url(&self, path: &str) -> String {
        format!("http://{}{}", self.addr, path)
    }
}

pub fn pattern(len: usize) -> Vec<u8> {
    (0..len).map(|i| ((i * 31 + i / 251) % 256) as u8).collect()
}

pub async fn spawn(cfg: Cfg) -> Server {
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let addr = listener.local_addr().unwrap();
    let gets = Arc::new(AtomicUsize::new(0));
    let ranged = Arc::new(AtomicUsize::new(0));
    let (g, r) = (gets.clone(), ranged.clone());
    tokio::spawn(async move {
        loop {
            let Ok((sock, _)) = listener.accept().await else { break };
            let (cfg, g, r) = (cfg.clone(), g.clone(), r.clone());
            tokio::spawn(async move {
                let _ = handle(sock, cfg, g, r).await;
            });
        }
    });
    Server { addr, get_requests: gets, ranged_get_requests: ranged }
}

fn parse_range(v: &str, len: usize) -> Option<(usize, usize)> {
    let spec = v.trim().strip_prefix("bytes=")?;
    let (a, b) = spec.split_once('-')?;
    let start: usize = a.parse().ok()?;
    let end: usize = if b.is_empty() { len.checked_sub(1)? } else { b.parse::<usize>().ok()?.min(len.checked_sub(1)?) };
    (start <= end).then_some((start, end))
}

async fn handle(mut sock: TcpStream, cfg: Cfg, gets: Arc<AtomicUsize>, ranged: Arc<AtomicUsize>) -> std::io::Result<()> {
    let mut buf = Vec::new();
    let mut tmp = [0u8; 2048];
    while !buf.windows(4).any(|w| w == b"\r\n\r\n") {
        let n = sock.read(&mut tmp).await?;
        if n == 0 {
            return Ok(());
        }
        buf.extend_from_slice(&tmp[..n]);
    }
    let text = String::from_utf8_lossy(&buf).into_owned();
    let mut lines = text.split("\r\n");
    let method = lines.next().unwrap_or("").split(' ').next().unwrap_or("").to_string();
    let range_header = lines.filter_map(|l| l.split_once(':')).find(|(k, _)| k.eq_ignore_ascii_case("range")).map(|(_, v)| v.trim().to_string());

    let is_head = method == "HEAD";
    if !is_head {
        gets.fetch_add(1, Ordering::SeqCst);
        if range_header.is_some() {
            ranged.fetch_add(1, Ordering::SeqCst);
        }
    }

    let len = cfg.data.len();
    let mut headers = String::new();
    if cfg.advertise_ranges {
        headers.push_str("Accept-Ranges: bytes\r\n");
    }
    if let Some(e) = &cfg.etag {
        headers.push_str(&format!("ETag: {e}\r\n"));
    }
    headers.push_str("Last-Modified: Wed, 21 Oct 2015 07:28:00 GMT\r\nContent-Type: application/octet-stream\r\n");
    if let Some(d) = &cfg.disposition {
        headers.push_str(&format!("Content-Disposition: {d}\r\n"));
    }

    if let Some(code) = cfg.force_status {
        sock.write_all(format!("HTTP/1.1 {code} Err\r\nContent-Length: 0\r\nConnection: close\r\n\r\n").as_bytes()).await?;
        return sock.shutdown().await;
    }

    let range = if cfg.serve_ranges && !is_head { range_header.as_deref().and_then(|v| parse_range(v, len)) } else { None };
    let (status, start, end) = match range {
        Some((s, e)) => {
            headers.push_str(&format!("Content-Range: bytes {s}-{e}/{len}\r\n"));
            ("206 Partial Content", s, e + 1)
        }
        None => ("200 OK", 0, len),
    };
    if !cfg.unknown_length {
        headers.push_str(&format!("Content-Length: {}\r\n", end - start));
    }
    sock.write_all(format!("HTTP/1.1 {status}\r\n{headers}Connection: close\r\n\r\n").as_bytes()).await?;

    if !is_head {
        for chunk in cfg.data[start..end].chunks(16 * 1024) {
            sock.write_all(chunk).await?;
            if !cfg.chunk_delay.is_zero() {
                tokio::time::sleep(cfg.chunk_delay).await;
            }
        }
    }
    sock.shutdown().await
}

