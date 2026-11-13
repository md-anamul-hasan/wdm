mod common;

use std::path::Path;
use std::sync::Arc;
use std::time::Duration;

use common::{pattern, spawn, Cfg};
use sha2::{Digest, Sha256};
use tokio_util::sync::CancellationToken;
use wdm_core::engine::{resume, DownloadEngine};
use wdm_core::models::{DownloadRequestOptions, DownloadStatus};
use wdm_core::Error;

const MB: usize = 1024 * 1024;

fn engine() -> Arc<DownloadEngine> {
    let e = DownloadEngine::new(None, 0, None).unwrap();
    e.set_retry_backoff_unit(Duration::from_millis(10));
    e
}

async fn run(
    e: &Arc<DownloadEngine>,
    url: &str,
    dir: &Path,
    conns: i32,
    opts: Option<DownloadRequestOptions>,
) -> wdm_core::Result<()> {
    e.start(url, Some(dir), conns, CancellationToken::new(), opts)
        .await
}

fn names(dir: &Path) -> Vec<String> {
    let mut v: Vec<String> = std::fs::read_dir(dir)
        .unwrap()
        .map(|e| e.unwrap().file_name().to_string_lossy().into_owned())
        .collect();
    v.sort();
    v
}

#[tokio::test]
async fn multi_connection_download_is_byte_exact() {
    let data = pattern(6 * MB + 123);
    let server = spawn(Cfg::new(data.clone())).await;
    let dir = tempfile::tempdir().unwrap();
    let e = engine();

    run(&e, &server.url("/files/data.bin"), dir.path(), 4, None)
        .await
        .unwrap();

    assert_eq!(e.status(), DownloadStatus::Completed);
    assert_eq!(std::fs::read(dir.path().join("data.bin")).unwrap(), data);
    assert_eq!(
        names(dir.path()),
        ["data.bin"],
        "no .wdm-part / .wdm-state left behind"
    );
    assert!(
        server
            .ranged_get_requests
            .load(std::sync::atomic::Ordering::SeqCst)
            >= 4
    );
    let m = e.metadata().unwrap();
    assert_eq!(m.total_bytes as usize, data.len());
    assert!(m.completed_at.is_some() && m.part_file_path.is_none());
}

#[tokio::test]
async fn progress_and_status_events_are_reported() {
    let server = spawn(Cfg {
        chunk_delay: Duration::from_millis(15),
        ..Cfg::new(pattern(3 * MB))
    })
    .await;
    let dir = tempfile::tempdir().unwrap();
    let e = engine();
    let statuses = Arc::new(std::sync::Mutex::new(Vec::new()));
    let progress = Arc::new(std::sync::Mutex::new(Vec::new()));
    let (s, p) = (statuses.clone(), progress.clone());
    e.set_on_status(move |st| s.lock().unwrap().push(st));
    e.set_on_progress(move |pr| {
        p.lock()
            .unwrap()
            .push((pr.downloaded_bytes, pr.total_bytes))
    });

    run(&e, &server.url("/p.bin"), dir.path(), 2, None)
        .await
        .unwrap();

    let statuses = statuses.lock().unwrap();
    assert_eq!(statuses.first(), Some(&DownloadStatus::Connecting));
    assert!(statuses.contains(&DownloadStatus::Downloading));
    assert_eq!(statuses.last(), Some(&DownloadStatus::Completed));
    let progress = progress.lock().unwrap();
    assert!(!progress.is_empty());
    assert!(progress
        .iter()
        .all(|(d, t)| *t == 3 * MB as i64 && *d <= *t));
    assert_eq!(progress.last().unwrap().0, 3 * MB as i64);
}

#[tokio::test]
async fn content_disposition_names_the_file() {
    let cfg = Cfg {
        disposition: Some("attachment; filename=\"report final.pdf\"".into()),
        ..Cfg::new(pattern(2000))
    };
    let server = spawn(cfg).await;
    let dir = tempfile::tempdir().unwrap();
    run(
        &engine(),
        &server.url("/download?id=7"),
        dir.path(),
        4,
        None,
    )
    .await
    .unwrap();
    assert_eq!(names(dir.path()), ["report final.pdf"]);
}

#[tokio::test]
async fn caller_chosen_file_name_wins_and_is_sanitized() {
    let server = spawn(Cfg::new(pattern(100))).await;
    let dir = tempfile::tempdir().unwrap();
    let opts = DownloadRequestOptions {
        file_name: Some("..\\..\\mine.txt".into()),
        ..Default::default()
    };
    run(&engine(), &server.url("/x.bin"), dir.path(), 1, Some(opts))
        .await
        .unwrap();
    assert_eq!(names(dir.path()), ["....mine.txt"]); // separators vanish, nothing can traverse
}

