## WDM 2.0.0

A major release: verify what you download, understand why browser integration does or does not work, and control exactly which browser downloads WDM takes over. Everything from 1.0.x keeps working: your download list, settings and half-finished downloads are read as they are.

### New
- **SHA-256 verification.** Paste the checksum a website publishes into *Add download* (new optional field), or use `wdm <url> --sha256 <hex>` / `/sha256 <hex>`. WDM checks the finished file before it gets its real name. A mismatch never publishes the file: it is kept as `name.wdm-part`, the download shows an error with both hashes, and the command line exits with code 2. A malformed checksum is refused before any data is downloaded.
- **Browser integration diagnostics.** Run `Wdm.NativeHost.exe --diagnose` for a plain-language check of every browser: is the host registered, does the manifest point to an existing `Wdm.NativeHost.exe`, is your extension allowed, is WDM running, and the exact command that fixes what is wrong.
- **Takeover rules in the extension (2.0.0).** In the extension popup, *Takeover rules* lets you add file types WDM should take over, file types it should leave to the browser, sites it should never take over (subdomains included), and the size above which unrecognised files are taken over. One click on *Never take over downloads from this site* does it for the page you are on.
- **Screen reader labels.** Icon-only buttons (queue, site grabber, scheduler, theme, options, more, dialog close) and the new fields have accessible names.

### Upgrading
- Install over 1.0.x; nothing to migrate. Downloads paused in 1.0.x resume.
- The Chrome Web Store extension updates itself to 2.0.0. It works with WDM 1.0.2 or newer; install 2.0.0 to get `--diagnose`.
- Downgrading to 1.0.x later is safe; the new checksum field is simply ignored there.

### Known limitations
- No system tray yet (planned for 2.1).
- The download is not verified while it is running, only when complete; very large files take a few seconds to hash at the end.
- Firefox is not yet tested.

### Files
| File | Purpose |
|---|---|
| `WDM-Setup-2.0.0.exe` | Installer (self-contained, no .NET install needed) |
| `WDM-Extension-2.0.0.zip` | Browser extension for manual install |
| `SHA256SUMS.txt` | Checksums |

Verify with the feature above: `Get-FileHash .\WDM-Setup-2.0.0.exe -Algorithm SHA256` and compare with `SHA256SUMS.txt`.

Builds are not code-signed yet, so Windows SmartScreen may warn about an unknown publisher.
