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

## Application (installed 2.5.5, 5 cold starts)
- Main window handle present after 31 to 176 ms (first start slowest; this is the native window, the web content paints after).
- Idle after 5 s, `wdm-desktop.exe` alone: working set 30 to 31 MB, private bytes 7 MB, 43 threads, about 366 handles.
- Idle after 10 s, the whole tree (WDM plus the 6 WebView2 processes it starts, found by parent process): 163 MB private, 363 MB working set (the working-set sum counts shared pages more than once, so private bytes is the honest figure).

## Real internet (one PC, one home connection, 2026-11-13)
`wdm.exe <url> -c N`, 100 MiB file from `ipv4.download.thinkbroadband.com` (HTTP, Range supported), two runs per setting, every file checked for exact size. `curl` single stream to a second server managed 6.5 MB/s, so the link itself is about 6 to 7 MB/s.

| Connections | MB/s (run 1 / run 2) |
|---|---|
| 1 | 6.1 / 5.0 |
| 4 | 6.6 / 6.7 |
| 8 | 6.7 / 6.9 |
| 16 | 6.3 / 4.9 |
| 32 | 2.9 / 3.2 |

Reading: here the connection is the limit, so more connections gain little (about 10 to 20 % at 4 to 8), and 32 connections were slower, probably because the server or link throttles many parallel streams. The default of 8 is a sound choice; 32 is not a speed-up. A first test against `proof.ovh.net` completed with one connection, then every multi-connection attempt failed at the first connection (a connect timeout), which looks like that server rate-limiting repeated tests from one address; it was not reproduced on the other server.

## Large file (above 4 GiB)
Loopback server, `wdm.exe -c 8`, 5,368,709,620 bytes (5.0 GiB): finished in 11 s (about 250 MB/s, disk-bound), exact size, and 200 bytes sampled across the file (start, both sides of the 2 GiB and 4 GiB boundaries, end) all matched the expected pattern. Not a full hash of the file.

## Not benchmarked
Pause and resume handle leaks, 50 parallel downloads, speed-limiter accuracy, other downloaders, CPU use, UI frame times.
