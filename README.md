# Sineus Arena Versus

BepInEx Versus mod for [Sineus Arena Survivors](https://store.steampowered.com/app/4227400/): independent solo arenas, VP monster sends, last stronghold standing.

**Thunderstore:** [Fowks-SineusArenaVersus](https://thunderstore.io/c/sineus-arena-survivors/p/Fowks/SineusArenaVersus/)

## Build

Requires local game + Thunderstore BepInEx profile paths (see `Directory.Build.props`).

```bash
dotnet build src/SineusArenaVersus/SineusArenaVersus.csproj -c Release
dotnet test SineusArenaVersus.sln -c Release
powershell -File tools/pack_thunderstore.ps1
```

## License

MIT — see LICENSE
