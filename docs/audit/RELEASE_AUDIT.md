# Release, installer and CI audit

Inputs read: `.github/workflows/release.yml`, `scripts/*.ps1`, `installer/wdm.iss`, `.signpath/*`. **None of the scripts or the installer were executed** (no PowerShell/Inno Setup/Windows here).

## Workflow (`release.yml`) — changes made
| Item | Before | After |
|---|---|---|
| Token scope | `contents: write` for the whole workflow | workflow `contents: read`; only the job gets `write` (single job, so the gain is limited — see below) |
| Third-party actions | floating tags `@v4`, `@v1` | pinned to commit SHAs resolved with `git ls-remote` on 2026-10-30 (tag kept in a comment) |
| Extension tests | not run in CI | `node --test tests/extension/background.test.mjs` before packaging |

Still open: signing and publishing share one job/token (split into `build` → `publish` jobs so the signing job never holds `contents: write`); `choco install innosetup` is unpinned; SHA pins must be refreshed periodically (Dependabot `github-actions` ecosystem recommended); the workflow was **not run**, so syntax is checked by eye only.

## Findings
| ID | Sev | Finding |
|---|---|---|
| AUD-019 | P2 | `fetch-tools.ps1` pulls yt-dlp `latest` and BtbN ffmpeg `latest` without checksum; the result is signed with WDM's own binaries' trust and shipped. A compromised release would ship silently. Pin version + SHA-256 and verify before use. |
| AUD-024 | P3 | Installer finish page told users to "Load unpacked" although the Web Store listing exists → fixed (offers the store, keeps the offline path). |
| AUD-025 | P3 | Release zip `WDM-Extension-<v>.zip` contains `key` and Firefox keys (fine for sideload, not for stores — `package-extension.ps1` handles the Chrome store variant; no Firefox package script exists). |
| — | OK | Installer is per-user by default (`PrivilegesRequired=lowest`), has no `[UninstallDelete]` for `%APPDATA%\WDM` or download folders, and unregisters the native host on uninstall: user data is preserved by design (**not executed**). |
| — | OK | `SHA256SUMS.txt` generated with LF endings; `release-notes-<v>.md` is required before tagging (guard exists). |
| — | Info | Binaries are unsigned until SignPath approval (documented in release notes). |

## Version consistency (prepared 1.0.2)
`Directory.Build.props` 1.0.2 · `installer/wdm.iss` default 1.0.2 · `extension/manifest.json` 1.0.1 (Web Store currently 1.0.0) · `docs/release-notes-1.0.2.md` drafted · CHANGELOG section "Unreleased" should be renamed on release.
