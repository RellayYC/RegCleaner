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
New-Item -ItemType Directory -Force $Out | Out-Null
Push-Location $PSScriptRoot
try {
    wix build RegKeeper.wxs -arch x64 -culture ru-RU -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
        -d Version=$Version -d Pub=$pub -d Picker=$picker -pdbtype none -o "$Out\RegKeeper-$Version-x64.msi"
    if ($LASTEXITCODE) { throw 'wix build failed' }
} finally { Pop-Location }
