# Changelog

All notable changes to WDM. Versioning follows [Semantic Versioning](https://semver.org/); the version lives in `Directory.Build.props`.

## [Unreleased]

### Changed
- A failed download now shows its reason in the details panel instead of only in a status tooltip.

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
