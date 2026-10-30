# Owner action plan

Things only you can do (accounts, Windows, publishing, decisions). Ordered by priority. Commands are PowerShell on Windows unless noted.

## A. Release the fixes (P0 — Web Store users are currently broken)
1. **Review & merge** branch `claude/pensive-cray-kuddss` (no PR was opened; open one on GitHub).
2. **Windows verification before tagging** (≈30 min):
   ```powershell
   dotnet restore Wdm.slnx
   dotnet build Wdm.slnx -c Release
   dotnet test Wdm.slnx -c Release
   node --test tests/extension/background.test.mjs
   ```
   Expect 119 .NET tests + 8 Node tests green. Then launch `src\Wdm.Ui\bin\Release\net10.0-windows\Wdm.Ui.exe` and download a file with 8 connections, pause, resume.
3. **Build the installer locally once**: `.\scripts\build-installer.ps1` (needs Inno Setup 6: `winget install JRSoftware.InnoSetup`). Install it on a clean Windows VM; confirm Start-menu entry, then check `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.wdm.download` and open `%APPDATA%\WDM\NativeMessaging\com.wdm.download.json`: `allowed_origins` must list **both** `kdjgcdbahccolkkagbckkchfpcchcfne` and `flnbghmpkajibfdfhbcfhkcjkhlokfgi`.
4. **Upgrade test:** install 1.0.1 first, then 1.0.2 over it; downloads list and settings in `%APPDATA%\WDM` must survive. **Uninstall test:** `%APPDATA%\WDM` and your download folder must remain; the registry keys above must be gone.
5. **Tag and release (v1.0.2):** the tag run (41089440372) passed build, 119 tests, extension tests, pinned tools and the installer build, then the publish step failed because the release already existed (created in the web UI). Recover with **one** of:
   - **A (recommended, ~5 min):** on GitHub open Releases → `v1.0.2` → *Delete* (this removes the release, **not** the tag) → Actions → run 41089440372 → *Re-run failed jobs*. The workflow rebuilds and creates the release with the installer, extension zip and `SHA256SUMS.txt`.
   - **B (no rebuild):** Actions → run 41089440372 → Artifacts → download `release` (zip) → unzip → Releases → `v1.0.2` → Edit → attach `WDM-Setup-1.0.2.exe`, `WDM-Extension-1.0.2.zip`, `SHA256SUMS.txt`; title `WDM 1.0.2`; paste `docs/release-notes-1.0.2.md`; Publish.
   Either way verify: `Get-FileHash .\WDM-Setup-1.0.2.exe -Algorithm SHA256` equals the line in `SHA256SUMS.txt`. Future tags no longer have this problem once the workflow fix (AUD-032) is merged. If you find a Windows problem in the checks below, publish 1.0.3 rather than moving the tag.
6. **Store update:** run `.\scripts\package-extension.ps1` (gives `dist\store\WDM-Extension-Chrome-1.0.1.zip`), upload in the Chrome Web Store dashboard → Package → *Upload new package* → submit for review. Version 1.0.1 > 1.0.0 as required.
7. **Immediate workaround for existing users** (until 1.0.2): `& "$env:LOCALAPPDATA\Programs\WDM\Wdm.NativeHost.exe" --register --extension-id flnbghmpkajibfdfhbcfhkcjkhlokfgi` (adjust path if installed per-machine), then restart the browser. Put this in the release notes/FAQ.

