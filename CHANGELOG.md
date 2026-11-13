# Changelog

## 2.5.5 - 2026-11-13

Maintenance release; the app and the browser extension are both 2.5.5. Versions 2.5.1 to 2.5.4 were never published. No change to download behaviour, data formats, permissions or the native-messaging protocol.

### Changed
- Setup checks for the Microsoft Edge WebView2 Runtime (the window needs it) and says where to get it when it is missing.
- Packaging no longer ships the duplicate `Wdm.Ui.exe`, `Wdm.Cli.exe` and `Wdm.NativeHost.exe` copies; setup deletes the old ones and, with browser integration selected, re-registers the host at `wdm-nativehost.exe`.
- Built with the MSVC toolchain (`rust-toolchain.toml`): no linker warning, and smaller programs (window 29.2 to 15.3 MB, native host 11.2 to 1.4 MB, command line 11.9 to 6.1 MB).
- `cargo fmt --check` is clean.

### Added
- Docs: CycloneDX SBOM in `docs/sbom/`, `docs/BENCHMARKS.md`.
- Build and QA: `scripts/sign.ps1` (local Authenticode signing), `scripts/make-sbom.ps1`, `scripts/qa/browsers.mjs` (extension and native host through Edge; Chrome and Brave registration), an engine throughput benchmark, and a local hash-verified cache for ffmpeg. The pinned upstream ffmpeg download now returns 404, so `tools.lock.json` points at a mirror of the same build (release `tools-ffmpeg-n8.1.3`); the `ffmpeg.exe` SHA-256 is unchanged.

### Still open
- Builds are not code-signed until a certificate or SignPath approval exists.
- Owner decision pending: fetch `yt-dlp` and `ffmpeg` on first use instead of bundling them (installer about 65 MB to about 10 MB; video-site downloads would then need internet and a first-use download).

## 2.5.0 - 2026-11-03

Native stack: Rust engine, native-messaging host and CLI; Tauri v2 + React window. Data formats (`downloads.json`, `settings.json`, `.wdm-state`, DPAPI secrets, pipe `WDM.Ipc.<user>`, native-messaging frames) are unchanged, so 2.0.x profiles load as they are.

### Added
- Connections per download from the Add dialog (`Connections` in the request and, optionally, in the stored options; absent in files that do not set it).
- Auto-retry of network and disk failures (5 attempts, 30 s apart) and the failure reason in the row.
- Shutdown-when-finished fires when the queue drains, with an abortable 60 s prompt.
- Single-instance guard (`Local\WDM.SingleInstance.<user>`); a second launch forwards its request.
- `crates/wdm-desktop/capabilities/default.json` (events and folder picker only), a content security policy, and 12 new tests (desktop state, native host).
- Installer removes the .NET 2.0.x files (`[InstallDelete]`).

### Fixed (found by QA of the migrated build, driven through the real app)
- Release builds opened an empty window: `custom-protocol` was not enabled, so the binary tried the Vite dev server.
- No capability file: the window never received status or progress events.
- Add dialog downloads were listed but never started with the default settings.
- Status and segment data reached the UI as numbers or missing fields (`DownloadStatus` serializes as the legacy number, segment state is `serde(skip)`): the UI now gets names, speed and byte counts through `progress_json`.
- Path traversal: the destination folder was derived from an unsanitised file name (`..\..\x.exe`).
- Batch requests kept only the first address; the "external download" event was emitted before the download was added.
- Same-URL requests created duplicates and orphaned a running engine; pause/resume could race on the engine map and mark a restarted download Paused.
- Interrupted downloads loaded as Connecting/Downloading with no engine and could not be resumed.
- Start queue ignored the concurrency limit; settings (dynamic split, timeout, user agent) were not applied; the completion time was the creation time.
- The Add dialog kept the previous address and ignored the connection count; `open_file`/`open_folder` accepted any path; the native host could start a program outside its folder in release builds.
- CI and release compiled the desktop crate before the UI existed.

### Changed
- Extension 2.5.0: same code as 2.0.3; the version only matches the app.
- README test counts and tray notes corrected.

## 2.0.3 - 2026-11-02

### Fixed
- The status bar now says "Could not save your download list" (or settings) when the disk refuses a write, instead of failing silently (AUD-031).
- Keyboard focus is visible on the sidebar items, filter chips and check boxes (AUD-027).

### Changed
- Release workflow installs Inno Setup 6.7.1 explicitly (AUD-020).
- Audit register: AUD-015, 018 and 025 accepted, AUD-021 verified, AUD-028 deferred to the tray feature (roadmap #5), AUD-020 closed.
- Extension 2.0.3: same code as 2.0.1; the version only matches the app.

## 2.0.2 - 2026-11-02

### Fixed
- Screen readers: the toolbar buttons Add URL, Resume, Pause and Delete and the speed-limit button now have accessible names, and every input control (search, folders, connection sliders, speed limit, user agent, scheduler times, site grabber address, batch addresses, download list) has one; `XamlAccessibilityTests` fails when a button or input in `MainWindow.xaml` has none (AUD-026, completed for the main window).
- The sidebar showed a doubled bullet while the queue ran ("● ● Queue Active"): the status text no longer carries its own bullet.

### Changed
- Extension 2.0.2 (includes the 2.0.1 Chrome Web Store build; 2.0.2 only aligns its version with the app): the popup's "Never take over downloads from <site>" button wraps long host names instead of overflowing; tighter spacing in the rules section.
- `scripts/make-store-media.ps1`: store screenshots now show the takeover rules (replacing the batch screenshot), the marquee mentions rules and SHA-256, and the script checks that every source image exists before it clears `store/chrome`.
- New `scripts/capture-extension-screens.mjs` renders the real popup (light and dark) with Playwright for those screenshots; it also fails on any popup script error.

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
