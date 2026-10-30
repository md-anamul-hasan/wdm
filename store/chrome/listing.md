# Chrome Web Store listing — WDM Download Manager Integration

Paste these into the developer dashboard. Package: `dist\store\WDM-Extension-Chrome-<version>.zip` (build with `scripts\package-extension.ps1`).

## Store listing tab

**Name** (taken from the manifest): WDM Download Manager Integration

**Summary** (manifest `description`, max 132): Sends downloads and streaming media from your browser to the free WDM download manager for Windows.

**Category**: Tools (or Productivity)

**Language**: English

**Detailed description**

```
Send downloads from Chrome to WDM, the free and open-source download manager for Windows.

WDM splits a file across up to 32 connections, resumes interrupted downloads, and keeps a queue with a scheduler and a speed limit. This extension is the bridge between your browser and the WDM desktop app.

REQUIRES THE WDM DESKTOP APP, VERSION 1.0.2 OR NEWER - 2.0 RECOMMENDED (Windows 10/11). Install it first from https://github.com/md-anamul-hasan/wdm/releases/latest - the extension does nothing on its own. Older WDM versions cannot connect to this extension.

WHAT IT DOES
• Right-click any link, video or audio and choose "Download with WDM".
• Right-click a page and choose "Download all links with WDM" to queue every link on it.
• Toolbar popup: paste a URL and send it to WDM.
• Takeover rules: choose which file types WDM takes over or leaves to the browser, exclude sites (one click for the page you are on), and set the size limit for unrecognised files.
• Optional "all sites" access (you turn it on in the popup, and off again any time): WDM then takes over large or archive-type downloads using your logged-in session, and shows a "download this media" button on pages that stream video or audio. If WDM is not running or does not confirm, the browser download continues untouched - nothing is ever lost.

PRIVACY
• No analytics, no tracking, no accounts, no remote code.
• Download addresses and the cookies needed for your logged-in session go only to the WDM app on your own computer, through Chrome's native messaging. Cookies are used for the site they belong to and nowhere else.
• Incognito downloads are never taken over.

OPEN SOURCE (MIT): https://github.com/md-anamul-hasan/wdm
Privacy policy: https://github.com/md-anamul-hasan/wdm/blob/master/docs/PRIVACY.md
```

**Icon**: `extension/icon128.png` (128x128)

**Screenshots** (1280x800, up to 5): `screenshot-1-popup.png`, `screenshot-2-takeover-rules.png`, `screenshot-3-optional-access.png`, `screenshot-4-site-grabber.png`, `screenshot-5-main-window.png`. Regenerate: `node scripts/capture-extension-screens.mjs` (popup shots, needs Playwright), then `scripts\make-store-media.ps1` (composes all store images; Windows).

**Small promo tile** (440x280): `promo-small-440x280.png`

**Marquee promo tile** (1400x560, optional): `marquee-1400x560.png`

**Homepage URL**: https://github.com/md-anamul-hasan/wdm (or your website's WDM resource page)

**Support URL**: https://github.com/md-anamul-hasan/wdm/issues

## Privacy practices tab

**Single purpose**

> Send downloads and streaming media from the browser to the WDM download manager installed on the user's computer.

**Permission justifications**

