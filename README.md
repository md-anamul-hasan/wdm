# WDM — Windows Download Manager

A multi-connection download manager for Windows built on **.NET 10 / WPF**, with Chrome/Edge/Brave/Firefox integration.
WDM is an independent implementation: its behaviour was modelled on what IDM does (observed from its documented
options, settings keys and native-messaging layout), but it shares no code, assets or branding with it.

## What it does

| Area | WDM behaviour |
|---|---|
| Segmentation | Splits a file across up to 32 connections; idle connections split the largest active segment (`DynamicSegmenter`) |
| Storage | Zero-merge: workers write straight into their offset of a pre-allocated file (`StorageWriter`) |
| Resume | `.wdm-state` checkpoints every 500 ms with ETag/Last-Modified validation; ranges are verified (a server that ignores `Range` fails the segment instead of corrupting the file) |
| Request context | Referer, cookies, user agent and basic auth travel with every request; cookies are only sent to the host they were captured for and credentials to the original host, never across a redirect to another host or over an https→http downgrade |
| Download list | Persisted in `%APPDATA%\WDM\downloads.json`; interrupted downloads come back paused and resumable |
| Queue & scheduler | Start/stop queue, start/stop times, 30 s auto-retry of failed items (5 attempts), optional shutdown when the queue finishes (60 s abort window) |
| Single instance | Later launches (browser, command line) hand their download to the running window over a per-user named pipe |
| Browser integration | MV3 extension + `Wdm.NativeHost` (stdio native messaging). The extension takes over matching downloads **only after WDM confirms**; otherwise the browser download resumes untouched |
| Other | Speed limiter (one limit shared by all downloads), unknown-length (chunked) downloads, batch download (several browser links open the batch dialog), site grabber, clipboard sniffer, YouTube-style pages via bundled `yt-dlp`, dark/light theme |

### Command line (`Wdm.Ui.exe`)

```
Wdm.Ui.exe /d <url> [/p <folder>] [/f <filename>] [/n] [/a]
```
`/n` start silently without the dialog · `/a` add to the queue without starting · a bare URL also works.

`Wdm.Cli.exe <url> [-o dir] [-c connections] [-l KB/s]` is a standalone terminal downloader.

## Build, test, install

```powershell
dotnet build Wdm.slnx
dotnet test  Wdm.slnx

# Publish everything to dist\WDM and register the native messaging host for the current user
.\scripts\publish.ps1
```

Installer: `.\scripts\build-installer.ps1` builds a self-contained `dist\installer\WDM-Setup-<version>.exe` (needs Inno Setup 6: `winget install JRSoftware.InnoSetup`). Both scripts download `yt-dlp.exe` and the LGPL build of `ffmpeg.exe` (BtbN) on first run; neither is stored in the repository. The version comes from `Directory.Build.props`.

Release for a website: `.\scripts\package-release.ps1` writes `dist\release\` with the installer, a zip of the extension and `SHA256SUMS.txt`.

Load the extension: `chrome://extensions` → Developer mode → **Load unpacked** → select `extension\`.
The manifest pins the extension ID (`kdjgcdbahccolkkagbckkchfpcchcfne`) that `Wdm.NativeHost --register` authorizes.
Use `Wdm.NativeHost --unregister` to remove the registration.

Set `WDM_DATA_DIR` to relocate settings and the download list (portable mode / testing).

## Layout

```
src/Wdm.Core        engine, request helper, persistence, IPC (no UI dependencies)
src/Wdm.Ui          WPF app (MVVM): MainWindow, MainViewModel, DownloadItemViewModel
src/Wdm.Cli         terminal downloader
src/Wdm.NativeHost  browser <-> WDM bridge + registration
extension/          MV3 browser extension (Chrome/Edge/Brave; Firefox id included)
tests/Wdm.Tests     xUnit (45 tests): segmentation, storage/resume, end-to-end and unknown-length downloads, speed limiter,
                    file-name handling, cookie/credential scoping, IPC, settings
scripts/            publish.ps1 (publish + register), build-installer.ps1 (Inno Setup installer),
                    fetch-tools.ps1 (downloads yt-dlp + ffmpeg for both)
installer/          Inno Setup script
Directory.Build.props  single source of the product version
```

## Known limitations

- YouTube-style pages are fetched with `yt-dlp`; `scripts/publish.ps1` and the installer bundle `ffmpeg.exe` beside it, which joins the separate video and audio streams (a copy on `PATH` also works; without ffmpeg the download fails with a hint).
- No tray icon, proxy settings or per-site logins UI yet (credentials are supported by the engine via `DownloadRequestOptions`).
- The browser takeover list is a fixed default file-type list in `extension/background.js` (not yet editable from the app).
- Firefox support is declared in the manifest but not verified in this repository.

## License

[MIT](LICENSE). Bundled third-party tools keep their own licenses: `yt-dlp` (Unlicense) and `ffmpeg` (LGPL build from [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds); source is available from [ffmpeg.org](https://ffmpeg.org/download.html)).

## Code signing policy

Free code signing for release builds is provided by [SignPath.io](https://about.signpath.io/), with a certificate from the [SignPath Foundation](https://signpath.org/).

- Releases are built and signed only by the GitHub Actions workflow `.github/workflows/release.yml` from tagged commits of this repository.
- Only WDM's own binaries and its installer are signed; bundled third-party tools are not.
- Maintainer and approver: Md Anamul Hasan ([@md-anamul-hasan](https://github.com/md-anamul-hasan)).

## Privacy

WDM sends no telemetry and has no update check. It connects only to the servers you download from (and, for video-site pages, to whatever `yt-dlp` contacts for that site). Settings and the download list stay in `%APPDATA%\WDM` on your machine.
