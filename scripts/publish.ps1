<#
.SYNOPSIS  Builds Modern Native Stack WDM into dist\WDM and registers browser integration.
.EXAMPLE   .\scripts\publish.ps1            # publish + register native host
           .\scripts\publish.ps1 -NoRegister
           .\scripts\publish.ps1 -Output D:\tools\wdm
#>
param(
    [string]$Configuration = 'Release',
    [string]$Output,
    [switch]$NoRegister
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = if ($Output) { $Output } else { Join-Path $root 'dist\WDM' }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null

# 1. Build frontend bundle
Write-Host "[WDM] Building React + TypeScript frontend bundle..." -ForegroundColor Cyan
Push-Location (Join-Path $root 'src-ui')
try {
    if (-not (Test-Path 'node_modules')) { npm install }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "Frontend build failed" }
} finally {
    Pop-Location
}

# 2. Build Rust workspace binaries
Write-Host "[WDM] Building native Rust binaries ($Configuration)..." -ForegroundColor Cyan
if ($Configuration -eq 'Release') {
    cargo build --workspace --release
    $binDir = Join-Path $root 'target\release'
} else {
    cargo build --workspace
    $binDir = Join-Path $root 'target\debug'
}
if ($LASTEXITCODE -ne 0) { throw "Cargo build failed" }

# 3. Copy binaries to destination
Copy-Item (Join-Path $binDir 'wdm-desktop.exe') (Join-Path $out 'wdm-desktop.exe')
Copy-Item (Join-Path $binDir 'wdm.exe') (Join-Path $out 'wdm.exe')
Copy-Item (Join-Path $binDir 'wdm-nativehost.exe') (Join-Path $out 'wdm-nativehost.exe')


# 4. yt-dlp + ffmpeg power video-platform downloads (YouTube etc.)
& (Join-Path $PSScriptRoot 'fetch-tools.ps1') -Directory $out

if (-not $NoRegister) {
    & (Join-Path $out 'wdm-nativehost.exe') --register
}

Write-Host "`nWDM published to $out"
Write-Host "Load the browser extension from: $(Join-Path $root 'extension') (chrome://extensions > Developer mode > Load unpacked)"
