<#
.SYNOPSIS  Downloads the pinned yt-dlp.exe and ffmpeg.exe (scripts\tools.lock.json) into a folder and verifies their SHA-256.
           A tool already in the folder is kept only if its hash matches the lock file; anything else is replaced.
           Used by publish.ps1 and build-installer.ps1. Throws (non-zero exit) on any hash mismatch.
#>
param(
    [Parameter(Mandatory)][string]$Directory,
    [string]$LockFile = (Join-Path $PSScriptRoot 'tools.lock.json')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
New-Item -ItemType Directory -Force $Directory | Out-Null
$lock = Get-Content $LockFile -Raw | ConvertFrom-Json

function Test-Hash([string]$Path, [string]$Expected) {
    (Test-Path $Path) -and ((Get-FileHash $Path -Algorithm SHA256).Hash -ieq $Expected)
}

function Get-Verified([string]$Url, [string]$Expected, [string]$Destination) {
    $partial = "$Destination.download"
    Remove-Item $partial -Force -ErrorAction SilentlyContinue
    try {
        Invoke-WebRequest $Url -OutFile $partial -MaximumRetryCount 3 -RetryIntervalSec 5
        $actual = (Get-FileHash $partial -Algorithm SHA256).Hash
        if ($actual -ine $Expected) {
            throw "SHA-256 mismatch for $Url`n  expected $Expected`n  actual   $actual"
        }
        Move-Item $partial $Destination -Force
    }
    finally {
        Remove-Item $partial -Force -ErrorAction SilentlyContinue
    }
}

# ---- yt-dlp
$ytdlp = Join-Path $Directory 'yt-dlp.exe'
if (-not (Test-Hash $ytdlp $lock.'yt-dlp'.sha256)) {
    Write-Host "Fetching yt-dlp $($lock.'yt-dlp'.version)"
    Get-Verified $lock.'yt-dlp'.url $lock.'yt-dlp'.sha256 $ytdlp
}

# ---- ffmpeg: merges the separate video and audio streams YouTube serves; the LGPL build is enough for stream copy
$ffmpeg = Join-Path $Directory 'ffmpeg.exe'
$ffmpegLicense = Join-Path $Directory 'ffmpeg-LICENSE.txt'
if (-not ((Test-Hash $ffmpeg $lock.ffmpeg.exeSha256) -and (Test-Path $ffmpegLicense))) {
    Write-Host "Fetching ffmpeg $($lock.ffmpeg.version)"
    $id = [guid]::NewGuid().ToString('N')
    $zip = Join-Path ([IO.Path]::GetTempPath()) "wdm-ffmpeg-$id.zip"
    $extract = Join-Path ([IO.Path]::GetTempPath()) "wdm-ffmpeg-$id"
    try {
        Get-Verified $lock.ffmpeg.url $lock.ffmpeg.zipSha256 $zip
        Expand-Archive $zip -DestinationPath $extract
        $exe = Join-Path $extract $lock.ffmpeg.exeEntry
        if (-not (Test-Hash $exe $lock.ffmpeg.exeSha256)) { throw "ffmpeg.exe inside the archive does not match the pinned SHA-256" }
        Copy-Item $exe $ffmpeg -Force
        Copy-Item (Join-Path $extract $lock.ffmpeg.licenseEntry) $ffmpegLicense -Force
    }
    finally {
        Remove-Item $zip, $extract -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Tools verified in $Directory"
