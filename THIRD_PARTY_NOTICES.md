# Third-party software bundled with the WDM installer

WDM itself is MIT-licensed (see `LICENSE`). The installer also ships these separate programs, fetched at build time by `scripts/fetch-tools.ps1` and verified against `scripts/tools.lock.json`. They are run as separate processes; WDM does not link to them.

| Program | Version | License | Source |
|---|---|---|---|
| yt-dlp | 2026.10.26 | The Unlicense (public domain) | https://github.com/yt-dlp/yt-dlp |
| FFmpeg (LGPL build, win64) | n8.1.3-25-g611cec792a | LGPL v2.1 or later (build contains no GPL components) | Binaries: https://github.com/BtbN/FFmpeg-Builds (build scripts and exact configuration); source: https://git.ffmpeg.org/ffmpeg.git (tag n8.1 and later commits) |

The FFmpeg license text is installed next to `ffmpeg.exe` as `ffmpeg-LICENSE.txt`. You may replace `ffmpeg.exe` with another build of your choice by copying it over the bundled file (the LGPL relinking right); WDM only requires a working `ffmpeg.exe` in its folder or on `PATH`.
