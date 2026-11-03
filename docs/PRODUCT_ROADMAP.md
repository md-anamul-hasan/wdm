# Product roadmap (from the audit)

Priority = user value vs. risk. Only items that fit the current architecture are proposed; none are implemented unless listed in CHANGELOG.

| # | Feature | Problem | Complexity | Security / privacy | Priority | Phase |
|---|---|---|---|---|---|---|
| 1 | Release 1.0.2 with the audit fixes + extension 1.0.1 to the Web Store | Store users currently cannot hand off (AUD-009) | Low | — | **P0** | done |
| 2 | ~~Encrypt stored cookies/passwords (DPAPI) with migration~~ — **done in 1.0.2** | AUD-013 | — | Positive | done | 1.0.2 |
| 3 | ~~Pin + checksum yt-dlp/ffmpeg~~ and ~~pin Actions by SHA~~ — **done in 1.0.2** | AUD-019/020 | Low | Positive | done | 1.0.2 |
| 4 | ~~Write to `name.ext.wdm-part`~~ — **done in 1.0.2** | AUD-014 | — | — | done | 1.0.2 |
| 5 | ~~System tray (minimise to tray, pause/resume all, notification on first hide, exit)~~ — **done in Rust + Tauri Native Stack** | App must stay open to be useful | — | Positive | done | 2.0.3 |
| 6 | ~~Diagnostics~~ — **`--diagnose` done in 2.0.0** | "WDM not found" is opaque | Low | — | done | 2.0.0 |
| 7 | ~~Editable takeover rules~~ — **done in extension 2.0.0** | Hard-coded IDM list | — | — | done | 2.0.0 |
| 8 | ~~Optional SHA-256 verification~~ — **done in 2.0.0** | Integrity | — | — | done | 2.0.0 |
| 9 | ~~Single-connection fallback when Range is ignored~~ — **done in wdm-core** | Fails instead of degrading | Low | — | done | 2.0.3 |
| 10 | Firefox package + AMO listing, after real testing | Firefox unverified | Medium | — | P2 | 2.1 |
| 11 | ~~Automated CI tests running on Windows runners~~ — **done in Rust + Node CI** | UI is unbuilt/untested | Low | — | done | 2.0.3 |
| 12 | ~~Categories, history search, stream visualizer~~ — **done in React UI** | Competitive parity | — | — | done | 2.0.3 |
