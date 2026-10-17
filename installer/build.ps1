# Builds RegKeeper-<version>-x64.msi: single-file publish of both exes, then WiX 5.
# Once per machine: dotnet tool install -g wix --version 5.0.2; wix extension add -g WixToolset.UI.wixext/5.0.2 WixToolset.Util.wixext/5.0.2
param([string]$Version = '1.0.0', [string]$Out = 'W:\RegMsi')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$pub = Join-Path $root 'artifacts\msi-publish'
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
foreach ($p in 'RegKeeper', 'RegKeeper.Watcher') {
    dotnet publish "$root\src\$p" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:SatelliteResourceLanguages=en -o $pub -nologo -v q
    if ($LASTEXITCODE) { throw "publish $p failed" }
}
New-Item -ItemType Directory -Force $Out | Out-Null
Push-Location $PSScriptRoot
try {
    wix build RegKeeper.wxs -arch x64 -culture ru-RU -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
        -d Version=$Version -d Pub=$pub -pdbtype none -o "$Out\RegKeeper-$Version-x64.msi"
    if ($LASTEXITCODE) { throw 'wix build failed' }
} finally { Pop-Location }
