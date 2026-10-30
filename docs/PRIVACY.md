# Privacy Policy — WDM and the WDM browser extension

_Last updated: 2026-10-30 (2.0.0)_

WDM (Windows Download Manager) and its browser extension, "WDM Download Manager Integration", are free, open-source software maintained by Md Anamul Hasan. This policy covers both.

## Summary

- WDM collects **no personal data** and sends **no telemetry**. There are no analytics, no accounts, no advertising and no update check.
- Nothing the extension reads leaves your computer, except to the websites you are downloading from, and only as described below.
- There is no server operated by the WDM project that receives your data.

## Site access is optional

When installed, the extension has no standing access to websites. The toolbar popup and the right-click menus work on the page you click on. A separate, optional "all sites" permission can be granted from the popup and removed again at any time. Only with it does the extension take over browser downloads, read the cookies of a download's site, and watch for media on pages. Everything below describes what happens with that data.

## What the extension does with browser data

| Data | Why it is accessed | Where it goes |
|---|---|---|
| Download URL, referring page address | To hand a download you started to the WDM desktop app | To the WDM app on your own computer, through Chrome's native messaging (stdio). Not sent over the network. |
| Cookies for the download's site | Many downloads need your logged-in session. The cookies of the download URL's site are attached to the hand-off. | To the WDM app on your computer, which sends them **only** to the host they belong to when it downloads the file. Never to any other host and never over an https to http downgrade. |
| Browser user-agent string | So the file server sees the same browser identity | Same as above |
| Media request addresses seen on a page (webRequest headers) | To show the "download this media" button on pages that stream video or audio | Kept in memory for that tab only. Not stored, not sent anywhere except to WDM when you press the button. |
| Page links | Only when you choose "Download all links with WDM" | To the WDM app on your computer |
| The "take over downloads" on/off setting and your takeover rules (file types, excluded sites, size limit) | To remember your choices | Stored locally with `chrome.storage.local`; never sent anywhere |

The extension does not read, store or transmit your browsing history, form input, passwords or page content, and it loads no remotely hosted code.

## What the desktop app stores

Settings and the download list are kept on your computer in `%APPDATA%\WDM` (or the folder in `WDM_DATA_DIR`). They are never uploaded. The app connects only to the servers you download from and, for video-site pages, to whatever the bundled `yt-dlp` contacts for that site.

## Third parties

WDM bundles `yt-dlp` and `ffmpeg`, which run locally. The sites you download from receive the requests you ask WDM to make, as they would from any download manager.

## Your control

- Turn off "Take over browser downloads" in the extension popup, or remove the extension, to stop it handling downloads.
- Delete `%APPDATA%\WDM` to remove all app data. Uninstalling WDM removes the native messaging registration.

## Children

WDM is a general-purpose utility and is not directed at children, and it collects no data from anyone.

## Changes and contact

Changes to this policy are published in this repository with a new date. Questions: open an issue at https://github.com/md-anamul-hasan/wdm/issues.

## What WDM stores on your computer (added in 1.0.2)
- `%APPDATA%\WDM\downloads.json` and, next to an unfinished download, `<name>.wdm-state`: the download list and resume data. Cookies and passwords used for a download are encrypted with Windows DPAPI for your Windows account and are deleted when the download completes. Older versions stored them unencrypted; WDM 1.0.2 re-encrypts them the first time it starts.
- Unfinished downloads are kept as `<name>.wdm-part` in the download folder until they complete, then renamed.
- Nothing is sent anywhere except the downloads you start.
