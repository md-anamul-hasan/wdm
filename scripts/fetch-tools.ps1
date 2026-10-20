<#
.SYNOPSIS  Downloads yt-dlp.exe and ffmpeg.exe into a folder (skips tools already there). Used by publish.ps1 and build-installer.ps1.
#>
param([Parameter(Mandatory)][string]$Directory)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
New-Item -ItemType Directory -Force $Directory | Out-Null

$ytdlp = Join-Path $Directory 'yt-dlp.exe'
if (-not (Test-Path $ytdlp)) {
    Invoke-WebRequest 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' -OutFile $ytdlp
}

# ffmpeg merges the separate video and audio streams that YouTube serves; the LGPL build is enough for stream copy
$ffmpeg = Join-Path $Directory 'ffmpeg.exe'
if (-not (Test-Path $ffmpeg)) {
    $zip = Join-Path ([IO.Path]::GetTempPath()) "wdm-ffmpeg-$([guid]::NewGuid().ToString("N")).zip"
    $extract = Join-Path ([IO.Path]::GetTempPath()) "wdm-ffmpeg-$([guid]::NewGuid().ToString("N"))"
    try {
        Invoke-WebRequest 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl.zip' -OutFile $zip
        Expand-Archive $zip -DestinationPath $extract
        $exe = Get-ChildItem $extract -Recurse -Filter ffmpeg.exe | Select-Object -First 1
        if (-not $exe) { throw 'ffmpeg.exe not found in the downloaded archive' }
        Copy-Item $exe.FullName $ffmpeg
    }
    finally {
        Remove-Item $zip, $extract -Recurse -Force -ErrorAction SilentlyContinue
    }
}
