## WDM 2.5.5

Maintenance release for the native (Rust + Tauri) edition. The app and the browser extension are both **2.5.5**. Install over 2.5.0 or 2.0.x: your download list, settings and saved (encrypted) cookies carry over unchanged.

### What is new
- **Much smaller programs.** Rebuilt with the MSVC toolchain: the window is 15.3 MB (was 29.2), the browser host 1.4 MB (was 11.2) and the command-line tool 6.1 MB (was 11.9). The installer is 65 MB because it still bundles yt-dlp and ffmpeg for video sites.
- **No duplicate programs.** The installer no longer copies the program three times under the old `Wdm.*.exe` names, and removes those copies when you update. Browser integration is re-registered automatically if you leave that option ticked.
- **WebView2 check.** Setup tells you where to get the Microsoft Edge WebView2 Runtime if it is missing (the window needs it).
- No change to downloads, settings files, permissions or the browser protocol.

### Extension 2.5.5
Same behaviour and permissions as 2.5.0; the version matches the app. It works with this and older WDM versions.

### Checked before release
Rust format, lint (warnings as errors) and the full test suite; a real-app end-to-end run (24 checks); native-host checks (7); the extension and host in Microsoft Edge; install, update over an existing install and uninstall of the installer. Not tested: Brave (not installed on the test PC) and loading the extension into current Google Chrome by automation (Chrome 157 blocks it; registration was checked).

### Files
| File | Purpose |
|---|---|
| `WDM-Setup-2.5.5.exe` | Installer (no .NET install needed) |
| `WDM-Extension-2.5.5.zip` | Browser extension for manual install |
| `SHA256SUMS.txt` | Checksums |
| `WDM-SBOM-2.5.5.zip` | CycloneDX software bill of materials |

Verify with `Get-FileHash .\WDM-Setup-2.5.5.exe -Algorithm SHA256` and compare with `SHA256SUMS.txt`.

Builds are not code-signed yet, so Windows SmartScreen may warn about an unknown publisher.
