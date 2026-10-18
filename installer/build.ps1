# Builds RegKeeper-<version>-x64.msi: single-file publish of both exes, then WiX 5.
# Once per machine: dotnet tool install -g wix --version 5.0.2; wix extension add -g WixToolset.UI.wixext/5.0.2 WixToolset.Util.wixext/5.0.2
# The update channel (GitHub download url, public key) comes from %USERPROFILE%\.regkeeper\update.json, see release.mjs; without it the build has none.
param([string]$Version = '1.0.0', [string]$Out = 'W:\RegMsi')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$pub = Join-Path $root 'artifacts\msi-publish'
$channel = @()
$cfg = Join-Path $env:USERPROFILE '.regkeeper\update.json'
if (Test-Path $cfg) {
    $c = Get-Content $cfg -Raw | ConvertFrom-Json
    $channel = "-p:UpdateUrl=$($c.url)", "-p:UpdatePub=$($c.pub)"
} else { Write-Warning "no $cfg, this build has no update channel" }
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
foreach ($p in 'RegKeeper', 'RegKeeper.Watcher') {
    dotnet publish "$root\src\$p" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:Version=$Version -p:SatelliteResourceLanguages=en @channel `
        -p:BaseOutputPath="$root\artifacts\msi-bin\$p\" -o $pub -nologo -v q # own bin: a running dev build in src\*\bin locks its files
    if ($LASTEXITCODE) { throw "publish $p failed" }
}
# the "Browse" helper: .NET Framework 4 csc ships with Windows, so no SDK or package is needed for it
$picker = Join-Path $root 'artifacts\FolderPicker.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:winexe -platform:anycpu -codepage:65001 -optimize `
    "-win32manifest:$PSScriptRoot\FolderPicker.manifest" "-out:$picker" "$PSScriptRoot\FolderPicker.cs"
if ($LASTEXITCODE) { throw 'FolderPicker build failed' }
$t = & $picker --target 'D:\Programs\' | Out-String
if ($t.Trim() -ne 'D:\Programs\RegKeeper\') { throw "FolderPicker target check: $t" }
# component icons of the setup dialog: a Fluent glyph in the app's ink on a white tile, 32 px (24 dialog units at 96 dpi,
# msiexec is never DPI-aware), stored as a BMP-format ICO, which is what the MSI Icon control loads
Add-Type -AssemblyName System.Drawing
$icons = Join-Path $root 'artifacts\setup-icons'
New-Item -ItemType Directory -Force $icons | Out-Null
$glyphFont = New-Object System.Drawing.Font 'Segoe Fluent Icons', 16, ([System.Drawing.GraphicsUnit]::Pixel)
if ($glyphFont.Name -ne 'Segoe Fluent Icons') { $glyphFont = New-Object System.Drawing.Font 'Segoe MDL2 Assets', 16, ([System.Drawing.GraphicsUnit]::Pixel) }
if ($glyphFont.Name -ne 'Segoe MDL2 Assets' -and $glyphFont.Name -ne 'Segoe Fluent Icons') { throw 'no Segoe Fluent Icons / MDL2 Assets font for the setup icons' }
foreach ($i in @{ start = 0xE8A9; desktop = 0xE7F4; shell = 0xE74D; watcher = 0xE890; folder = 0xE8B7 }.GetEnumerator()) {
    $bmp = New-Object System.Drawing.Bitmap 32, 32, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc(0.5, 0.5, 12, 12, 180, 90); $tile.AddArc(19.5, 0.5, 12, 12, 270, 90)
    $tile.AddArc(19.5, 19.5, 12, 12, 0, 90); $tile.AddArc(0.5, 19.5, 12, 12, 90, 90); $tile.CloseFigure()
    $g.FillPath([System.Drawing.Brushes]::White, $tile)
    $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(0xDC, 0xDC, 0xE1)), 1), $tile) # Light PanelLine
    $g.PixelOffsetMode = 'None' # whole-pixel glyph strokes; half-pixel offset smears them grey
    $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString([string][char]$i.Value, $glyphFont, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x18, 0x18, 0x1B))),
        (New-Object System.Drawing.RectangleF 0, 0, 32, 32), $sf) # Light Primary
    $g.Dispose()
    $px = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, 32, 32), 'ReadOnly', $bmp.PixelFormat)
    $raw = New-Object byte[] 4096; [Runtime.InteropServices.Marshal]::Copy($px.Scan0, $raw, 0, 4096); $bmp.UnlockBits($px); $bmp.Dispose()
    $w = New-Object System.IO.BinaryWriter ([System.IO.File]::Create("$icons\$($i.Key).ico"))
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]1)                   # ICONDIR, one image
    $w.Write([byte]32); $w.Write([byte]32); $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32](40 + 4096 + 128)); $w.Write([uint32]22)                         # its entry
    $w.Write([uint32]40); $w.Write([int32]32); $w.Write([int32]64); $w.Write([uint16]1); $w.Write([uint16]32) # BITMAPINFOHEADER, height x2 for the mask
    $w.Write([uint32]0); $w.Write([uint32](4096 + 128)); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = 31; $y -ge 0; $y--) { $w.Write($raw, $y * 128, 128) }                   # BGRA rows, bottom-up
    $w.Write((New-Object byte[] 128))                                                 # AND mask: alpha does the job
    $w.Close()
}
New-Item -ItemType Directory -Force $Out | Out-Null
Push-Location $PSScriptRoot
try {
    wix build RegKeeper.wxs -arch x64 -culture ru-RU -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
        -d Version=$Version -d Pub=$pub -d Picker=$picker -d Icons=$icons -pdbtype none -o "$Out\RegKeeper-$Version-x64.msi"
    if ($LASTEXITCODE) { throw 'wix build failed' }
} finally { Pop-Location }
