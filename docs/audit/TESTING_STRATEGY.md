# Testing strategy

## Layers (what exists now)
| Layer | Where | Count | Runs on |
|---|---|---|---|
| Unit | `tests/Wdm.Tests` (segmenter, writer, resume, limiter, args, settings, schedule) | ~50 | any OS |
| Integration (in-process HTTP server on loopback) | `IntegrationDownloadTests`, `RegressionTests`, `AuditFaultInjectionTests`, `AuditStressTests` | ~50 | any OS |
| Extension logic (mocked `chrome`) | `tests/extension/*.test.mjs` (background handoff, takeover rules) | 21 | Node ≥ 20 |
| UI / end-to-end | none | 0 | needs Windows (FlaUI planned) |
| Real browser + native host | none | 0 | needs Windows + browser |

144 .NET tests + 21 Node tests; all pass; no external network use; each test uses its own temp dir and port.

## Commands (verified in this audit)
```
dotnet test tests/Wdm.Tests -c Release
dotnet test tests/Wdm.Tests -c Release --collect:"XPlat Code Coverage"
node --test "tests/extension/*.test.mjs"
WDM_BENCH=1 dotnet test tests/Wdm.Tests -c Release --filter AuditBenchmark --logger "console;verbosity=detailed"
dotnet build Wdm.slnx -c Release -p:EnableWindowsTargeting=true     # compiles the WPF UI on Linux/macOS (compile only)
```

## Coverage (Coverlet, Core only, Linux, all 106 tests)
Line **75.9 %**, branch **70.6 %** (65.1 % / 60.4 % when the audit started adding tests; the original suite alone was not measured). Weakest: `MediaResolver` process/yt-dlp path (only parsing and URL matching are tested), `AppSettings` Load/Save (needs `WDM_DATA_DIR` isolation), `DownloadProgress` formatting. Engine, worker, segmenter, writer, pipe, request helper: 85–100 %.

## Flakiness
Full suite run 14 times in a row after the last fix: 0 failures. One earlier flake (`RepeatedPauseAndResume`) was a test-design flaw (rate limiter bucket let a resumed run finish before the pause landed), fixed in the test.

## Fault-injection matrix (all executed)
Wrong `Content-Range` · 401/403/404/410 mid-download · 429/500/503 then success · mid-body disconnect · Range advertised but ignored · empty file (HEAD ok / HEAD rejected) · unknown length chunked · deleted/truncated partial · tampered path · out-of-range segments · CRLF header values · silent IPC client · non-http URLs over IPC · hostile 8 MB page for the grabber regex · 1/2/8/16/32 connections byte-identical on 24 MB · 6 pause/resume cycles · 10 parallel downloads.

## Not covered (needs Windows)
Disk-full, access-denied, power-loss; sleep/DST for the scheduler end-to-end (the decision function is unit-tested, the timer wiring is not); real native messaging framing against Chrome; UI state transitions; installer/uninstaller; PowerShell scripts.

## Rules for new tests
No internet; no shared global state (pipe tests go in collection `NamedPipe`); every fixed bug gets a test named after its issue ID; byte-compare with SHA-256, never just file length.

## Lessons from the v2.0.0 tag run
A pipe test passed on a Windows dry run and failed on the tag run (same code). Cause: tests shared one real pipe name and one failure leaked a listener that broke the next tests. Rule: tests that use IPC get their own pipe name (`PipeFixture`) and always stop their listener; "green once" on a new concurrency test is not evidence, run it several times.
