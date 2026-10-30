# Browser compatibility and Chrome Web Store review

## Published listing (reviewed 2026-10-30)
- URL: https://chromewebstore.google.com/detail/wdm-download-manager-inte/flnbghmpkajibfdfhbcfhkcjkhlokfgi
- ID `flnbghmpkajibfdfhbcfhkcjkhlokfgi`; listing page reports version **1.0.0**; description matches `extension/manifest.json`.
- Only the listing page was fetched. The packaged store zip was **not** downloaded or diffed against the repo, so "store build == repo build" is unverified.

### QA of the store release
| Check | Result |
|---|---|
| Native host authorizes the store ID | **Was broken** (AUD-009). Fixed in source (`NativeHostManifest.StoreChromeExtensionId`); users need a new WDM release, or run `Wdm.NativeHost.exe --register --extension-id flnbghmpkajibfdfhbcfhkcjkhlokfgi` |
| Store zip is built by `package-extension.ps1` (no `key`, no Firefox keys, no `background.scripts`) | By script inspection yes; not verified against the uploaded file |
| Takeover only after WDM accepted | Verified with mocked `chrome` (accepted → cancel+erase; rejected / host down → resume) |
| Service-worker suspension during handoff | Defect found and fixed (AUD-011); **fix is not in store v1.0.0** — requires bumping `manifest.json` version and re-uploading |
| Permissions | Minimal at install, `<all_urls>` optional. Reasonable; justification text in `store/chrome/listing.md` is consistent with code |
| Real Chrome end-to-end (install → popup → handoff) | **Not tested** (no Windows, no host) |

## Matrix
| Browser | Status | Evidence |
|---|---|---|
| Chrome | Native host registration + manifest present; store ID now authorized | Static + unit tests. Not run in a browser |
| Edge | Registered under `Software\Microsoft\Edge\NativeMessagingHosts`. Edge installs from Chrome Web Store keep the Chrome ID; an Edge Add-ons listing would get a new ID → needs `--extension-id` | Static |
| Brave | Registered under `BraveSoftware\Brave-Browser`; uses Chrome Web Store ID | Static |
| Firefox | Manifest has `background.scripts` + gecko id `wdm@windowsdownloadmanager.com`, min 128, native manifest uses `allowed_extensions`. API surface used (downloads pause/resume/cancel/erase, permissions, scripting, cookies, sendNativeMessage) exists in Firefox MV3. `finalUrl` is Chrome-only (code falls back to `url`). **Never run in Firefox. Not claimed to work.** | Static |

## Firefox gaps to test on Windows
1. Load via `about:debugging`; confirm `sendNativeMessage` reaches the host with `allowed_extensions`.
2. `downloads.onCreated` timing: does `pause()` win the race for small files?
3. AMO submission: confirm the `data_collection_permissions: none` declaration; `package-extension.ps1` strips `browser_specific_settings`, so a Firefox package script is still missing (roadmap).