#[tokio::test]
async fn downloading_the_same_name_twice_never_overwrites() {
    let server = spawn(Cfg::new(pattern(5000))).await;
    let dir = tempfile::tempdir().unwrap();
    run(&engine(), &server.url("/f.bin"), dir.path(), 2, None)
        .await
        .unwrap();
    run(&engine(), &server.url("/f.bin"), dir.path(), 2, None)
        .await
        .unwrap();
    assert_eq!(names(dir.path()), ["f (1).bin", "f.bin"]);
}

#[tokio::test]
async fn pause_then_resume_completes_correctly() {
    let data = pattern(8 * MB);
    let server = spawn(Cfg {
        chunk_delay: Duration::from_millis(10),
        ..Cfg::new(data.clone())
    })
    .await;
    let dir = tempfile::tempdir().unwrap();
    let url = server.url("/big.bin");

    let first = engine();
    let task = {
        let (e, url, d) = (first.clone(), url.clone(), dir.path().to_path_buf());
        tokio::spawn(async move {
            e.start(&url, Some(&d), 2, CancellationToken::new(), None)
                .await
        })
    };
    tokio::time::sleep(Duration::from_millis(700)).await;
    first.pause();
    task.await.unwrap().unwrap();

    assert_eq!(first.status(), DownloadStatus::Paused);
    let target = dir.path().join("big.bin").to_string_lossy().into_owned();
    assert!(resume::checkpoint_path(&target).exists());
    assert!(Path::new(&resume::part_path(&target)).exists());
    assert!(!dir.path().join("big.bin").exists());
    let saved = resume::load_checkpoint(&target).await.unwrap();
    let done: i64 = saved.segments.iter().map(|s| s.downloaded_bytes()).sum();
    assert!(
        done > 0 && done < data.len() as i64,
        "partial progress was saved ({done})"
    );

    let second = engine();
    run(&second, &url, dir.path(), 2, None).await.unwrap();
    assert_eq!(second.status(), DownloadStatus::Completed);
    assert_eq!(std::fs::read(dir.path().join("big.bin")).unwrap(), data);
    assert_eq!(names(dir.path()), ["big.bin"]);
}

#[tokio::test]
async fn a_different_url_never_adopts_another_downloads_checkpoint() {
    let dir = tempfile::tempdir().unwrap();
    let old = spawn(Cfg {
        chunk_delay: Duration::from_millis(10),
        ..Cfg::new(pattern(4 * MB))
    })
    .await;
    let first = engine();
    let task = {
        let (e, url, d) = (first.clone(), old.url("/v.bin"), dir.path().to_path_buf());
        tokio::spawn(async move {
            e.start(&url, Some(&d), 2, CancellationToken::new(), None)
                .await
        })
    };
    tokio::time::sleep(Duration::from_millis(400)).await;
    first.pause();
    task.await.unwrap().unwrap();

    // Same file name, other URL (another server): it must get its own name and leave the old partial untouched
    let new_data: Vec<u8> = pattern(4 * MB).iter().map(|b| b ^ 0xff).collect();
    let new = spawn(Cfg {
        etag: Some("\"v2\"".into()),
        ..Cfg::new(new_data.clone())
    })
    .await;
    let second = engine();
    run(&second, &new.url("/v.bin"), dir.path(), 2, None)
        .await
        .unwrap();
    assert_eq!(
        std::fs::read(dir.path().join("v (1).bin")).unwrap(),
        new_data
    );
    assert!(dir.path().join("v.bin.wdm-state").exists());
}

#[tokio::test]
async fn server_that_ignores_ranges_falls_back_to_a_single_stream() {
    let data = pattern(5 * MB);
    let server = spawn(Cfg {
        serve_ranges: false,
        advertise_ranges: true,
        ..Cfg::new(data.clone())
    })
    .await;
    let dir = tempfile::tempdir().unwrap();
    let e = engine();
    run(&e, &server.url("/r.bin"), dir.path(), 4, None)
        .await
        .unwrap();
    assert_eq!(e.status(), DownloadStatus::Completed);
    assert_eq!(std::fs::read(dir.path().join("r.bin")).unwrap(), data);
    assert_eq!(names(dir.path()), ["r.bin"]);
    assert!(!e.metadata().unwrap().supports_ranges);
}

#[tokio::test]
async fn unknown_length_download_uses_what_the_server_sent() {
    let data = pattern(300_000);
    let server = spawn(Cfg {
        unknown_length: true,
        advertise_ranges: false,
        serve_ranges: false,
        etag: None,
        ..Cfg::new(data.clone())
    })
    .await;
    let dir = tempfile::tempdir().unwrap();
    let e = engine();
    run(&e, &server.url("/u.bin"), dir.path(), 8, None)
        .await
        .unwrap();
    assert_eq!(std::fs::read(dir.path().join("u.bin")).unwrap(), data);
    assert_eq!(e.metadata().unwrap().total_bytes as usize, data.len());
}

