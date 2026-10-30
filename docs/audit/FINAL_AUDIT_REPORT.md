# WDM engineering audit — final report

Environment: Linux sandbox, .NET SDK 10.0.114, Node 22. **No Windows, no running WPF, no real browsers, no Inno Setup/PowerShell.** Everything marked "verified" below ran here; everything else is labelled.

## Executive summary
- **Initial condition:** builds clean, 45 tests (2 failing only on Linux). Solid, compact engine. No telemetry code found.
- **Most important findings:** (1) the published Web Store extension was not authorized by the native host, so it could not hand off downloads; (2) resume trusted a checkpoint blindly and could finish a zero-filled file as "Completed"; (3) a wrong `Content-Range` was written at the wrong offset; (4) checkpoints were written before the data was flushed; (5) scheduler triggers could be missed after sleep/DST; (6) the MV3 worker could leave a browser download paused forever.
- **All of these are fixed in source with regression tests.** Fixes are not released yet.
- **Remaining risks:** plaintext cookies/passwords at rest, unverified bundled tools (yt-dlp/ffmpeg), preallocated "looks complete" files, no accessibility work, Firefox untested, nothing runtime-tested on Windows.

## Before / after (measured)
| | Before | After |
|---|---|---|
| Release build, whole solution (`dotnet build Wdm.slnx -c Release -p:EnableWindowsTargeting=true`) | not run | 0 warnings, 0 errors incl. Wdm.Ui (compile only; UI never run) |
| .NET tests | 45: 43 pass, 2 fail | 119: 119 pass; 8+ consecutive full runs after the last change, 0 failures |
| Extension tests | none | 8 pass (mocked `chrome`) |
| Core line / branch coverage | not measured | 74.0 % / 68.7 % |
| NuGet vulnerabilities | — | none (`dotnet list package --vulnerable --include-transitive`) |
| Loopback throughput, 256 MiB | — | 1 conn ≈ 270–300 MB/s; 2+ ≈ 400 MB/s; no managed leak across 18 downloads |

## Validation record
| Validation | Result | Evidence |
|---|---|---|
| Restore / Release build (5 projects) | Pass (UI compile-only) | command above |
| Unit + integration + fault injection | Pass 119/119 | `dotnet test tests/Wdm.Tests -c Release` |
| Extension logic | Pass 8/8 (mock) | `node --test tests/extension/background.test.mjs` |
| Dependency vulnerabilities | Pass | `dotnet list ... --vulnerable` |
| Security | Source review + exploit tests; no SAST tool | SECURITY_AUDIT.md |
| Performance | Loopback only | PERFORMANCE_AUDIT.md |
| Windows CI (run 41060931118, `windows-latest`, workflow_dispatch on the branch) | **Pass**: solution build incl. WPF UI, 119 .NET tests, extension tests, pinned yt-dlp/ffmpeg download+verify, Inno Setup installer build, packaging (108 MB artifact); signing/publish skipped by design | https://github.com/md-anamul-hasan/wdm/actions/runs/41060931118 |
| WPF UI runtime, installer install/upgrade/uninstall, real DPAPI | **Not verified** (CI builds but does not run them) | needs a Windows machine |
| Real Chrome/Edge/Brave/Firefox + native host | **Not verified** | needs Windows |
| Power loss, disk full, access denied | **Not verified** | needs Windows |

## Status by area
| Area | Status |
|---|---|
| Engine resume / data integrity | Improved, tested vs local servers; no crash testing |
| Segmentation / concurrency | Verified: 1–32 connections byte-identical, 10 parallel, 6 pause/resume cycles |
| IPC / native host | Improved; Linux pipes tested; Windows ACLs unverified |
| Extension takeover | Verified with mocks; not in a browser |
| Web Store release | Reviewed; ID bug fixed in source — **needs release** |
| Firefox | Not verified |
| Secrets at rest | DPAPI wiring tested with a fake cipher; real DPAPI **unverified** (Windows-only) |
| `.wdm-part` | Tested incl. legacy 1.0.x checkpoints, name collisions, crash-before-rename; Windows file-locking behaviour (AV) unverified |
| CLI | Exit codes verified by running it: paused 130, completed 0, errors 1 |
| Scheduler | Fixed + unit-tested; timer wiring compile-only |
| CI / release | Hardened (SHA pins, read-only default token, extension tests); **executed green on Windows** (dispatch run, publish step skipped) |
| UI/UX, accessibility | Static review only |
| Pinned tools | yt-dlp 2026.10.26 + FFmpeg n8.1.3 LGPL pinned by SHA-256; `fetch-tools.ps1` run under PowerShell 7 (fresh / rerun / tampered / wrong hash); all 6 scripts parse; `package-extension.ps1` produced a valid store zip |
| Docs | All `docs/audit/*`, PRODUCT_ROADMAP, SECURITY.md, CONTRIBUTING.md, release notes 1.0.2 written. **Not written:** USER_GUIDE, FAQ, TROUBLESHOOTING, CLI docs (no verified Windows behaviour to describe) |
| Feature enrichment | Roadmap only; nothing implemented beyond fixes |

Everything you need to do next, with steps: **OWNER_ACTION_PLAN.md**. Per-issue detail: **ISSUE_REGISTER.md**.
