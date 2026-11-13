//! Loopback throughput of the engine. Run: cargo test --release -p wdm-core --test bench -- --ignored --nocapture
mod common;

use std::time::Instant;

use common::{pattern, spawn, Cfg};
use tokio_util::sync::CancellationToken;
use wdm_core::engine::DownloadEngine;

const MB: usize = 1024 * 1024;

#[tokio::test(flavor = "multi_thread")]
#[ignore = "benchmark"]
async fn throughput_by_connection_count() {
    let size = 256 * MB;
    let data = pattern(size);
    let server = spawn(Cfg::new(data.clone())).await;
    println!("connections  MB/s (median of 3, {} MiB)", size / MB);
    for conns in [1, 2, 4, 8, 16, 32] {
        let mut speeds = vec![];
        for _ in 0..3 {
            let dir = tempfile::tempdir().unwrap();
            let e = DownloadEngine::new(None, 0, None).unwrap();
            let t = Instant::now();
            e.start(
                &server.url("/files/big.bin"),
                Some(dir.path()),
                conns,
                CancellationToken::new(),
                None,
            )
            .await
            .unwrap();
            speeds.push(size as f64 / MB as f64 / t.elapsed().as_secs_f64());
            let got = std::fs::read(dir.path().join("big.bin")).unwrap();
            assert!(got == data, "download must be byte-exact");
        }
        speeds.sort_by(|a, b| a.partial_cmp(b).unwrap());
        println!("{conns:>11}  {:>6.0}", speeds[1]);
    }
}
