## WDM 2.0.3

A fixes release. Install over 2.0.x; nothing to migrate.

### Fixed
- **Save failures are no longer silent.** If the disk refuses to save your download list or settings (full disk, read-only folder, antivirus lock), the status bar says "Could not save your download list" (or settings) until a later save works.
- **Keyboard focus is visible** on the sidebar items, filter chips and check boxes. The ring appears for keyboard navigation only.

### Release process
- The installer is built with a pinned Inno Setup (6.7.1).

### Extension 2.0.3
Same behaviour and permissions as the 2.0.1 build on the Chrome Web Store. The version only matches the app.

### Files
| File | Purpose |
|---|---|
| `WDM-Setup-2.0.3.exe` | Installer (self-contained, no .NET install needed) |
| `WDM-Extension-2.0.3.zip` | Browser extension for manual install |
| `SHA256SUMS.txt` | Checksums |

Verify with `Get-FileHash .\WDM-Setup-2.0.3.exe -Algorithm SHA256` and compare with `SHA256SUMS.txt`.

Builds are not code-signed yet, so Windows SmartScreen may warn about an unknown publisher.
