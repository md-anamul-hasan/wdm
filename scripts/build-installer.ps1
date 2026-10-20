<#
.SYNOPSIS  Builds a self-contained WDM and packages it as dist\installer\WDM-Setup-<version>.exe (needs Inno Setup 6).
#>
param([string]$Version)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) { $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version }
$stage = Join-Path $root 'dist\stage'

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

# Self-contained so the installed app needs no separate .NET runtime
foreach ($project in 'Wdm.Ui', 'Wdm.Cli', 'Wdm.NativeHost') {
    dotnet publish (Join-Path $root "src\$project\$project.csproj") -c Release -r win-x64 --self-contained true -o $stage --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $project" }
}

# yt-dlp + ffmpeg power video-platform downloads (YouTube etc.)
& (Join-Path $PSScriptRoot 'fetch-tools.ps1') -Directory $stage

& $iscc "/DAppVersion=$Version" (Join-Path $root 'installer\wdm.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed' }

Get-Item (Join-Path $root "dist\installer\WDM-Setup-$Version.exe") | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
