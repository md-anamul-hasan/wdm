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

REQUIRES THE WDM DESKTOP APP (Windows 10/11). Install it first from https://github.com/md-anamul-hasan/wdm/releases/latest - the extension does nothing on its own.

WHAT IT DOES
• Right-click any link, video or audio and choose "Download with WDM".
• Right-click a page and choose "Download all links with WDM" to queue every link on it.
• Toolbar popup: paste a URL and send it to WDM.
• Optional "all sites" access (you turn it on in the popup, and off again any time): WDM then takes over large or archive-type downloads using your logged-in session, and shows a "download this media" button on pages that stream video or audio. If WDM is not running or does not confirm, the browser download continues untouched - nothing is ever lost.

PRIVACY
• No analytics, no tracking, no accounts, no remote code.
• Download addresses and the cookies needed for your logged-in session go only to the WDM app on your own computer, through Chrome's native messaging. Cookies are used for the site they belong to and nowhere else.
• Incognito downloads are never taken over.

OPEN SOURCE (MIT): https://github.com/md-anamul-hasan/wdm
Privacy policy: https://github.com/md-anamul-hasan/wdm/blob/master/docs/PRIVACY.md
```

**Icon**: `extension/icon128.png` (128x128)

**Screenshots** (1280x800, up to 5): `screenshot-1-popup.png`, `screenshot-2-optional-access.png`, `screenshot-3-site-grabber.png`, `screenshot-4-main-window.png`, `screenshot-5-batch.png` (regenerate all store images with `scriptsmake-store-media.ps1`)

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
| `downloads` | To detect a download the browser has just started, pause it, offer it to WDM, and cancel the browser's copy only after WDM confirms (or resume it if WDM is unreachable). Only acts when the user has turned on all-sites access. |
| `contextMenus` | Adds the "Download with WDM" and "Download all links with WDM" right-click entries. |
| `cookies` | To read the cookies of the download URL's site and pass them to WDM, so downloads that need the user's logged-in session work. Used only for the URL being sent, and only sent to the WDM app on the same computer. Limited to the sites the extension has access to. |
| `webRequest` | Read-only observation of response headers to notice media streams on a page and offer the "download this media" button. No request is blocked or modified. Receives events only for sites the user granted. |
| `storage` | Remembers one setting: whether download takeover is on. |
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

```
The extension is the browser half of the free Windows app WDM; it needs the app (and its native messaging host) installed to function.

1. Install WDM from https://github.com/md-anamul-hasan/wdm/releases/latest (the WDM-Setup installer, Windows 10/11). Keep "Enable browser integration" ticked.
2. Open Chrome, install this extension, open any page with a file link (for example https://github.com/md-anamul-hasan/wdm/releases/latest) and right-click the installer link > "Download with WDM". The WDM window opens and downloads the file.
3. Or click the toolbar icon, paste a URL and press "Send to WDM".
4. Optional features: in the popup press "Allow access" (all-sites access, a standard permission prompt). Then downloads of large or archive files started in the browser are taken over by WDM, and pages that play video show a "Download with WDM" button. "Turn off" in the popup removes the access again.
5. Without WDM installed the popup shows it cannot reach the app and browser downloads proceed normally.

No account or login is needed.
```

## After the first upload

The store assigns the extension its own ID (different from the sideloaded one). Authorize it for the native host:

1. Copy the 32-letter ID from the dashboard.
2. Add it to `PublishedChromeExtensionIds` in `src/Wdm.NativeHost/Program.cs`, rebuild and release (the installer then authorizes it by default). To test before that: `Wdm.NativeHost.exe --register --extension-id <id>`.

## Updating later

Bump `version` in `extension/manifest.json`, run `scripts\package-extension.ps1`, upload the new zip in the Package tab.
