## WDM 2.5.0

WDM now runs on a native stack: a **Rust** download engine, native-messaging host and command-line tool, with a **Tauri v2** window (React + TypeScript) in place of the .NET/WPF app. Install over 2.0.x: your download list, settings and saved (encrypted) cookies carry over unchanged, and the installer removes the old .NET files.

### What is new
- **Native engine**, same behaviour as 2.0.x: up to 32 connections with middle-split, zero-merge writes, resume with ETag/Last-Modified checks, SHA-256 verification, speed limiter, DPAPI-protected cookies, site grabber, `yt-dlp` pages.
- **Connections per download** are honoured from the Add dialog.
- **Auto-retry** for network and disk errors: up to 5 attempts, 30 seconds apart. A failed download shows its reason in the row.
- **Shut down when finished** triggers when the whole queue is done (not at the stop time) and shows a Windows 60-second countdown with a Cancel button.
- **Single instance**: a second launch (shortcut, browser, command line) brings the running window forward and hands over its download.
- **Accessibility**: every button, field and dialog has an accessible name, rows are keyboard-focusable, progress bars are announced.

### Fixed during the migration QA
- Downloads started from the Add dialog now start (with the default settings they were only listed).
- Status, speed and per-stream details reach the window correctly (they were sent as numbers or left out).
- A file name sent by a website or the command line can no longer move a download out of the chosen folder.
- A browser batch keeps every address (only the first was kept); several addresses are queued when the dialog is enabled.
- The same address is reused instead of listed twice; pause, resume and delete no longer race.
- Downloads that were running when WDM stopped or crashed come back as Paused and can be resumed.
- Start queue honours "simultaneous downloads"; the network timeout, user agent and dynamic split settings are applied.
- Open file and Open folder only work for files in the list; the window runs with a content security policy and the minimum permissions.
- A release build now shows its interface (a plain `cargo build` looked for a development server).

### Known differences from 2.0.x
- No balloon notification the first time WDM is minimised to the tray; the tray menu is unchanged.

### Extension 2.5.0
Same behaviour and permissions as 2.0.3; the version only matches the app. It works with WDM 2.5.0 and with older versions of WDM.

### Files
| File | Purpose |
|---|---|
| `WDM-Setup-2.5.0.exe` | Installer (no .NET install needed) |
| `WDM-Extension-2.5.0.zip` | Browser extension for manual install |
| `SHA256SUMS.txt` | Checksums |

Verify with `Get-FileHash .\WDM-Setup-2.5.0.exe -Algorithm SHA256` and compare with `SHA256SUMS.txt`.

Builds are not code-signed yet, so Windows SmartScreen may warn about an unknown publisher.
