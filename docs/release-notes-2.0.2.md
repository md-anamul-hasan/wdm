## WDM 2.0.2

A fixes release. Nothing to migrate: install over 2.0.x.

### Fixed
- **Screen readers.** The toolbar buttons Add URL, Resume, Pause and Delete, the speed-limit button and every input control (search, folders, connection sliders, speed limit, user agent, scheduler times, site grabber address, batch addresses, download list) now have accessible names. A test fails if a button or input in the main window has none.
- **Queue status.** The sidebar showed a doubled bullet while the queue ran ("● ● Queue Active").

### Extension 2.0.2
Same behaviour and permissions as the 2.0.1 build on the Chrome Web Store (popup wraps long site names). The version only matches the app.

### Files
| File | Purpose |
|---|---|
| `WDM-Setup-2.0.2.exe` | Installer (self-contained, no .NET install needed) |
| `WDM-Extension-2.0.2.zip` | Browser extension for manual install |
| `SHA256SUMS.txt` | Checksums |

Verify with `Get-FileHash .\WDM-Setup-2.0.2.exe -Algorithm SHA256` and compare with `SHA256SUMS.txt`.

Builds are not code-signed yet, so Windows SmartScreen may warn about an unknown publisher.