## B. Test what I could not (needs Windows + browsers)
| # | Test | How | Pass criteria |
|---|---|---|---|
| 1 | Store extension handoff | Install the store extension in Chrome after step A3; popup → "Connected v…" | Green pill; right-click link → "Download with WDM" opens WDM |
| 2 | Takeover | Popup → Allow access; click a .zip link | Browser entry disappears, WDM downloads it |
| 3 | WDM not running | Close WDM; click a link | WDM starts and downloads (host waits ≤10 s) |
| 4 | WDM unavailable | Rename `Wdm.Ui.exe` temporarily | Browser keeps its own download (resumes) |
| 5 | Edge / Brave | repeat 1–3 | same |
| 6 | **Firefox** | `about:debugging` → Load Temporary Add-on → `extension\manifest.json`; repeat 1–3 | Record result in `docs/audit/BROWSER_COMPATIBILITY.md`. Until done, don't claim Firefox support |
| 7 | Crash resume | Start 2 GB download, kill `Wdm.Ui.exe` in Task Manager, restart, resume | Final SHA-256 equals the source's (`Get-FileHash`) |
| 8 | Disk full | Download to a tiny VHD | Clear error, partial + `.wdm-state` kept |
| 9 | Scheduler | Set start 2 min ahead, put PC to sleep 3 min, wake | Queue starts on wake (≤30 min late) |
| 10 | Accessibility | Run Accessibility Insights (FastPass) on the main window; Tab through everything; try Narrator on icon-only buttons | File findings for AUD-026/027 |
| 11 | High DPI | 125/150/200 % scaling | no clipped text |

## C. Decisions (all resolved — recorded for the audit trail)
| Decision | Outcome |
|---|---|
| Encrypt stored cookies/passwords; stop storing for completed downloads | Yes → implemented (DPAPI, migration, dropped on completion) |
| `.wdm-part` while downloading | Yes → implemented; 1.0.x partials still resume |
| CLI exit 130 when paused | Yes → implemented and verified |
| Pin yt-dlp/ffmpeg | Done: yt-dlp 2026.10.26, FFmpeg n8.1.3 LGPL (immutable BtbN autobuild tag), SHA-256 verified |
| Firefox `data_collection_permissions` | Kept as `none` (open-source, data only goes to the local app); revisit if a remote endpoint is ever added |
| SECURITY.md reporting | Still **your action**: enable GitHub private vulnerability reporting (Settings → Code security) |

### Extra Windows checks for the new behaviour (add to section B)
| # | Test | Pass criteria |
|---|---|---|
| 12 | DPAPI round trip | Start a download with a cookie (browser handoff), pause, close WDM. Open `%APPDATA%\WDM\downloads.json` and the `*.wdm-state`: values start with `dpapi1:`, no readable cookie. Reopen WDM, resume: the logged-in download continues |
| 13 | Migration | Put a 1.0.1 `downloads.json` with a plaintext cookie in `%APPDATA%\WDM`, start 1.0.2: file no longer contains the cookie text |
| 14 | `.wdm-part` | During a download the folder has `name.ext.wdm-part` + `.wdm-state` and no `name.ext`; after completion only `name.ext`. Antivirus holding the file at rename time should end as an error you can retry, not data loss |
| 15 | Upgrade of a paused 1.0.1 download | Pause in 1.0.1, install 1.0.2, resume: finishes in place, SHA-256 matches |
| 16 | Delete download + file | Removes `.wdm-part`, `.wdm-state` and the file |
| 17 | Installer | Contains `ffmpeg.exe`, `ffmpeg-LICENSE.txt`, `yt-dlp.exe`, `THIRD_PARTY_NOTICES.md`, `LICENSE`; a YouTube download that needs merging works |

## D. Repo housekeeping
- **Done in the repo:** `.github/dependabot.yml` (weekly NuGet + GitHub Actions PRs) and `.github/workflows/ci.yml` (build + all tests on every pull request, including Dependabot's).
- **Repository settings (only the owner can change; one-time):**
  ```powershell
  gh api -X PUT repos/md-anamul-hasan/wdm/private-vulnerability-reporting   # makes the SECURITY.md link work
  gh api -X PUT repos/md-anamul-hasan/wdm/vulnerability-alerts              # Dependabot alerts
  gh api -X PUT repos/md-anamul-hasan/wdm/automated-security-fixes          # Dependabot security updates
  ```
  Or in the web UI: Settings → Code security → enable *Private vulnerability reporting*, *Dependabot alerts*, *Dependabot security updates*.
- Branch protection on `master`: Settings → Branches → require the `ci / test` check before merging (it appears after the first PR run).
- SignPath approval pending → set `SIGNPATH_ORGANIZATION_ID` variable and `SIGNPATH_API_TOKEN` secret (never commit them).
- Edge Add-ons listing (optional): after publishing, register its new ID with `--register --extension-id <id>` or add it to `NativeHostManifest`.
