use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use tokio_util::sync::CancellationToken;

use wdm_core::engine::{DownloadEngine, MediaResolver};
use wdm_core::ipc::ExternalDownloadRequest;
use wdm_core::models::{DownloadProgress, DownloadRequestOptions, DownloadStatus, Sha256Checksum};
use wdm_core::Error;

const EXIT_PAUSED: i32 = 130;
const EXIT_CHECKSUM_MISMATCH: i32 = 2;

#[tokio::main]
async fn main() {
    let args: Vec<String> = std::env::args().collect();
    let code = run(args).await;
    std::process::exit(code);
}

async fn run(args: Vec<String>) -> i32 {
    if args.len() < 2 {
        print_banner();
        println!("Usage: wdm <url> [-o <directory>] [-c <connections>] [-l <limit_KB/s>] [--sha256 <hex>]");
        println!("\nExample: wdm https://speed.hetzner.de/100MB.bin -c 8\n");
        return 1;
    }

    let url = args[1].clone();
    let is_http = ExternalDownloadRequest::is_http_url(&url);
    let is_yt = MediaResolver::is_youtube_url(&url);

    if !is_http && !is_yt {
        eprintln!("[WDM Error] '{url}' is not an http(s) URL.");
        return 1;
    }

    let mut out_dir: Option<PathBuf> = None;
    let mut connections: i32 = 8;
    let mut limit_bytes_per_sec: i64 = 0;
    let mut sha256: Option<String> = None;

    let mut i = 2;
    while i < args.len() {
        let arg = args[i].as_str();
        if (arg == "-o" || arg == "--output") && i + 1 < args.len() {
            i += 1;
            out_dir = Some(PathBuf::from(&args[i]));
        } else if (arg == "-c" || arg == "--connections") && i + 1 < args.len() {
            i += 1;
            if let Ok(c) = args[i].parse::<i32>() {
                connections = c.clamp(1, 32);
            }
        } else if arg == "--sha256" && i + 1 < args.len() {
            i += 1;
            let val = &args[i];
            if let Some(normalized) = Sha256Checksum::try_normalize(Some(val)) {
                sha256 = Some(normalized);
            } else {
                eprintln!("[WDM Error] --sha256 needs 64 hexadecimal characters.");
                return 1;
            }
        } else if (arg == "-l" || arg == "--limit") && i + 1 < args.len() {
            i += 1;
            if let Ok(l) = args[i].parse::<i64>() {
                limit_bytes_per_sec = l.max(0) * 1024;
            }
        }
        i += 1;
    }

    print_banner();
    println!("Initiating download from: {url}");
    println!(
        "Connections: {connections} | Speed Limit: {}",
        if limit_bytes_per_sec > 0 {
            format!("{} KB/s", limit_bytes_per_sec / 1024)
        } else {
            "Unlimited".to_string()
        }
    );

    let cancel_token = CancellationToken::new();
    let cancel_token_ctrlc = cancel_token.clone();
    let is_paused = Arc::new(AtomicBool::new(false));
    let is_paused_ctrlc = is_paused.clone();

    tokio::spawn(async move {
        if tokio::signal::ctrl_c().await.is_ok() {
            println!("\n[WDM] Pausing download and saving checkpoint...");
            is_paused_ctrlc.store(true, Ordering::SeqCst);
            cancel_token_ctrlc.cancel();
        }
    });

    if is_yt {
        let (cancel_tx, cancel_rx) = tokio::sync::watch::channel(false);
        let ct = cancel_token.clone();
        tokio::spawn(async move {
            ct.cancelled().await;
            let _ = cancel_tx.send(true);
        });

        let res = MediaResolver::download_video(
            &url,
            out_dir.as_deref(),
            |prog| {
                print_progress(&prog);
            },
            |status| {
                if status == DownloadStatus::Connecting {
                    println!("[WDM] Resolving YouTube video streams via yt-dlp...");
                }
            },
            |meta| {
                println!("[WDM] Video: {}", meta.file_name);
                println!("[WDM] Saving to: {}\n", meta.target_file_path);
            },
            cancel_rx,
        )
        .await;

        match res {
            Ok(()) => {
                println!("\n[WDM] Download 100% Complete!");
                0
            }
            Err(Error::Canceled) => {
                println!("\n[WDM] Download paused. Run again to resume.");
                EXIT_PAUSED
            }
            Err(e) => {
                eprintln!("\n[WDM Error] {e}");
                1
            }
        }
    } else {
        let engine = match DownloadEngine::new(None, limit_bytes_per_sec, None) {
            Ok(e) => e,
            Err(e) => {
                eprintln!("[WDM Error] Failed to initialize engine: {e}");
                return 1;
            }
        };

        engine.set_on_status(|status| {
            if status == DownloadStatus::Connecting {
                println!("[WDM] Probing server headers and range capability...");
            }
        });

        engine.set_on_progress(|prog| {
            print_progress(&prog);
        });

        let mut options = DownloadRequestOptions::default();
        if let Some(s) = sha256 {
            options.expected_sha256 = Some(s);
        }

        let engine_clone = engine.clone();
        let ct_clone = cancel_token.clone();

        tokio::spawn(async move {
            ct_clone.cancelled().await;
            engine_clone.pause();
        });

        let result = engine
            .start(
                &url,
                out_dir.as_deref(),
                connections,
                cancel_token,
                Some(options),
            )
            .await;

        match result {
            Ok(()) => {
                let meta = engine.metadata();
                let path = meta
                    .as_ref()
                    .map(|m| m.target_file_path.as_str())
                    .unwrap_or("");
                println!("\n[WDM] Download 100% Complete! Saved to: {path}");
                0
            }
            Err(Error::ChecksumMismatch {
                expected,
                actual,
                kept_file,
            }) => {
                eprintln!("\n[WDM Error] SHA-256 mismatch: expected {expected}, got {actual}. Kept as '{kept_file}'");
                EXIT_CHECKSUM_MISMATCH
            }
            Err(Error::Canceled) => {
                println!("\n[WDM] Download paused. Checkpoint saved. Run again with the same URL to resume instantly.");
                EXIT_PAUSED
            }
            Err(e) => {
                eprintln!("\n[WDM Error] {e}");
                1
            }
        }
    }
}

fn print_progress(prog: &DownloadProgress) {
    let pct = prog.percentage();
    let bar_width = 36;
    let filled = ((pct / 100.0) * bar_width as f64) as usize;
    let filled = filled.min(bar_width);
    let bar = format!("{}{}", "█".repeat(filled), "░".repeat(bar_width - filled));

    let eta_str = match prog.estimated_seconds_remaining {
        Some(s) if s.is_finite() && s > 0.0 => {
            let total_secs = s as u64;
            format!("{:02}:{:02}", total_secs / 60, total_secs % 60)
        }
        _ => "--:--".to_string(),
    };

    let line = format!(
        "\rProgress: [{bar}] {pct:5.1}% | {} | {:>10} | ETA: {eta_str}",
        prog.formatted_downloaded(),
        prog.formatted_speed(),
    );
    print!("{line}");
    use std::io::Write;
    let _ = std::io::stdout().flush();
}

fn print_banner() {
    println!(
        r#"
╔══════════════════════════════════════════════════════════════╗
║             WDM - Windows Download Manager (CLI)             ║
║   High-Performance Dynamic Multi-Segment Download Engine     ║
║        Zero-Merge Direct Storage • Full Resume Engine        ║
╚══════════════════════════════════════════════════════════════╝
"#
    );
}