| Permission | Justification |
|---|---|
| `nativeMessaging` | The extension's only output channel. It sends the download URL to the locally installed WDM app (host `com.wdm.download`). Without it the extension cannot do its single purpose. |
| `downloads` | To detect a download the browser has just started, pause it, offer it to WDM, and cancel the browser's copy only after WDM confirms (or resume it if WDM is unreachable). If the browser suspends the extension in the middle of that hand-off, the download is resumed when the extension restarts, so it is never left paused. Only acts when the user has turned on all-sites access. |
| `contextMenus` | Adds the "Download with WDM" and "Download all links with WDM" right-click entries. |
| `cookies` | To read the cookies of the download URL's site and pass them to WDM, so downloads that need the user's logged-in session work. Used only for the URL being sent, and only sent to the WDM app on the same computer. Limited to the sites the extension has access to. |
| `webRequest` | Read-only observation of response headers to notice media streams on a page and offer the "download this media" button. No request is blocked or modified. Receives events only for sites the user granted. |
| `storage` | Remembers the user's choices in local storage: whether download takeover is on, and the takeover rules the user sets in the popup (file types, excluded sites, size limit). Also keeps, in session storage that is cleared when the browser closes, the ids of browser downloads that are mid-hand-off to WDM, so they can be resumed or finished if the browser suspends the extension. No URLs, cookies or page content are stored. |
| `activeTab` | Lets the right-click and toolbar actions work on the current page after the user's click (reading that page's links for "Download all links"), without any standing site access. |
| `scripting` | Reads the links of the current page when the user picks "Download all links with WDM", and registers the media button's script only if the user has granted all-sites access. |
| Optional host permission `<all_urls>` | NOT requested at install. The user can grant it from the popup. It is needed only for the opt-in features: taking over browser downloads (the extension must read the cookies for whichever site a download comes from, and see the download's site), and the "download this media" button on any site. It contacts none of those sites itself, and the user can turn it off again in the popup. |

**Remote code**: No. All JavaScript is in the package.

**Data usage disclosure** — tick only these, all "handled locally, not sold, not used for unrelated purposes, not used for creditworthiness":
- **Web history**: no.
- **Website content**: no (only link and media addresses, which the user chooses to send, are read).
- **Authentication information**: yes — cookies of the download's site, sent only to the WDM app on the user's computer. (If the dashboard's categories do not fit, choose the closest and explain in the description above.)
- All other categories: not collected.

Certify all three: no sale to third parties, no use unrelated to the single purpose, no use for credit or lending decisions.

**Privacy policy URL**: https://github.com/md-anamul-hasan/wdm/blob/master/docs/PRIVACY.md

## Test instructions tab (for reviewers)

The dashboard limits "Additional instructions" to **500 characters** (the username and password fields stay empty: no login is needed). Paste exactly this (494 characters):

```
Needs the free Windows app WDM 1.0.2+: github.com/md-anamul-hasan/wdm/releases/latest (WDM-Setup; keep "Enable browser integration" ticked). Then install the extension, right-click any file link > "Download with WDM": WDM opens and downloads it. Or paste a URL in the toolbar popup > "Send to WDM". Optional: popup > "Allow access" enables takeover of large downloads and the Takeover rules. Without WDM the popup says it cannot connect and browser downloads continue normally. No login needed.
```

Longer reviewer notes (not for the dashboard): the app installs the native messaging host `com.wdm.download`; without it the extension reports "WDM not found" and never touches the browser's own downloads. With "all sites" access on, a large or archive download is paused, offered to WDM, and cancelled in the browser only after WDM confirms.

## After the first upload

The store assigned the ID `flnbghmpkajibfdfhbcfhkcjkhlokfgi`. It is built into `NativeHostManifest.StoreChromeExtensionId`
(`src/Wdm.Core/Ipc/NativeHostManifest.cs`), so `Wdm.NativeHost.exe --register` authorizes it by default.
Other IDs (for example an Edge Add-ons listing) can be added with `--register --extension-id <id>`.

## Updating later

Bump `version` in `extension/manifest.json` (must be higher than the published one), run `scripts\package-extension.ps1`, upload the new zip in the Package tab, then review the Store listing and Privacy practices tabs for any text that changed.

## Release history (store versions)

| Version | Changes | Dashboard fields touched |
|---|---|---|
| 1.0.0 | First release | all |
| 1.0.1 | Hand-offs interrupted by the browser suspending the extension are settled on restart (no download stays paused); Firefox manifest declares data collection (not part of the Chrome package). Needs WDM 1.0.2+ on the PC. Permissions unchanged. | Package; description (WDM version); `downloads` and `storage` justifications; test instructions |
| 2.0.0 | Takeover rules (file types, excluded sites, size limit; "never from this site" button) with a new rules.js; version aligned with WDM 2.0.0. Permissions unchanged. | Package; description; `storage` justification; test instructions |
| 2.0.1 | Popup layout fix (long site names wrap). Same permissions and features as 2.0.0. **Upload this instead of 2.0.0 if 2.0.0 is not already live.** New screenshot set (takeover rules added, batch dropped). | Package; screenshots |
