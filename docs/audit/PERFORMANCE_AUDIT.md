# Performance audit (loopback only)

**Method:** `tests/Wdm.Tests/AuditBenchmark.cs`, run with `WDM_BENCH=1`. 256 MiB random file, in-process `HttpListener` server and WDM engine in the same process, Linux, 4 vCPU, 16 GB RAM, temp dir on the sandbox disk. Median of 3 runs per setting; two full passes shown.

| Connections | Pass 1 MB/s | Pass 2 MB/s |
|---|---|---|
| 1 | 264 | 298 |
| 2 | 389 | 402 |
| 4 | 376 | 409 |
| 8 | 411 | 412 |
| 16 | 389 | 422 |
| 32 | 396 | 391 |

## What this does and does not show
- Engine + disk overhead is small: one stream sustains ~270–300 MB/s, 2+ streams plateau ≈ 400 MB/s where the shared CPU/server becomes the limit. Going beyond 2–4 connections gave **no** gain locally, which is expected: loopback has no per-connection throttling or latency. **Nothing here predicts real internet gains** (the case for multi-connection depends on server-side per-connection limits).
- First attempt of the benchmark was too noisy to use (e.g. 4 connections 2.1 s vs 8 connections 0.76 s); results above are from repeated runs after the machine settled. Treat ±15 % as noise.
- **Memory:** managed heap after a forced GC stayed at ≈258 MB (the 256 MB test buffer) across 18 consecutive downloads ⇒ no managed leak in the engine. Process working set varied 1.1–3.7 GB between runs because of the in-process server and lazy GC trimming; it is not a leak indicator.
- Not measured: CPU, thread counts, handle leaks, UI responsiveness, speed-limiter accuracy beyond the existing unit tests, 50 parallel downloads (10 were tested for correctness), Windows/NTFS behaviour, comparison with a browser downloader.

## Suggested next measurements (Windows)
`typeperf`/Process Explorer during an 8-connection download of a 2 GB file against a throttled server (e.g. nginx `limit_rate` per connection), dotnet-counters for GC/thread-pool, handle count before/after 100 pause/resume cycles.
