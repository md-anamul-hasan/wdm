# Builds the Chrome Web Store images in store/chrome from the screenshots in docs/media (Windows PowerShell or pwsh on Windows:
# it uses System.Drawing and the Segoe UI font).
# Outputs: 5 screenshots (1280x800), small promo tile (440x280), marquee (1400x560).
#
# The extension popup screenshots (docs/media/screenshots/extension-popup-*.png) are rendered from the real extension/popup.html;
# regenerate them first whenever the popup changes:   node scripts/capture-extension-screens.mjs
# (needs Playwright + Chromium). Then run this script and upload the PNGs in store/chrome to the dashboard.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$shots = Join-Path $root 'docs\media\screenshots'
$out = Join-Path $root 'store\chrome'
$icon = Join-Path $root 'extension\icon128.png'

function Get-Path($p) { [System.Drawing.ColorTranslator]::FromHtml($p) }

function New-Canvas($w, $h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (Get-Path '#0f172a'), (Get-Path '#1e3a8a'), 90
    $g.FillRectangle($brush, $rect)
    $brush.Dispose()
    [pscustomobject]@{ Bmp = $bmp; G = $g }
}

function New-RoundPath($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseAllFigures()
    $p
}

# Draws an image scaled to fit, with rounded corners and a soft shadow. Returns the drawn rectangle.
function Add-Framed($g, $img, $x, $y, $maxW, $maxH, $radius) {
    $scale = [Math]::Min($maxW / $img.Width, $maxH / $img.Height)
    $w = [int]($img.Width * $scale); $h = [int]($img.Height * $scale)
    $px = [int]($x + ($maxW - $w) / 2); $py = [int]$y
    for ($i = 14; $i -ge 1; $i--) {
        $sp = New-RoundPath ($px - $i) ($py - $i + 10) ($w + 2 * $i) ($h + 2 * $i) ($radius + $i)
        $sb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(7, 0, 0, 0))
        $g.FillPath($sb, $sp); $sb.Dispose(); $sp.Dispose()
    }
    $path = New-RoundPath $px $py $w $h $radius
    $state = $g.Save()
    $g.SetClip($path)
    $g.DrawImage($img, $px, $py, $w, $h)
    $g.Restore($state)
    $path.Dispose()
    New-Object System.Drawing.Rectangle $px, $py, $w, $h
}

function Add-Text($g, $text, $x, $y, $w, $size, $style, $color, $align) {
    $font = New-Object System.Drawing.Font 'Segoe UI Semibold', $size, $style, ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = $align
    $brush = New-Object System.Drawing.SolidBrush (Get-Path $color)
    $rect = New-Object System.Drawing.RectangleF $x, $y, $w, ($size * 4)
    $g.DrawString($text, $font, $brush, $rect, $fmt)
    $m = $g.MeasureString($text, $font, [int]$w, $fmt)
    $brush.Dispose(); $font.Dispose(); $fmt.Dispose()
    $m.Height
}

function Save-Png($canvas, $name) {
    $size = "$($canvas.Bmp.Width)x$($canvas.Bmp.Height)"
    $canvas.G.Dispose()
    $canvas.Bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Bmp.Dispose()
    Write-Host "wrote store/chrome/$name ($size)"
}

function Load-Img($path) { [System.Drawing.Image]::FromFile($path) }

# ---- screenshots 1280x800: headline + subtitle + framed capture.
# The store allows five. Popup shots are tall (Tall = true) and are framed by height instead of width.
$items = @(
    @{ File = 'extension-popup-on-dark.png'; Name = 'screenshot-1-popup.png'; Title = 'Send any link to WDM'; Sub = 'Toolbar popup, right-click menu and automatic download takeover'; Tall = $true },
    @{ File = 'extension-popup-rules-light.png'; Name = 'screenshot-2-takeover-rules.png'; Title = 'Choose what WDM takes over'; Sub = 'File types, sites and a size limit - or skip the site you are on with one click'; Tall = $true },
    @{ File = 'extension-popup-access-light.png'; Name = 'screenshot-3-optional-access.png'; Title = 'Site access is optional'; Sub = 'Turn it on only if you want WDM to take over downloads, off again any time'; Tall = $true },
    @{ File = 'grabber-dark.png'; Name = 'screenshot-4-site-grabber.png'; Title = 'Grab every link on a page'; Sub = 'Scan a page, tick the files or streams you want, download them all' },
    @{ File = '..\screenshot-dark.png'; Name = 'screenshot-5-main-window.png'; Title = 'Fast, resumable downloads'; Sub = 'Up to 32 connections per file, pause and resume, queue and scheduler' }
)

# Check every source image BEFORE touching the output folder, so a missing file cannot leave store/chrome half empty
$sources = @($items | ForEach-Object { Join-Path $shots $_.File }) + $icon + (Join-Path $root 'docs\media\screenshot-dark.png')
$missing = $sources | Where-Object { -not (Test-Path $_) }
if ($missing) { throw "Missing source image(s):`n  $($missing -join "`n  ")`nRun: node scripts/capture-extension-screens.mjs (popup shots) or restore docs/media." }

if (-not (Test-Path $out)) { New-Item -ItemType Directory $out | Out-Null }

# Remove the previous set (names changed between versions, so stale files must not linger)
Get-ChildItem $out -Filter *.png | Remove-Item -Force

foreach ($it in $items) {
    $c = New-Canvas 1280 800
    [void](Add-Text $c.G $it.Title 0 38 1280 54 'Regular' '#ffffff' 'Center')
    [void](Add-Text $c.G $it.Sub 0 106 1280 24 'Regular' '#bfd2f5' 'Center')
    $img = Load-Img (Join-Path $shots $it.File)
    if ($it.Tall) { [void](Add-Framed $c.G $img 0 168 1280 600 20) }
    else { [void](Add-Framed $c.G $img 140 168 1000 620 14) }
    $img.Dispose()
    Save-Png $c $it.Name
}

# ---- small promo tile 440x280
$c = New-Canvas 440 280
$ic = Load-Img $icon
$c.G.DrawImage($ic, 32, 34, 64, 64); $ic.Dispose()
[void](Add-Text $c.G 'WDM' 108 40 300 48 'Regular' '#ffffff' 'Near')
[void](Add-Text $c.G 'Send downloads from your browser to WDM' 32 126 380 28 'Regular' '#ffffff' 'Near')
[void](Add-Text $c.G 'Free, open-source download manager' 32 214 380 17 'Regular' '#bfd2f5' 'Near')
Save-Png $c 'promo-small-440x280.png'

# ---- marquee 1400x560
$c = New-Canvas 1400 560
$ic = Load-Img $icon
$c.G.DrawImage($ic, 90, 110, 80, 80); $ic.Dispose()
[void](Add-Text $c.G 'WDM' 190 122 400 68 'Regular' '#ffffff' 'Near')
[void](Add-Text $c.G 'Send downloads and videos to the free, open-source WDM download manager.' 90 232 640 40 'Regular' '#ffffff' 'Near')
[void](Add-Text $c.G 'Takeover rules, resume, queue, SHA-256 check.' 90 424 640 22 'Regular' '#bfd2f5' 'Near')
$img = Load-Img (Join-Path $root 'docs\media\screenshot-dark.png')
[void](Add-Framed $c.G $img 760 90 1000 640 16)
$img.Dispose()
Save-Png $c 'marquee-1400x560.png'
