## WDM 1.0.0

A free, open-source, multi-connection download manager for Windows 10/11 (64-bit), with browser integration.

![WDM](https://raw.githubusercontent.com/md-anamul-hasan/wdm/master/docs/media/screenshot-dark.png)

### Highlights
- Splits a download across up to 32 connections and resumes interrupted ones.
- Queue, scheduler, speed limit shared by all downloads, batch links and site grabber.
- Chrome / Edge / Brave extension that takes over a download only after WDM confirms it.
- Video-site pages through bundled `yt-dlp` and `ffmpeg`.
- No telemetry, no update check. MIT license.

### Files
| File | Purpose |
|---|---|
| `WDM-Setup-1.0.0.exe` | Installer (self-contained, no .NET install needed) |
| `WDM-Extension-1.0.0.zip` | Browser extension for manual install: `chrome://extensions` > Developer mode > Load unpacked |
| `SHA256SUMS.txt` | Checksums for the files above |

Verify a download: `Get-FileHash .\WDM-Setup-1.0.0.exe -Algorithm SHA256` and compare with `SHA256SUMS.txt`.

### Install
1. Run the installer and keep "Enable browser integration" ticked.
2. Load the extension from the install folder's `extension` directory, or from the zip.

Full list of changes: [CHANGELOG.md](https://github.com/md-anamul-hasan/wdm/blob/master/CHANGELOG.md).
