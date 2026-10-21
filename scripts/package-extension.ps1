<#
.SYNOPSIS  Builds the Chrome Web Store upload: dist\store\WDM-Extension-Chrome-<version>.zip
           Same files as extension\, but with a store-ready manifest: no "key" (the store assigns the ID),
           no Firefox-only "browser_specific_settings" and no "background.scripts" (Chrome uses the service worker).
#>
param([string]$Output)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'extension'
$out = if ($Output) { $Output } else { Join-Path $root 'dist\store' }

$manifest = Get-Content (Join-Path $src 'manifest.json') -Raw | ConvertFrom-Json
foreach ($prop in 'key', 'browser_specific_settings') { $manifest.PSObject.Properties.Remove($prop) }
$manifest.background.PSObject.Properties.Remove('scripts')

if ($manifest.manifest_version -ne 3) { throw 'Chrome Web Store requires manifest_version 3' }
if ($manifest.description.Length -gt 132) { throw "description is $($manifest.description.Length) characters; the store limit is 132" }
if ($manifest.name.Length -gt 75) { throw 'name is longer than the 75 characters the store allows' }

$stage = Join-Path ([IO.Path]::GetTempPath()) "wdm-ext-$([guid]::NewGuid().ToString('N'))"
try {
    New-Item -ItemType Directory $stage | Out-Null
    Get-ChildItem $src -File | Where-Object { $_.Name -ne 'manifest.json' } | Copy-Item -Destination $stage
    $manifest | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $stage 'manifest.json') -Encoding utf8

    foreach ($file in @($manifest.icons.PSObject.Properties.Value) + $manifest.background.service_worker + $manifest.action.default_popup) {
        if (-not (Test-Path (Join-Path $stage $file))) { throw "manifest references missing file: $file" }
    }

    New-Item -ItemType Directory -Force $out | Out-Null
    $zip = Join-Path $out "WDM-Extension-Chrome-$($manifest.version).zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
}
finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}

Get-Item $zip | Select-Object Name, @{n='KB'; e={[math]::Round($_.Length / 1KB, 1)}}
