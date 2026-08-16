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

# Runtime deps — do NOT ship steam_api64.dll (Valve redistributable / auto-mod risk).
# The game already loads Steam; Facepunch attaches to the existing client.
$runtimeDlls = @(
    "Facepunch.Steamworks.Win64.dll",
    "System.Text.Json.dll",
    "System.Text.Encodings.Web.dll",
    "System.Memory.dll",
    "System.Buffers.dll",
    "System.Runtime.CompilerServices.Unsafe.dll",
    "Microsoft.Bcl.AsyncInterfaces.dll",
    "System.Threading.Tasks.Extensions.dll",
    "System.Numerics.Vectors.dll",
    "System.ValueTuple.dll"
)
foreach ($dll in $runtimeDlls) {
    $src = Join-Path $buildDir $dll
    if (Test-Path $src) {
        Copy-Item $src "$out/$pluginDir/"
    } else {
        Write-Warning "Missing runtime dep: $dll"
    }
}

# Zip root must contain manifest/README/icon (not a nested folder).
if (Test-Path $zip) { Remove-Item $zip -Force }
Push-Location $out
Compress-Archive -Path @("manifest.json", "README.md", "icon.png", "BepInEx") -DestinationPath (Join-Path (Get-Location) "..\Fowks-SineusArenaVersus-$version.zip") -Force
Pop-Location
# Normalize path (Compress wrote to dist/)
$zipPath = (Resolve-Path "dist/Fowks-SineusArenaVersus-$version.zip").Path
Write-Host "Packed: $zipPath"
Get-ChildItem $zipPath | Format-List Name, Length, LastWriteTime
