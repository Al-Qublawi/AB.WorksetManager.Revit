<#
.SYNOPSIS
    Draws the AB Worksets Manager ribbon icon and product logo.

.DESCRIPTION
    Three stacked layers (worksets) in the AB Adv Tools colours, drawn at 128 px and downsampled so the
    16 and 32 px ribbon images stay crisp. Writes src\AB.WorksetManager.Addin\Resources\Icons\worksets_*.png,
    assets\logo.png and installer\product.ico (through the AB Adv Tools kit).

.EXAMPLE
    .\tools\make-icons.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repo   = Split-Path -Parent $PSScriptRoot
$icons  = Join-Path $repo 'src\AB.WorksetManager.Addin\Resources\Icons'
$assets = Join-Path $repo 'assets'
New-Item -ItemType Directory -Force -Path $icons, $assets, (Join-Path $repo 'installer') | Out-Null

$navy   = [System.Drawing.Color]::FromArgb(23, 34, 48)
$accent = [System.Drawing.Color]::FromArgb(31, 111, 235)
$steel  = [System.Drawing.Color]::FromArgb(96, 110, 128)
$light  = [System.Drawing.Color]::FromArgb(150, 175, 205)

function Draw-Layers([System.Drawing.Graphics] $g, [float] $s) {
    # One layer = a flat diamond (a sheet seen in isometric). Bottom to top: steel, light, accent.
    $layers = @(@{ Y = 86; C = $steel }, @{ Y = 62; C = $light }, @{ Y = 38; C = $accent })
    foreach ($layer in $layers) {
        $y = [float]$layer.Y * $s
        $pts = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF((64 * $s), ($y - 26 * $s))),
            (New-Object System.Drawing.PointF((122 * $s), $y)),
            (New-Object System.Drawing.PointF((64 * $s), ($y + 26 * $s))),
            (New-Object System.Drawing.PointF((6 * $s), $y)))
        $brush = New-Object System.Drawing.SolidBrush($layer.C)
        $pen = New-Object System.Drawing.Pen($navy, (5 * $s))
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.FillPolygon($brush, $pts)
        $g.DrawPolygon($pen, $pts)
        $brush.Dispose(); $pen.Dispose()
    }
}

function New-Image([int] $size, [float] $scale, [bool] $background) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    if ($background) {
        $b = New-Object System.Drawing.SolidBrush($navy)
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $r = 48
        $path.AddArc(0, 0, $r, $r, 180, 90); $path.AddArc($size - $r, 0, $r, $r, 270, 90)
        $path.AddArc($size - $r, $size - $r, $r, $r, 0, 90); $path.AddArc(0, $size - $r, $r, $r, 90, 90); $path.CloseFigure()
        $g.FillPath($b, $path)
        $g.TranslateTransform(32, 34)
        $navy = [System.Drawing.Color]::White
    }
    Draw-Layers $g $scale
    $g.Dispose()
    return $bmp
}

$master = New-Image 128 1.0 $false
foreach ($size in 16, 32) {
    $out = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($out)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($master, 0, 0, $size, $size)
    $g.Dispose()
    $out.Save((Join-Path $icons "worksets_$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $out.Dispose()
}
$master.Dispose()

$logo = New-Image 256 1.5 $true
$logoPath = Join-Path $assets 'logo.png'
$logo.Save($logoPath, [System.Drawing.Imaging.ImageFormat]::Png)
$logo.Dispose()

& (Join-Path $repo 'shared\ABAdvTools\tools\make-product-icon.ps1') -Png $logoPath -Ico (Join-Path $repo 'installer\product.ico')
Write-Host '  Ribbon icons, assets\logo.png and installer\product.ico written.'
