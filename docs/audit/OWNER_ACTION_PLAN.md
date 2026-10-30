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
   Expect 106 .NET tests + 8 Node tests green. Then launch `src\Wdm.Ui\bin\Release\net10.0-windows\Wdm.Ui.exe` and download a file with 8 connections, pause, resume.
3. **Build the installer locally once**: `.\scripts\build-installer.ps1` (needs Inno Setup 6: `winget install JRSoftware.InnoSetup`). Install it on a clean Windows VM; confirm Start-menu entry, then check `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.wdm.download` and open `%APPDATA%\WDM\NativeMessaging\com.wdm.download.json`: `allowed_origins` must list **both** `kdjgcdbahccolkkagbckkchfpcchcfne` and `flnbghmpkajibfdfhbcfhkcjkhlokfgi`.
4. **Upgrade test:** install 1.0.1 first, then 1.0.2 over it; downloads list and settings in `%APPDATA%\WDM` must survive. **Uninstall test:** `%APPDATA%\WDM` and your download folder must remain; the registry keys above must be gone.
5. **Tag and release:** `git tag v1.0.2 && git push origin v1.0.2`. The workflow needs `docs/release-notes-1.0.2.md` (already drafted). Watch the Actions run — it has never executed with my changes (SHA-pinned actions, new Node step, split permissions). If it fails, the error is most likely in those three edits.
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

## C. Decisions I need from you
| Decision | Options | My recommendation |
|---|---|---|
| Encrypt stored cookies/passwords (AUD-013) | DPAPI `ProtectedData` w/ one-time migration · don't persist cookies at all (re-captured on retry) · leave | Don't persist cookies/passwords for **completed** downloads and DPAPI-protect the rest. Say yes and I will implement with a migration test. |
| `.wdm-part` suffix while downloading (AUD-014) | yes/no | Yes — prevents "looks finished" files; resume must also find old-style partials. |
| CLI exit code when paused (AUD-030) | keep 0 · 130 | 130 |
| Pin yt-dlp/ffmpeg (AUD-019) | give me the versions you want, or "latest at date X" | I need network access to compute SHA-256 of the exact GitHub assets (the sandbox could reach git but I haven't pulled the binaries); tell me if you want me to try. |
| Firefox `data_collection_permissions: none` | confirm | Accurate only if you agree "data stays on this PC" counts as none. |
| SECURITY.md reporting | enable GitHub *private vulnerability reporting* (repo Settings → Code security) | do it, otherwise the link in SECURITY.md 404s |

## D. Repo housekeeping (5 min each)
- Enable Dependabot (`.github/dependabot.yml`: ecosystems `nuget`, `github-actions`) so SHA pins don't rot.
- Branch protection on `master`: require the test workflow (add a PR `ci.yml` running `dotnet test` + `node --test`; I did not add one because it triggers on every PR and you may want different policy).
- SignPath approval pending → set `SIGNPATH_ORGANIZATION_ID` variable and `SIGNPATH_API_TOKEN` secret (never commit them).
- Edge Add-ons listing (optional): after publishing, register its new ID with `--register --extension-id <id>` or add it to `NativeHostManifest`.
