<#
.SYNOPSIS  Regenerates docs\sbom\*.cdx.json (CycloneDX 1.5): one per Rust crate (cargo-cyclonedx), the UI's production npm tree, and the bundled tools from tools.lock.json.
           Needs: cargo install cargo-cyclonedx; Node.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'docs\sbom'
New-Item -ItemType Directory -Force $out | Out-Null
Push-Location $root
try {
    cargo cyclonedx --format json --spec-version 1.5 --all
    if ($LASTEXITCODE) { throw 'cargo cyclonedx failed' }
    Get-ChildItem crates -Recurse -Filter '*.cdx.json' | Move-Item -Destination $out -Force
    Push-Location src-ui
    try { npx --yes '@cyclonedx/cyclonedx-npm' --omit dev --output-format JSON --spec-version 1.5 --output-file (Join-Path $out 'wdm-ui.cdx.json'); if ($LASTEXITCODE) { throw 'cyclonedx-npm failed' } } finally { Pop-Location }

    $lock = Get-Content scripts\tools.lock.json -Raw | ConvertFrom-Json
    $ff = $lock.ffmpeg
    $bom = [ordered]@{
        bomFormat = 'CycloneDX'; specVersion = '1.5'; version = 1
        metadata = [ordered]@{ timestamp = (Get-Date).ToUniversalTime().ToString('o'); component = [ordered]@{ type = 'application'; name = 'wdm-bundled-tools'; version = (Get-Content Cargo.toml | Select-String '^version\s*=\s*"([^"]+)"' | Select-Object -First 1).Matches[0].Groups[1].Value } }
        components = @(
            [ordered]@{ type = 'application'; name = 'yt-dlp'; version = $lock.'yt-dlp'.version; licenses = @(@{ license = @{ id = 'Unlicense' } }); externalReferences = @(@{ type = 'distribution'; url = $lock.'yt-dlp'.url }); hashes = @(@{ alg = 'SHA-256'; content = $lock.'yt-dlp'.sha256 }) },
            [ordered]@{ type = 'application'; name = 'ffmpeg'; version = $ff.version; licenses = @(@{ license = @{ id = 'LGPL-2.1-or-later' } }); externalReferences = @(@{ type = 'distribution'; url = $ff.url }); hashes = @(@{ alg = 'SHA-256'; content = $ff.exeSha256 }) }
        )
    }
    $bom | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $out 'wdm-bundled-tools.cdx.json') -Encoding utf8
} finally { Pop-Location }
