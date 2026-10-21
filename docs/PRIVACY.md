# Privacy Policy — WDM and the WDM browser extension

_Last updated: 2026-10-21_

WDM (Windows Download Manager) and its browser extension, "WDM Integration Module", are free, open-source software maintained by Md Anamul Hasan. This policy covers both.

## Summary

- WDM collects **no personal data** and sends **no telemetry**. There are no analytics, no accounts, no advertising and no update check.
- Nothing the extension reads leaves your computer, except to the websites you are downloading from, and only as described below.
- There is no server operated by the WDM project that receives your data.

## What the extension does with browser data

| Data | Why it is accessed | Where it goes |
|---|---|---|
| Download URL, referring page address | To hand a download you started to the WDM desktop app | To the WDM app on your own computer, through Chrome's native messaging (stdio). Not sent over the network. |
| Cookies for the download's site | Many downloads need your logged-in session. The cookies of the download URL's site are attached to the hand-off. | To the WDM app on your computer, which sends them **only** to the host they belong to when it downloads the file. Never to any other host and never over an https to http downgrade. |
| Browser user-agent string | So the file server sees the same browser identity | Same as above |
| Media request addresses seen on a page (webRequest headers) | To show the "download this media" button on pages that stream video or audio | Kept in memory for that tab only. Not stored, not sent anywhere except to WDM when you press the button. |
| Page links | Only when you choose "Download all links with WDM" | To the WDM app on your computer |
| The "take over downloads" on/off setting | To remember your choice | Stored locally with `chrome.storage.local` |

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
