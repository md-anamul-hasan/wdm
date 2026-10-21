## WDM 1.0.1

![WDM](https://raw.githubusercontent.com/md-anamul-hasan/wdm/master/docs/media/screenshot-dark.png)

### Fixed
- **YouTube and other video-site downloads with non-English titles** (for example Bengali) finished but were reported as "output file was not found". They now complete normally.
- A failed download now shows its **reason in the details panel** instead of only in a tooltip on the status badge.
- Selected-row text was unreadable in the dark theme, the "Downloading" badge was clipped, and the connection count read "1 connections".

### Changed
- **Browser extension:** it no longer asks for access to all websites at install. The popup and right-click menus work from your click alone; "Allow access" in the popup turns on automatic download takeover and the on-page video button, and can be turned off again. The same version is under review for the Chrome Web Store.
- Internal cleanup of the main view model (no behaviour change).

### Files
| File | Purpose |
|---|---|
| `WDM-Setup-1.0.1.exe` | Installer (self-contained, no .NET install needed) |
| `WDM-Extension-1.0.1.zip` | Browser extension for manual install: `chrome://extensions` > Developer mode > Load unpacked |
| `SHA256SUMS.txt` | Checksums for the files above |

Verify a download: `Get-FileHash .\WDM-Setup-1.0.1.exe -Algorithm SHA256` and compare with `SHA256SUMS.txt`.

Builds are not code-signed yet, so Windows SmartScreen may warn about an unknown publisher.

Full list of changes: [CHANGELOG.md](https://github.com/md-anamul-hasan/wdm/blob/master/CHANGELOG.md).
