# Draws the RegKeeper icon (light rounded tile with a dark R stroke, the logo mark) at every size natively
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

    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0xFA, 0xFA, 0xFA))), (RoundRect 0 0 $s $s (4 * $u)))

    # the R stroke, same geometry as the logo in MainWindow.xaml: M11,8 V24 M11,8 H16.5 A4.5,4.5 0 0 1 16.5,17 H11 M16,17 L22,24
    $r = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r.AddLine([float](11 * $u), [float](8 * $u), [float](11 * $u), [float](24 * $u))
    $r.StartFigure()
    $r.AddLine([float](11 * $u), [float](8 * $u), [float](16.5 * $u), [float](8 * $u))
    $r.AddArc([float](12 * $u), [float](8 * $u), [float](9 * $u), [float](9 * $u), 270, 180)
    $r.AddLine([float](16.5 * $u), [float](17 * $u), [float](11 * $u), [float](17 * $u))
    $r.StartFigure()
    $r.AddLine([float](16 * $u), [float](17 * $u), [float](22 * $u), [float](24 * $u))
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(0x09, 0x09, 0x0B)), ([float](3.5 * $u))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $g.DrawPath($pen, $r)
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
