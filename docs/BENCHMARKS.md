# Benchmarks (WDM 2.5.5, Rust + Tauri)

Measured 2026-11-13 on the development PC (Windows 11 Pro, MSVC release build). Single machine, loopback only, small sample: treat as ±15 % and as a regression baseline, not a promise about real networks.

## Engine throughput
`cargo test --release -p wdm-core --test bench -- --ignored --nocapture` (256 MiB file, in-process server and engine share the CPU; median of 3; every run checked byte-exact).

| Connections | MB/s |
|---|---|
| 1 | 736 |
| 2 | 760 |
| 4 | 640 |
| 8 | 402 |
| 16 | 367 |
| 32 | 377 |

Reading: loopback has no per-connection limit or latency, so extra connections cannot help and cost some CPU (the server runs in the same process). This shows engine and disk overhead are small; it does **not** predict internet gains, which depend on server-side per-connection throttling.

## Binaries (release)
| File | MSVC (shipped) | Earlier GNU build |
|---|---|---|
| `wdm-desktop.exe` | 15.3 MB | 29.2 MB |
| `wdm.exe` (CLI) | 6.1 MB | 11.9 MB |
| `wdm-nativehost.exe` | 1.4 MB | 11.2 MB |
| Installer (with yt-dlp 22.5 MB and ffmpeg 125.7 MB unpacked) | 65.2 MB | - |

## Application (5 cold starts of the installed app)
- Main window handle present after 31 to 176 ms (first start slowest; this is the native window, the web content paints after).
- Idle after 5 s: working set 30 to 31 MB, private bytes 7 MB, 43 threads, about 366 handles (the WDM process only).
- Not measured: WebView2 memory attributable to WDM (other apps on this PC also run WebView2, so a process-wide sum is meaningless), CPU use, UI frame times, pause/resume handle leaks, comparison with other downloaders.

## Not benchmarked
Real-internet throughput, 2 GB+ files on NTFS, 50 parallel downloads, speed-limiter accuracy.
