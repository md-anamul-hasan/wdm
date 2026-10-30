# Changelog

## 2.0.0 - 2026-10-30

### Added
- Optional SHA-256 verification: *Add download* field, `wdm --sha256 <hex>` (exit code 2 on mismatch), `/sha256` command-line switch. The file is verified before it is renamed from `.wdm-part`; on mismatch the data is kept and nothing is published. Malformed checksums are refused before any request.
- `Wdm.NativeHost.exe --diagnose`: per-browser registration, manifest, extension-ID, host-program and "WDM running" checks with the fix for each problem (`NativeHostManifestInspector`).
- Extension 2.0.0: takeover rules (extra and blocked file types, excluded sites incl. subdomains and referrer, minimum size for unrecognised files) with a "never from this site" shortcut in the popup; rules live in `rules.js` and are unit-tested.
- Accessible names for icon-only buttons and the new fields (AUD-026, partially).

### Fixed
- IPC listener no longer stops for good after an unexpected error (previously only three exception types were survived), so browser hand-offs cannot silently stop until WDM is restarted.

### Changed
- Version 2.0.0 for the app and the extension. No data-format break: the only addition is the optional `ExpectedSha256` field in download options.
- `WdmPipe.IsListeningAsync` (used by `--diagnose`); the IPC listener ignores empty connections.

## 1.0.2 - 2026-10-30

### Fixed
- **Browser integration for Chrome Web Store installs:** the native host now authorizes the published extension ID `flnbghmpkajibfdfhbcfhkcjkhlokfgi` by default; before, only the sideloaded ID was allowed, so the store extension reported "WDM not found" unless re-registered with `--extension-id`.
- Resume no longer trusts a checkpoint whose partial file was deleted, truncated or replaced (previously a zero-filled file could be reported as complete); damaged segment tables and the checkpoint's stored target path are ignored.
- A `206` response whose `Content-Range` starts elsewhere than requested is rejected instead of being written at the wrong offset.
- Permanent HTTP errors (401/403/404/410/416...) fail immediately instead of retrying for ~46 s.
- Checkpoints are written only after the file data they describe is flushed to disk; the checkpoint is kept until the finished file is flushed.
- Request headers containing CR/LF (cookies, referer, user agent) are dropped instead of being sent.
- IPC: a client that connects and stays silent no longer blocks the listener; non-http(s) URLs are discarded on receipt.
- File-name sanitising rejects `/ \ : * ? " < > |` on every platform.
- Extension: a handoff interrupted by the MV3 service worker being suspended no longer leaves the browser download paused; it is settled on the next worker start.
- Servers that advertise but ignore byte ranges fall back to one connection; empty files download; scheduler start/stop no longer missed after sleep or a DST jump; installer/README point to the Web Store extension.
- Firefox manifest declares `data_collection_permissions` (required by AMO for new extensions).

### Security / data handling
- Cookies, user names and passwords kept for a download are encrypted with Windows DPAPI (`dpapi1:` values); plaintext from older versions is migrated on first start and dropped for completed downloads.
- Unfinished downloads are written to `<name>.wdm-part` and renamed when complete; checkpoints from 1.0.x still resume in place.
- Bundled yt-dlp (2026.10.26) and FFmpeg (n8.1.3 LGPL) are pinned with SHA-256 and verified at build time; `THIRD_PARTY_NOTICES.md` added.

### Changed
- CLI exits with code 130 when a download is paused with Ctrl+C (was 0).

### Added
- `docs/audit/*` engineering audit records, `docs/PRODUCT_ROADMAP.md`, fault-injection tests (`AuditFaultInjectionTests`) and a mocked-`chrome` extension test (`tests/extension`, run with `node --test tests/extension/background.test.mjs`).

All notable changes to WDM. Versioning follows [Semantic Versioning](https://semver.org/); the version lives in `Directory.Build.props`.

## [1.0.1] - 2026-10-21

### Added
- Chrome Web Store packaging (`scripts/package-extension.ps1`), listing assets and privacy policy; `Wdm.NativeHost --register --extension-id <id>` authorizes the store-assigned extension ID.

### Changed
- Internal: `MainViewModel` split into partial classes by concern (downloads, queue/scheduler, dialogs, filtering); no behaviour change.
- Extension: no standing host access. Site access (`<all_urls>`) is now optional and granted from the popup; the popup and right-click menus work from `activeTab` alone. Download takeover, login cookies for it and the page media button need the optional access. This avoids the Chrome Web Store broad-host-permissions review.
- Extension popup and the on-page download button now use the desktop app's palette, light and dark (system theme); the page button runs in a shadow root and can be dismissed.
- A failed download now shows its reason in the details panel instead of only in a status tooltip.

### Fixed
- Video-site downloads whose title contains non-ASCII characters (for example Bengali) finished but were reported as "output file was not found"; yt-dlp is now run with `--encoding utf-8`.

## [1.0.0] - 2026-10-21

First public release.

### Added
- Multi-connection download engine: up to 32 connections, dynamic segmentation, zero-merge writes, checkpointed resume with ETag / Last-Modified validation.
- Downloads of unknown length (chunked responses), and servers that ignore `Range` restart cleanly instead of failing.
- Queue, scheduler (start/stop times, retry, optional shutdown), batch download dialog, site grabber, clipboard sniffer.
- One speed limit shared by all downloads, persisted across restarts.
- Browser extension (Chrome, Edge, Brave; Firefox id declared) and `Wdm.NativeHost` bridge; takeover happens only after WDM confirms.
- Video-site pages through `yt-dlp`, with `ffmpeg` bundled to join video and audio streams.
- Light and dark themes, single-instance hand-off over a per-user named pipe, `Wdm.Cli` terminal downloader.
- Installer (Inno Setup, self-contained) and a GitHub Actions release workflow with SignPath code signing.

### Security
- Cookies are sent only to the host they were captured for; credentials only to the original host; nothing sensitive crosses an https to http downgrade.
- Safe file-name handling (reserved device names, trailing dots, path separators) and atomic target-file reservation.
- Corrupt `downloads.json` / `settings.json` are preserved as `.corrupt` instead of being overwritten.

### Fixed
- Selected-row text was unreadable in the dark theme.
- The "Downloading" status badge was clipped, and the connection count read "1 connections".
