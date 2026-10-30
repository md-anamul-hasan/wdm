# Release, installer and CI audit

Inputs read: `.github/workflows/release.yml`, `scripts/*.ps1`, `installer/wdm.iss`, `.signpath/*`. **None of the scripts or the installer were executed** (no PowerShell/Inno Setup/Windows here).

## Workflow (`release.yml`) — changes made
| Item | Before | After |
|---|---|---|
| Token scope | `contents: write` for the whole workflow | workflow `contents: read`; only the job gets `write` (single job, so the gain is limited — see below) |
| Third-party actions | floating tags `@v4`, `@v1` | pinned to commit SHAs resolved with `git ls-remote` on 2026-10-30 (tag kept in a comment) |
| Extension tests | not run in CI | `node --test tests/extension/background.test.mjs` before packaging |

Still open: signing and publishing share one job/token (split into `build` → `publish` jobs so the signing job never holds `contents: write`); `choco install innosetup` is unpinned; SHA pins must be refreshed periodically (Dependabot `github-actions` ecosystem recommended); the modified workflow **was run** (dispatch on the branch, run 41060931118) and all build/test/installer steps passed; the signing and publish steps were skipped (no SignPath variables; publish only on tags) and are therefore still unexercised.

## Findings
| ID | Sev | Finding |
|---|---|---|
| AUD-019 | P2 | **Fixed.** `fetch-tools.ps1` used yt-dlp `latest` and BtbN `latest` with no checksum. Now `scripts/tools.lock.json` pins yt-dlp 2026.10.26 and FFmpeg n8.1.3-25-g611cec792a (LGPL, from the immutable BtbN tag `autobuild-2026-10-29-19-17`); both SHA-256 values equal the publishers' own checksum files (checked 2026-10-30), and the script rejects mismatches. Tested under PowerShell 7 on Linux (fresh, rerun, tampered, wrong hash). Caveat: BtbN prunes old autobuild tags — when the URL 404s the build fails loudly; refresh the lock (procedure below) or mirror the zip in a WDM release. yt-dlp's GPG signature on `SHA2-256SUMS` was not verified. |
| AUD-024 | P3 | Installer finish page told users to "Load unpacked" although the Web Store listing exists → fixed (offers the store, keeps the offline path). |
| AUD-025 | P3 | Release zip `WDM-Extension-<v>.zip` contains `key` and Firefox keys (fine for sideload, not for stores — `package-extension.ps1` handles the Chrome store variant; no Firefox package script exists). |
| — | OK | Installer is per-user by default (`PrivilegesRequired=lowest`), has no `[UninstallDelete]` for `%APPDATA%\WDM` or download folders, and unregisters the native host on uninstall: user data is preserved by design (**not executed**). |
| — | OK | `SHA256SUMS.txt` generated with LF endings; `release-notes-<v>.md` is required before tagging (guard exists). |
| — | Info | Binaries are unsigned until SignPath approval (documented in release notes). |

## Version consistency (prepared 1.0.2)
`Directory.Build.props` 1.0.2 · `installer/wdm.iss` default 1.0.2 · `extension/manifest.json` 1.0.1 (Web Store currently 1.0.0) · `docs/release-notes-1.0.2.md` drafted · CHANGELOG section "Unreleased" should be renamed on release.

## Updating the pinned tools
1. yt-dlp: pick the release; copy the `yt-dlp.exe` value from its `SHA2-256SUMS`; set `version`, `url`, `sha256`.
2. FFmpeg: pick a BtbN `autobuild-*` tag; `checksums.sha256` there lists the `win64-lgpl` zip (use the non-`shared` one for the release branch you want); set `url` and `zipSha256`; download it once, unzip, `sha256sum bin/ffmpeg.exe` → `exeSha256`; update `exeEntry`/`licenseEntry` folder names.
3. `pwsh scripts/fetch-tools.ps1 -Directory <empty dir>` must finish with "Tools verified"; update `THIRD_PARTY_NOTICES.md`.
4. Scripts verified to parse with PowerShell 7 (`build-installer`, `fetch-tools`, `make-store-media`, `package-extension`, `package-release`, `publish`); `package-extension.ps1` was run and produced a valid store zip (v1.0.1, no `key`, no Firefox keys). The Inno Setup and signing steps were not run.
