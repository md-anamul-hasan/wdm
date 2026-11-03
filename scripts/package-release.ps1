<#
.SYNOPSIS  Builds the installer and collects the files to upload to a website into dist\release.
           Contents: WDM-Setup-<version>.exe, WDM-Extension-<version>.zip, SHA256SUMS.txt
#>
param([string]$Version)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    $cargoToml = Get-Content (Join-Path $root 'Cargo.toml') -Raw
    if ($cargoToml -match 'version\s*=\s*"([^"]+)"') {
        $Version = $matches[1]
    } else {
        $Version = "2.5.0"
    }
}
$release = Join-Path $root 'dist\release'

& (Join-Path $PSScriptRoot 'build-installer.ps1') -Version $Version | Out-Null

if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory $release | Out-Null
Copy-Item (Join-Path $root "dist\installer\WDM-Setup-$Version.exe") $release
Compress-Archive -Path (Join-Path $root 'extension\*') -DestinationPath (Join-Path $release "WDM-Extension-$Version.zip")

# LF endings so `sha256sum -c` works on Linux and macOS too
$sums = Get-ChildItem $release -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)"
}
[IO.File]::WriteAllText((Join-Path $release 'SHA256SUMS.txt'), ($sums -join "`n") + "`n")

Get-ChildItem $release | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,2)}}
