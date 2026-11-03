<#
.SYNOPSIS  Builds the Modern Native Stack (Rust + Tauri) WDM and packages it as dist\installer\WDM-Setup-<version>.exe (needs Inno Setup 6).
#>
param(
    [string]$Version,
    [switch]$PublishOnly,  # stop after filling dist\stage (CI signs the binaries there before packaging)
    [switch]$SkipPublish   # package an existing dist\stage as-is
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    # Extract version from root Cargo.toml
    $cargoToml = Get-Content (Join-Path $root 'Cargo.toml') -Raw
    if ($cargoToml -match 'version\s*=\s*"([^"]+)"') {
        $Version = $matches[1]
    } else {
        $Version = "2.0.3"
    }
}
$stage = Join-Path $root 'dist\stage'

if ($PublishOnly -and $SkipPublish) { throw '-PublishOnly and -SkipPublish cannot be combined' }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc -and -not $PublishOnly) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

if (-not $SkipPublish) {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

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

    # 2. Build Rust workspace binaries in Release mode
    Write-Host "[WDM] Building native Rust binaries (Release)..." -ForegroundColor Cyan
    cargo build --workspace --release
    if ($LASTEXITCODE -ne 0) { throw "Cargo build failed" }

    $targetRelease = Join-Path $root 'target\release'

    # 3. Copy binaries to stage
    Copy-Item (Join-Path $targetRelease 'wdm-desktop.exe') (Join-Path $stage 'wdm-desktop.exe')
    Copy-Item (Join-Path $targetRelease 'wdm.exe') (Join-Path $stage 'wdm.exe')
    Copy-Item (Join-Path $targetRelease 'wdm-nativehost.exe') (Join-Path $stage 'wdm-nativehost.exe')

    # Backwards compatibility aliases for existing shortcuts, shell hooks, and registry keys
    Copy-Item (Join-Path $stage 'wdm-desktop.exe') (Join-Path $stage 'Wdm.Ui.exe')
    Copy-Item (Join-Path $stage 'wdm.exe') (Join-Path $stage 'Wdm.Cli.exe')
    Copy-Item (Join-Path $stage 'wdm-nativehost.exe') (Join-Path $stage 'Wdm.NativeHost.exe')

    # 4. yt-dlp + ffmpeg power video-platform downloads (YouTube etc.)
    if (Test-Path (Join-Path $PSScriptRoot 'fetch-tools.ps1')) {
        & (Join-Path $PSScriptRoot 'fetch-tools.ps1') -Directory $stage
    }
}

if ($PublishOnly) { return }

& $iscc "/DAppVersion=$Version" (Join-Path $root 'installer\wdm.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed' }

Get-Item (Join-Path $root "dist\installer\WDM-Setup-$Version.exe") | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
