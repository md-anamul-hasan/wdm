<#
.SYNOPSIS  Builds WDM into dist\WDM and registers browser integration.
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

foreach ($project in 'Wdm.Ui', 'Wdm.Cli', 'Wdm.NativeHost') {
    dotnet publish (Join-Path $root "src\$project\$project.csproj") -c $Configuration -o $out --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $project" }
}

# yt-dlp powers video-platform downloads (YouTube etc.); fetched once from the official release
$ytdlp = Join-Path $out 'yt-dlp.exe'
if (-not (Test-Path $ytdlp)) {
    Invoke-WebRequest 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' -OutFile $ytdlp
}

if (-not $NoRegister) {
    & (Join-Path $out 'Wdm.NativeHost.exe') --register
}

Write-Host "`nWDM published to $out"
Write-Host "Load the browser extension from: $(Join-Path $root 'extension') (chrome://extensions > Developer mode > Load unpacked)"