#[tokio::test]
async fn empty_file_downloads() {
    let server = spawn(Cfg::new(Vec::new())).await;
    let dir = tempfile::tempdir().unwrap();
    run(&engine(), &server.url("/empty.bin"), dir.path(), 4, None)
        .await
        .unwrap();
    assert_eq!(
        std::fs::read(dir.path().join("empty.bin")).unwrap(),
        Vec::<u8>::new()
    );
}

#[tokio::test]
async fn correct_checksum_passes_and_wrong_one_keeps_the_data() {
    let data = pattern(2 * MB);
    let good = hex::encode(Sha256::digest(&data));
    let server = spawn(Cfg::new(data.clone())).await;

    let dir = tempfile::tempdir().unwrap();
    let opts = DownloadRequestOptions {
        expected_sha256: Some(format!("SHA256:{}", good.to_uppercase())),
        ..Default::default()
    };
    run(&engine(), &server.url("/ok.bin"), dir.path(), 2, Some(opts))
        .await
        .unwrap();
    assert_eq!(std::fs::read(dir.path().join("ok.bin")).unwrap(), data);

    let dir2 = tempfile::tempdir().unwrap();
    let opts = DownloadRequestOptions {
        expected_sha256: Some("0".repeat(64)),
        ..Default::default()
    };
    let e = engine();
    let err = run(&e, &server.url("/bad.bin"), dir2.path(), 2, Some(opts))
        .await
        .unwrap_err();
    assert!(matches!(err, Error::ChecksumMismatch { .. }), "{err:?}");
    assert_eq!(e.status(), DownloadStatus::Error);
    assert!(
        !dir2.path().join("bad.bin").exists(),
        "never published under the real name"
    );
    let part = std::fs::read(dir2.path().join("bad.bin.wdm-part")).unwrap();
    assert_eq!(part, data, "data is kept, not deleted on a guess");
    assert!(dir2.path().join("bad.bin.wdm-state").exists());
}

#[tokio::test]
async fn malformed_checksum_fails_before_any_request() {
    let server = spawn(Cfg::new(pattern(10))).await;
    let dir = tempfile::tempdir().unwrap();
    let opts = DownloadRequestOptions {
        expected_sha256: Some("abc".into()),
        ..Default::default()
    };
    let err = run(&engine(), &server.url("/x"), dir.path(), 1, Some(opts))
        .await
        .unwrap_err();
    assert!(matches!(err, Error::InvalidArgument(_)));
    assert_eq!(
        server
            .get_requests
            .load(std::sync::atomic::Ordering::SeqCst),
        0
    );
    assert!(names(dir.path()).is_empty());
}

#[tokio::test]
async fn http_errors_fail_without_retry_storms() {
    let server = spawn(Cfg {
        force_status: Some(404),
        ..Cfg::new(Vec::new())
    })
    .await;
    let dir = tempfile::tempdir().unwrap();
    let e = engine();
    let err = run(&e, &server.url("/gone.bin"), dir.path(), 4, None)
        .await
        .unwrap_err();
    assert!(matches!(err, Error::Http(_)), "{err:?}");
    assert_eq!(e.status(), DownloadStatus::Error);
    assert_eq!(
        server
            .get_requests
            .load(std::sync::atomic::Ordering::SeqCst),
        1,
        "only the probe's ranged GET"
    );
    assert!(names(dir.path()).is_empty());
}

#[tokio::test]
async fn speed_limit_slows_the_transfer() {
    let server = spawn(Cfg::new(pattern(600_000))).await;
    let dir = tempfile::tempdir().unwrap();
    let e = DownloadEngine::new(None, 200_000, None).unwrap();
    let t = std::time::Instant::now();
    e.start(
        &server.url("/s.bin"),
        Some(dir.path()),
        2,
        CancellationToken::new(),
        None,
    )
    .await
    .unwrap();
    // 200 kB/s with a 200 kB initial bucket and some token debt: 600 kB cannot finish in under ~1 s (unthrottled: milliseconds)
    assert!(
        t.elapsed() >= Duration::from_millis(1000),
        "took {:?}",
        t.elapsed()
    );
}

#[tokio::test]
async fn video_platform_urls_are_rejected_explicitly() {
    let dir = tempfile::tempdir().unwrap();
    let err = run(
        &engine(),
        "https://www.youtube.com/watch?v=abc",
        dir.path(),
        1,
        None,
    )
    .await
    .unwrap_err();
    assert!(matches!(err, Error::InvalidArgument(_)));
}
