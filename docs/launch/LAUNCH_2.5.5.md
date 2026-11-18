# WDM 2.5.5 public launch kit (plan task D31)

Prepared 18-Nov-2026. Facts are from `docs/release-notes-2.5.5.md`, `README.md` and the v2.5.5 release page; re-check each line against them before posting.

**Where this goes:** software channels only (AlternativeTo, Reddit software communities, GitHub). It does **not** go on the owner's LinkedIn or the Live CAD channel: those stay inside mechanical design (owner rule, 18-Nov-2026).

## Pre-flight (done 18-Nov-2026)
- Release v2.5.5 assets: `WDM-Setup-2.5.5.exe`, `WDM-Extension-2.5.5.zip`, `WDM-SBOM-2.5.5.zip`, `SHA256SUMS.txt`.
- SBOM: the published `WDM-SBOM-2.5.5.zip` (replaced 14-Nov) matches `SHA256SUMS.txt` (`13f89533…f173`) and its `wdm-bundled-tools.cdx.json` is identical to `docs/sbom/`: ffmpeg points at the pinned mirror `releases/download/tools-ffmpeg-n8.1.3/…` (HTTP 200). The old upstream URL is gone. Nothing to fix.
- The installer is **not code-signed yet** (SignPath application pending): Windows SmartScreen will warn. Say so in every post; never tell people to "just click Run anyway" without the checksum step.

## 1. AlternativeTo listing
- Name: WDM (Windows Download Manager)
- Alternative to: Internet Download Manager, Free Download Manager, Xtreme Download Manager
- Platforms: Windows; browser extension for Chrome, Edge and Brave
- License: Free, open source (MIT)
- Tags: download-manager, download-accelerator, resume-downloads, browser-integration, open-source
- Description:
```
Free, open-source download manager for Windows. Splits each file across up to 32 connections, resumes interrupted downloads with server validation, and works with Chrome, Edge and Brave through a browser extension that only takes over a download after the app confirms it. Queue and scheduler, speed limiter, batch downloads, and video pages through the bundled yt-dlp. MIT licensed; SBOM and SHA-256 checksums published with each release.
```
- Links: https://github.com/md-anamul-hasan/wdm (source), https://github.com/md-anamul-hasan/wdm/releases/latest (download)

## 2. Reddit (one community at a time, a week apart)
Read each community's self-promotion rules first and post only where sharing your own free software is allowed; disclose that you made it. Candidates: r/software, r/opensource, r/windows (check rules). Reply to every comment the first day.

Title:
```
I made WDM, a free and open-source download manager for Windows (MIT, browser extension for Chrome/Edge/Brave)
```
Body:
```
I wanted an open-source alternative to the paid download managers, so I built WDM.

What it does:
- Splits a download across up to 32 connections and resumes interrupted downloads (it checks ETag/Last-Modified so a changed file is not silently corrupted)
- Browser extension for Chrome, Edge and Brave; the browser keeps the download unless WDM confirms it took it
- Queue with daily start/stop times, speed limiter, batch downloads, video pages via the bundled yt-dlp
- Cookies and credentials are only sent to the host they belong to, never across a redirect to another host

Honest notes:
- Windows 10/11 64-bit only
- The installer is not code-signed yet, so SmartScreen will warn. Check the file against SHA256SUMS.txt on the release page before running it.
- An SBOM (CycloneDX) is attached to the release

Download and source: https://github.com/md-anamul-hasan/wdm
Bug reports and ideas are welcome as GitHub issues.
```

## 3. GitHub
- Pin the v2.5.5 release; make sure the README's first screen shows the download link and the checksum step (it does).
- If Discussions are enabled, post the Reddit body as an "Announcements" discussion and link it from the release.

## 4. Measure (7 and 30 days after)
Release downloads per asset (`gh release view v2.5.5 --json assets`), stars, new issues, AlternativeTo likes. Log in `lifeos/imports/dual-stack-plan/Dual-Stack-KPIs.md` (GitHub release downloads row).
