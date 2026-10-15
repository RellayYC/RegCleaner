# Draws the RegKeeper icon (blue tile, white shield holding four registry cells) at every size natively
# and packs the PNGs into src/RegKeeper/Assets/RegKeeper.ico. Run: powershell -File scripts/icon.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\RegKeeper\Assets\RegKeeper.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); $p
}

$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $u = $s / 32.0  # design grid is 32x32

    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x25, 0x63, 0xEB))), (RoundRect 0 0 $s $s (7 * $u)))

    # shield: flat top, straight sides, pointed bottom
    $sh = New-Object System.Drawing.Drawing2D.GraphicsPath
    $sh.AddLines([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (7 * $u), (6 * $u)), (New-Object System.Drawing.PointF (25 * $u), (6 * $u)),
        (New-Object System.Drawing.PointF (25 * $u), (16 * $u)), (New-Object System.Drawing.PointF (16 * $u), (27 * $u)),
        (New-Object System.Drawing.PointF (7 * $u), (16 * $u))))
    $sh.CloseFigure()
    $g.FillPath([System.Drawing.Brushes]::White, $sh)

    # four cells; at 16-20 px they would blur, so the small sizes keep a plain shield with one cell
    $cell = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x25, 0x63, 0xEB))
    if ($s -ge 24) {
        foreach ($c in @(@(10, 9), @(16.5, 9), @(10, 15.5), @(16.5, 15.5))) {
            $g.FillPath($cell, (RoundRect ($c[0] * $u) ($c[1] * $u) (5.5 * $u) (5.5 * $u) (1.2 * $u)))
        }
    } else {
        $g.FillRectangle($cell, [float](12 * $u), [float](10 * $u), [float](8 * $u), [float](7 * $u))
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}

# ICO: 6-byte header, 16-byte directory entry per image, then the PNG payloads
$f = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($out))
$f.Write([uint16]0); $f.Write([uint16]1); $f.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $f.Write([byte]$dim); $f.Write([byte]$dim); $f.Write([byte]0); $f.Write([byte]0)
    $f.Write([uint16]1); $f.Write([uint16]32); $f.Write([uint32]$pngs[$i].Length); $f.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $f.Write($p) }
$f.Close()
"icon: $out ($($sizes.Count) sizes)"
