<#
.SYNOPSIS  Authenticode-signs files with signtool (SHA-256, RFC 3161 timestamp) and verifies the result. Local alternative to the SignPath job in release.yml.
           Certificate: WDM_SIGN_THUMBPRINT (cert in CurrentUser\My) or WDM_SIGN_PFX + WDM_SIGN_PFX_PASSWORD. Never commit a PFX.
.EXAMPLE   $env:WDM_SIGN_THUMBPRINT='...'; .\scripts\sign.ps1 dist\stage\*.exe dist\installer\WDM-Setup-2.5.5.exe
#>
param(
    [Parameter(Mandatory, Position = 0, ValueFromRemainingArguments)][string[]]$Path,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool) { throw 'signtool.exe not found (install the Windows SDK).' }
$cert = if ($env:WDM_SIGN_THUMBPRINT) { @('/sha1', $env:WDM_SIGN_THUMBPRINT) }
        elseif ($env:WDM_SIGN_PFX) { @('/f', $env:WDM_SIGN_PFX, '/p', $env:WDM_SIGN_PFX_PASSWORD) }
        else { throw 'Set WDM_SIGN_THUMBPRINT, or WDM_SIGN_PFX and WDM_SIGN_PFX_PASSWORD.' }
$files = Get-ChildItem $Path -File
if (-not $files) { throw "Nothing to sign: $Path" }
foreach ($f in $files) {
    & $signtool sign /fd SHA256 /td SHA256 /tr $TimestampUrl @cert $f.FullName
    if ($LASTEXITCODE) { throw "signtool failed for $($f.Name)" }
    $sig = Get-AuthenticodeSignature $f.FullName
    if (-not $sig.SignerCertificate) { throw "$($f.Name) has no signature after signing" }
    Write-Host "$($f.Name): $($sig.Status) ($($sig.SignerCertificate.Subject))"
}
