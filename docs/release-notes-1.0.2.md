## WDM 1.0.2

### Fixed
- **Browser extension from the Chrome Web Store now connects to WDM** (previously only the manually loaded copy was authorized, so the store version said "WDM not found").
- Resuming a download whose partial file was deleted or damaged no longer produces a corrupt file reported as complete.
- Servers that send the wrong byte range are detected instead of corrupting the file; servers that advertise but ignore ranges now fall back to a single connection; empty files download.
- Expired or forbidden links (HTTP 401/403/404/410) fail immediately instead of retrying for ~45 seconds.
- Progress is saved only after the data is on disk, so a power loss cannot resume over missing data.
- The scheduler no longer misses its start/stop time after sleep, hibernate or a daylight-saving change.
- Hardened browser/IPC input handling (control characters in headers, stalled clients, non-http links).
- Extension 1.0.1: a download paused by WDM's takeover can no longer stay paused if the browser suspends the extension.

### Files
| File | Purpose |
|---|---|
| `WDM-Setup-1.0.2.exe` | Installer (self-contained) |
| `WDM-Extension-1.0.2.zip` | Browser extension for manual install |
| `SHA256SUMS.txt` | Checksums |

Builds are not code-signed yet, so Windows SmartScreen may warn about an unknown publisher.
