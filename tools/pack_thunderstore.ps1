# tools/pack_thunderstore.ps1 — build Thunderstore zip from Release output
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

$version = (Get-Content "thunderstore/manifest.json" | ConvertFrom-Json).version_number
$buildDir = "src/SineusArenaVersus/bin/Release/net472"
$pluginDir = "BepInEx/plugins/Fowks-SineusArenaVersus"
$out = "dist/Fowks-SineusArenaVersus"
$zip = "dist/Fowks-SineusArenaVersus-$version.zip"

if (-not (Test-Path "$buildDir/SineusArenaVersus.dll")) {
    Write-Error "Release build missing. Run: dotnet build src/SineusArenaVersus/SineusArenaVersus.csproj -c Release"
}

Remove-Item -Recurse -Force $out, $zip -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$out/$pluginDir" | Out-Null

Copy-Item thunderstore/manifest.json, thunderstore/README.md, thunderstore/icon.png $out
Copy-Item "$buildDir/SineusArenaVersus.dll" "$out/$pluginDir/"

# Runtime deps (Facepunch.Steamworks + native Steam API)
$runtimeDlls = @(
    "Facepunch.Steamworks.Win64.dll",
    "System.Text.Json.dll",
    "System.Text.Encodings.Web.dll",
    "System.Memory.dll",
    "System.Buffers.dll",
    "System.Runtime.CompilerServices.Unsafe.dll",
    "Microsoft.Bcl.AsyncInterfaces.dll"
)
foreach ($dll in $runtimeDlls) {
    $src = Join-Path $buildDir $dll
    if (Test-Path $src) {
        Copy-Item $src "$out/$pluginDir/"
    }
}
$steamApi = Join-Path $buildDir "steam_api64.dll"
if (Test-Path $steamApi) {
    Copy-Item $steamApi "$out/$pluginDir/"
} else {
    Write-Warning "steam_api64.dll not found in $buildDir"
}

Compress-Archive -Path "$out/*" -DestinationPath $zip -Force
Write-Host "Packed: $zip"
Get-ChildItem $zip | Format-List Name, Length, LastWriteTime
