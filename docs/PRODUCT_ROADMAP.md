# Product roadmap (from the audit)

Priority = user value vs. risk. Only items that fit the current architecture are proposed; none are implemented unless listed in CHANGELOG.

| # | Feature | Problem | Complexity | Security / privacy | Priority | Phase |
|---|---|---|---|---|---|---|
| 1 | Release 1.0.2 with the audit fixes + extension 1.0.1 to the Web Store | Store users currently cannot hand off (AUD-009) | Low | — | **P0** | Now |
| 2 | Encrypt stored cookies/passwords (DPAPI) with migration | AUD-013 | Medium (format migration) | Positive | P1 | 1.1 |
| 3 | Pin + checksum yt-dlp/ffmpeg; pin Actions by SHA; split signing job permissions | AUD-019/020 | Low | Positive | P1 | 1.1 |
| 4 | Write to `name.ext.wdm-part`, rename on completion | AUD-014 false-complete files | Medium (resume compat) | — | P1 | 1.1 |
| 5 | System tray (minimise, pause/resume all, active count) | App must stay open to be useful | Medium (WPF, not testable here) | No startup persistence without consent | P2 | 1.1 |
| 6 | Diagnostics page: native host registered? extension ID allowed? pipe reachable? | "WDM not found" is opaque (cause of AUD-009) | Low | — | P1 | 1.1 |
| 7 | Editable takeover rules (extensions, domains, size) | Hard-coded IDM list | Low-Med | — | P2 | 1.2 |
| 8 | Optional SHA-256 verification | Integrity | Low | — | P2 | 1.2 |
| 9 | Single-connection fallback when Range is ignored (AUD-016) | Fails instead of degrading | Low | — | P2 | 1.1 |
| 10 | Firefox package + AMO listing, after real testing | Firefox unverified | Medium | — | P2 | 1.2 |
| 11 | FlaUI smoke tests + Windows CI job running `dotnet test` on the whole solution incl. UI build | UI is unbuilt/untested in this audit | Medium | — | P1 | 1.1 |
| 12 | Categories, history search, per-download limits, proxy support | Competitive parity | Med-High | Proxy creds need item 2 | P3 | 1.3+ |
