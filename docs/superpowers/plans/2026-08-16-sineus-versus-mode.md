# Sineus Arena Versus Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a Thunderstore-ready BepInEx Versus mod for Sineus Arena Survivors: Steam Friends lobby, parallel solo runs, VP economy (kills + 10s passive scaled by sends), single-target monster sends on a shared wave clock, last stronghold standing.

**Architecture:** Monolithic BepInEx 5 plugin. Each client plays vanilla solo; host-authoritative Steam P2P syncs only versus messages. Game hooks target Keep / BuildingDamageable / LairSpawner / Enemy / UnitDeath discovered in `Assembly-CSharp.dll`.

**Tech Stack:** C# net472, BepInEx 5.4.2305, HarmonyX, Facepunch.Steamworks (fallback Steamworks.NET if game SteamClient conflicts), optional soft use of SineusModdingApi, xUnit for pure logic tests.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-16-sineus-versus-mode-design.md`
- Game dir default: `C:\Program Files (x86)\Steam\steamapps\common\Sineus Arena`
- Steam AppId: `4227400`
- Max peers: 2–4; send target: exactly one living opponent
- Elimination: Keep/stronghold destroyed (or disconnect); hero revive stays vanilla
- Passive: every 10s; base +2 VP; +1 VP/tick per successful send (all config-exposed)
- Wave interval default: 20s
- No hardcoded catalog/economy values without BepInEx config or `catalog.json`
- Do not break solo when Versus inactive
- SubViewport spectate = polish task only (not V1 gate)
- Thunderstore community: `sineus-arena-survivors`; publish is the final task
- No git commit unless user asks

---

## File map

| Path | Responsibility |
|------|----------------|
| `SineusArenaVersus.sln` | Solution |
| `src/SineusArenaVersus/SineusArenaVersus.csproj` | Plugin project |
| `src/SineusArenaVersus/VersusPlugin.cs` | BepInEx entry + config |
| `src/SineusArenaVersus/Config/VersusConfig.cs` | Bound ConfigEntry knobs |
| `src/SineusArenaVersus/Catalog/VersusCatalog.cs` | Load/validate `catalog.json` |
| `src/SineusArenaVersus/Catalog/catalog.json` | Embedded default offerings |
| `src/SineusArenaVersus/Economy/VersusEconomy.cs` | VP, passive, scaling |
| `src/SineusArenaVersus/Net/VersusMessages.cs` | Message opcodes + DTOs |
| `src/SineusArenaVersus/Net/VersusNet.cs` | Steam P2P send/recv |
| `src/SineusArenaVersus/Lobby/VersusLobby.cs` | Friends lobby host/invite/ready/start |
| `src/SineusArenaVersus/Match/VersusMatch.cs` | LMS lifecycle, wave flush |
| `src/SineusArenaVersus/Game/GameFacades.cs` | Reflection/Harmony facades (Keep HP, kills, spawn) |
| `src/SineusArenaVersus/Game/Patches/*.cs` | Harmony patches |
| `src/SineusArenaVersus/Hud/VersusHud.cs` | Rival strip, shop, preview, countdown |
| `src/SineusArenaVersus/Spectate/VersusSpectate.cs` | Polish SubViewport (stub in V1) |
| `tests/SineusArenaVersus.Tests/*.cs` | Pure unit tests |
| `thunderstore/manifest.json` | Package manifest |
| `thunderstore/README.md` | Store page readme |
| `thunderstore/icon.png` | Package icon |
| `tools/pack_thunderstore.ps1` | Zip builder |

---

### Task 1: Solution scaffold + BepInEx plugin stub

**Files:**
- Create: `SineusArenaVersus.sln`
- Create: `src/SineusArenaVersus/SineusArenaVersus.csproj`
- Create: `src/SineusArenaVersus/VersusPlugin.cs`
- Create: `Directory.Build.props`
- Test: build output DLL

**Interfaces:**
- Consumes: none
- Produces: `VersusPlugin` loads in BepInEx; `GUID = "Fowks.SineusArenaVersus"`

- [ ] **Step 1: Create `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <GameDir Condition="'$(GameDir)' == ''">C:\Program Files (x86)\Steam\steamapps\common\Sineus Arena</GameDir>
    <BepInExProfile Condition="'$(BepInExProfile)' == ''">$(USERPROFILE)\AppData\Roaming\Thunderstore Mod Manager\DataFolder\SineusArenaSurvivors\profiles\Default</BepInExProfile>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <AssemblyName>SineusArenaVersus</AssemblyName>
    <RootNamespace>SineusArenaVersus</RootNamespace>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="BepInEx">
      <HintPath>$(BepInExProfile)\BepInEx\core\BepInEx.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(BepInExProfile)\BepInEx\core\0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>$(GameDir)\SineusArena_Data\Managed\UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(GameDir)\SineusArena_Data\Managed\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(GameDir)\SineusArena_Data\Managed\Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="Catalog\catalog.json" />
  </ItemGroup>
  <Target Name="CopyToProfile" AfterTargets="Build">
    <MakeDir Directories="$(BepInExProfile)\BepInEx\plugins\Fowks-SineusArenaVersus" />
    <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(BepInExProfile)\BepInEx\plugins\Fowks-SineusArenaVersus" />
  </Target>
</Project>
```

- [ ] **Step 3: Create plugin stub**

```csharp
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace SineusArenaVersus;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class VersusPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "Fowks.SineusArenaVersus";
    public const string PluginName = "Sineus Arena Versus";
    public const string PluginVersion = "0.1.0";

    internal static VersusPlugin Instance { get; private set; } = null!;
    internal static ManualLogSource Log => Instance.Logger;

    private Harmony? _harmony;

    private void Awake()
    {
        Instance = this;
        VersusConfig.Bind(Config);
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/SineusArenaVersus/SineusArenaVersus.csproj -c Release`  
Expected: `Build succeeded`; DLL under profile `BepInEx\plugins\Fowks-SineusArenaVersus\`

- [ ] **Step 5: Smoke-launch**

Launch game via Thunderstore profile Default.  
Expected: BepInEx log line `Sineus Arena Versus 0.1.0 loaded`

---

### Task 2: VersusConfig + data-driven catalog

**Files:**
- Create: `src/SineusArenaVersus/Config/VersusConfig.cs`
- Create: `src/SineusArenaVersus/Catalog/SendOffering.cs`
- Create: `src/SineusArenaVersus/Catalog/VersusCatalog.cs`
- Create: `src/SineusArenaVersus/Catalog/catalog.json`
- Create: `tests/SineusArenaVersus.Tests/CatalogTests.cs`

**Interfaces:**
- Consumes: BepInEx `ConfigFile` from Task 1
- Produces:
  - `VersusConfig` static entries
  - `VersusCatalog.TryGet(string id, out SendOffering offering)`
  - `IReadOnlyList<SendOffering> VersusCatalog.All`

- [ ] **Step 1: Write failing catalog test**

```csharp
using SineusArenaVersus.Catalog;
using Xunit;

public class CatalogTests
{
    [Fact]
    public void Default_catalog_has_four_offerings_with_positive_costs()
    {
        var cat = VersusCatalog.LoadFromEmbeddedDefault();
        Assert.True(cat.All.Count >= 4);
        Assert.All(cat.All, o => Assert.True(o.Cost > 0 && o.Count > 0 && !string.IsNullOrEmpty(o.Id)));
        Assert.True(cat.TryGet("swarm", out var swarm));
        Assert.Equal(8, swarm.Count);
    }
}
```

- [ ] **Step 2: Run test — expect FAIL**

Run: `dotnet test tests/SineusArenaVersus.Tests --filter CatalogTests`  
Expected: FAIL (types missing)

- [ ] **Step 3: Implement models + catalog.json**

`catalog.json`:

```json
{
  "offerings": [
    { "id": "swarm", "displayName": "Swarm", "cost": 10, "enemyKey": "trash", "count": 8 },
    { "id": "fast_pack", "displayName": "Fast Pack", "cost": 18, "enemyKey": "fast", "count": 5 },
    { "id": "elites", "displayName": "Elites", "cost": 35, "enemyKey": "elite", "count": 2 },
    { "id": "mini_boss", "displayName": "Mini-Boss", "cost": 60, "enemyKey": "mini_boss", "count": 1 }
  ]
}
```

```csharp
namespace SineusArenaVersus.Catalog;

public sealed class SendOffering
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Cost { get; set; }
    public string EnemyKey { get; set; } = "";
    public int Count { get; set; }
}
```

- [ ] **Step 4: Implement `VersusCatalog` + `VersusConfig`**

```csharp
// VersusConfig.cs — bind all knobs from spec defaults
public static class VersusConfig
{
    public static ConfigEntry<float> WaveIntervalSeconds = null!;
    public static ConfigEntry<int> VpTrash = null!;
    public static ConfigEntry<int> VpElite = null!;
    public static ConfigEntry<int> VpBoss = null!;
    public static ConfigEntry<float> PassiveIntervalSeconds = null!;
    public static ConfigEntry<int> PassiveBase = null!;
    public static ConfigEntry<int> PassivePerSuccessfulSend = null!;
    public static ConfigEntry<int> MaxPlayers = null!;
    public static ConfigEntry<string> CatalogOverridePath = null!;

    public static void Bind(ConfigFile cfg)
    {
        WaveIntervalSeconds = cfg.Bind("Versus", "WaveIntervalSeconds", 20f, "Host send-wave interval");
        VpTrash = cfg.Bind("Versus", "VpTrash", 1, "VP per trash kill");
        VpElite = cfg.Bind("Versus", "VpElite", 3, "VP per elite kill");
        VpBoss = cfg.Bind("Versus", "VpBoss", 15, "VP per boss kill");
        PassiveIntervalSeconds = cfg.Bind("Versus", "PassiveIntervalSeconds", 10f, "Passive income tick");
        PassiveBase = cfg.Bind("Versus", "PassiveBase", 2, "Base VP per passive tick");
        PassivePerSuccessfulSend = cfg.Bind("Versus", "PassivePerSuccessfulSend", 1, "Extra VP/tick per successful send");
        MaxPlayers = cfg.Bind("Versus", "MaxPlayers", 4, new ConfigDescription("2-4", new AcceptableValueRange<int>(2, 4)));
        CatalogOverridePath = cfg.Bind("Versus", "CatalogOverridePath", "", "Optional absolute path to catalog.json override");
    }
}
```

`VersusCatalog` loads override path if set/non-empty, else embedded resource; validates unique ids and positive costs.

- [ ] **Step 5: Run tests — expect PASS**

Run: `dotnet test tests/SineusArenaVersus.Tests --filter CatalogTests`  
Expected: PASS

---

### Task 3: VersusEconomy (pure logic)

**Files:**
- Create: `src/SineusArenaVersus/Economy/VersusEconomy.cs`
- Create: `tests/SineusArenaVersus.Tests/EconomyTests.cs`

**Interfaces:**
- Consumes: config values (injectable for tests)
- Produces:
  - `int Vp { get; }`
  - `int SuccessfulSends { get; }`
  - `int PassiveAmountPerTick { get; }` // PassiveBase + SuccessfulSends * PassivePerSuccessfulSend
  - `void AddKillVp(KillTier tier)`
  - `bool TrySpend(int cost)`
  - `void Refund(int amount)`
  - `void OnPassiveTick()`
  - `void RegisterSuccessfulSend()`

- [ ] **Step 1: Failing tests**

```csharp
[Fact]
public void Passive_scales_with_successful_sends()
{
    var eco = new VersusEconomy(passiveBase: 2, passivePerSend: 1);
    Assert.Equal(2, eco.PassiveAmountPerTick);
    eco.RegisterSuccessfulSend();
    Assert.Equal(3, eco.PassiveAmountPerTick);
    eco.OnPassiveTick();
    Assert.Equal(3, eco.Vp);
}

[Fact]
public void TrySpend_fails_when_insufficient_and_does_not_mutate()
{
    var eco = new VersusEconomy(passiveBase: 2, passivePerSend: 1);
    eco.AddKillVp(KillTier.Trash); // +1
    Assert.False(eco.TrySpend(10));
    Assert.Equal(1, eco.Vp);
}
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement `VersusEconomy` + `KillTier` enum**

```csharp
public enum KillTier { Trash, Elite, Boss }

public sealed class VersusEconomy
{
    private readonly Func<int> _vpTrash;
    private readonly Func<int> _vpElite;
    private readonly Func<int> _vpBoss;
    private readonly int _passiveBase;
    private readonly int _passivePerSend;

    public VersusEconomy(int passiveBase, int passivePerSend,
        Func<int>? vpTrash = null, Func<int>? vpElite = null, Func<int>? vpBoss = null)
    {
        _passiveBase = passiveBase;
        _passivePerSend = passivePerSend;
        _vpTrash = vpTrash ?? (() => 1);
        _vpElite = vpElite ?? (() => 3);
        _vpBoss = vpBoss ?? (() => 15);
    }

    public int Vp { get; private set; }
    public int SuccessfulSends { get; private set; }
    public int PassiveAmountPerTick => _passiveBase + SuccessfulSends * _passivePerSend;

    public void AddKillVp(KillTier tier) => Vp += tier switch
    {
        KillTier.Elite => _vpElite(),
        KillTier.Boss => _vpBoss(),
        _ => _vpTrash()
    };

    public bool TrySpend(int cost)
    {
        if (cost < 0 || Vp < cost) return false;
        Vp -= cost;
        return true;
    }

    public void Refund(int amount) { if (amount > 0) Vp += amount; }
    public void OnPassiveTick() { if (PassiveAmountPerTick > 0) Vp += PassiveAmountPerTick; }
    public void RegisterSuccessfulSend() => SuccessfulSends++;
}
```

- [ ] **Step 4: Run tests — PASS**

---

### Task 4: Protocol DTOs + serializer

**Files:**
- Create: `src/SineusArenaVersus/Net/VersusMessages.cs`
- Create: `src/SineusArenaVersus/Net/VersusSerializer.cs`
- Create: `tests/SineusArenaVersus.Tests/SerializerTests.cs`

**Interfaces:**
- Consumes: none
- Produces: round-trip binary for all opcodes in spec §7

- [ ] **Step 1: Define opcodes + records**

```csharp
public enum VersusOpcode : byte
{
    MatchStart = 1,
    WaveTick = 2,
    QueueSend = 3,
    RivalSnap = 4,
    StrongholdDown = 5,
    Winner = 6,
    Ready = 7,
    Refund = 8
}

public readonly record struct MatchStartMsg(ulong LobbyId, float WaveInterval, ulong[] Peers);
public readonly record struct WaveTickMsg(int WaveIndex, float HostTime);
public readonly record struct QueueSendMsg(ulong From, ulong To, string CatalogId, int Count);
public readonly record struct RivalSnapMsg(ulong PeerId, float StrongholdHp01, bool Alive);
public readonly record struct PeerMsg(ulong PeerId);
```

- [ ] **Step 2: Binary writer/reader (length-prefixed strings, little-endian)**

Include unit test: serialize `QueueSendMsg` → deserialize equals original.

- [ ] **Step 3: Tests PASS**

---

### Task 5: GameFacades — Keep HP, kills, spawn inject

**Files:**
- Create: `src/SineusArenaVersus/Game/GameFacades.cs`
- Create: `src/SineusArenaVersus/Game/Patches/UnitDeathPatch.cs`
- Create: `src/SineusArenaVersus/Game/Patches/KeepDestroyedPatch.cs`
- Create: `src/SineusArenaVersus/Game/EnemyKeyResolver.cs`
- Create: `tools/dump_game_hooks.md` (fill during discovery)

**Interfaces:**
- Consumes: live `Assembly-CSharp` types
- Produces:
  - `float TryGetLocalKeepHp01()`
  - `bool IsLocalKeepAlive()`
  - `event Action<KillTier> EnemyKilled`
  - `event Action LocalKeepDestroyed`
  - `bool TryInjectPack(string enemyKey, int count)`

Game DLL hints (verify with dnSpy before patching):
- Keep: `GetPlayerKeep`, `SetPlayerKeep`, `OnKeepSet`, `playersKeep`
- Damage: `BuildingDamageable`, `keyBuildingDestroyed`, `IDamageable`
- Spawn: `LairSpawner`, `FinalWaveSpawner`, `EnsureSpawnerExists`
- Death: `UnitDeath`, `Enemy`

- [ ] **Step 1: Discovery**

Open `Assembly-CSharp.dll` in dnSpy. Document exact methods for:
1. Local player Keep reference + current/max HP  
2. Keep/key building destroyed callback  
3. Enemy death with tier classification  
4. Forced spawn near player (rim)

Write findings into `tools/dump_game_hooks.md` with full type+method signatures.

- [ ] **Step 2: Implement reflection facade first (no hard type refs if obfuscation risk)**

Prefer Harmony on stable method names found in Step 1. Map `enemyKey` → prefab/id via configurable dictionary in `catalog.json` extended field `spawnId` once discovered.

- [ ] **Step 3: Manual in-game check (solo, Versus inactive)**

- Kill trash → log from death patch  
- Keep HP01 readable each second  
Expected: no gameplay breakage; logs only when debug config on

- [ ] **Step 4: Inject smoke**

Console/debug key forces `TryInjectPack("trash", 3)`.  
Expected: 3 enemies appear using vanilla spawn path

---

### Task 6: VersusMatch local controller

**Files:**
- Create: `src/SineusArenaVersus/Match/VersusMatch.cs`
- Create: `src/SineusArenaVersus/Match/PeerState.cs`
- Modify: `VersusPlugin.cs` (Update loop when match active)

**Interfaces:**
- Consumes: `VersusEconomy`, `VersusCatalog`, `GameFacades`, net callbacks
- Produces:
  - `void StartMatch(IReadOnlyList<ulong> peers, bool isHost)`
  - `void Tick(float dt)`
  - `bool TryQueueSend(ulong target, string catalogId)`
  - Incoming queue + preview snapshot for HUD
  - Elimination / winner events

- [ ] **Step 1: Implement match state machine**

States: `Idle → LobbyBound → InMatch → Eliminated → Ended`

On `InMatch`:
- Passive timer uses `VersusConfig.PassiveIntervalSeconds`
- Host accumulates `waveTimer`; at interval broadcast `WaveTick` then flush
- Clients apply flush on `WaveTick` only (host also applies locally)

Flush rules:
- For each pending `QueueSend` targeting local peer: `TryInjectPack` + clear preview
- If target peer already dead when host processes queue: send `Refund` to sender; do not inject
- On successful inject for a send you originated: `RegisterSuccessfulSend()`

- [ ] **Step 2: Wire GameFacades events**

- `EnemyKilled` → `Economy.AddKillVp`
- `LocalKeepDestroyed` → emit `StrongholdDown`, disable shop, set eliminated

- [ ] **Step 3: Offline debug mode**

Config `DebugOfflineVersus=true`: fake 1 rival peer, local wave timer, inject to self optional.  
Expected: economy + wave + inject work without Steam

---

### Task 7: Steam lobby + VersusNet

**Files:**
- Create: `src/SineusArenaVersus/Lobby/VersusLobby.cs`
- Create: `src/SineusArenaVersus/Net/VersusNet.cs`
- Create: `src/SineusArenaVersus/Steam/SteamBootstrap.cs`
- Package: Facepunch.Steamworks (or Steamworks.NET) as private dependency

**Interfaces:**
- Consumes: serializer, match controller
- Produces:
  - `Task HostLobbyAsync()`
  - `void InviteFriend(SteamId id)`
  - `void SetReady(bool ready)`
  - `void StartMatchAsHost()` // requires all ready, 2–4 members
  - `void Broadcast(VersusOpcode op, byte[] payload)`
  - `void SendTo(ulong peer, ...)`

- [ ] **Step 1: SteamBootstrap**

Init with AppId `4227400`. If game already initialized Steam, attach without double-init (detect and use existing callback pump). Call `RunCallbacks` from plugin `Update`.

- [ ] **Step 2: Lobby flow**

- Host creates lobby max members = `VersusConfig.MaxPlayers`
- Friends invite via Steam overlay / `InviteFriend`
- Lobby data keys: `versus=1`, ready flags per steamId
- Host Start → `MatchStart` to all → each client `VersusMatch.StartMatch`

- [ ] **Step 3: P2P reliability**

Reliable channel for MatchStart/WaveTick/QueueSend/StrongholdDown/Winner/Refund  
Unreliable for RivalSnap @ ~3 Hz from each alive peer

- [ ] **Step 4: Two-client LAN/Steam Friends playtest**

Checklist from spec §10 items 1–5.

---

### Task 8: VersusHud

**Files:**
- Create: `src/SineusArenaVersus/Hud/VersusHud.cs`
- Create: `src/SineusArenaVersus/Hud/RivalCardView.cs`

**Interfaces:**
- Consumes: `VersusMatch` public read models
- Produces: OnGUI or runtime uGUI overlay (prefer OnGUI V1 for speed; swap later if needed)

- [ ] **Step 1: Rival strip**

Up to 3 cards: name, HP bar from last `RivalSnap`, dead greyscale

- [ ] **Step 2: Shop panel**

VP label, passive rate label, catalog buttons (disabled if eliminated or can't afford), target peer dropdown (living only)

- [ ] **Step 3: Incoming preview + wave countdown**

Show queued packs against local player; countdown from match wave timer

- [ ] **Step 4: Winner/eliminated overlay**

Blocks shop; shows return-to-lobby button (leaves match state)

---

### Task 9: Boot path integration + safety

**Files:**
- Modify: `VersusPlugin.cs`
- Create: `src/SineusArenaVersus/Ui/VersusMenu.cs`

- [ ] **Step 1: Menu entry**

Keybind config `OpenVersusMenuKey` default `F8` opens Host/Invite/Ready/Start panel when not in match; in match toggles HUD collapse.

- [ ] **Step 2: Solo safety**

If no active VersusMatch: zero patches side effects beyond optional debug logs; no VP, no HUD.

- [ ] **Step 3: Disconnect handling**

Steam lobby member leave → treat as `StrongholdDown` for that peer; host recompute winner.

- [ ] **Step 4: Full acceptance playtest**

Spec success criteria 1–5 on 2 players.

---

### Task 10: Thunderstore package

**Files:**
- Create: `thunderstore/manifest.json`
- Create: `thunderstore/README.md`
- Create: `thunderstore/icon.png` (256×256)
- Create: `tools/pack_thunderstore.ps1`

**Interfaces:**
- Consumes: Release DLL
- Produces: zip uploadable to https://thunderstore.io/c/sineus-arena-survivors/

- [ ] **Step 1: manifest.json**

```json
{
  "name": "SineusArenaVersus",
  "version_number": "0.1.0",
  "website_url": "https://github.com/DocFowks/SineusArenaVersus",
  "description": "Versus mode: independent solo arenas, VP monster sends, last stronghold standing.",
  "dependencies": [
    "BepInEx-BepInExPack-5.4.2305"
  ]
}
```

- [ ] **Step 2: README**

Document: install via TMM, F8 menu, Friends lobby, VP rules, config knobs, known limitations (no SubViewport yet).

- [ ] **Step 3: pack script**

```powershell
# tools/pack_thunderstore.ps1
$out = "dist/Fowks-SineusArenaVersus"
Remove-Item -Recurse -Force $out, "dist/*.zip" -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$out/BepInEx/plugins/Fowks-SineusArenaVersus" | Out-Null
Copy-Item thunderstore/manifest.json, thunderstore/README.md, thunderstore/icon.png $out
Copy-Item src/SineusArenaVersus/bin/Release/net472/SineusArenaVersus.dll "$out/BepInEx/plugins/Fowks-SineusArenaVersus/"
Compress-Archive -Path "$out/*" -DestinationPath "dist/Fowks-SineusArenaVersus-0.1.0.zip"
```

- [ ] **Step 4: Install zip into clean TMM profile and verify load**

- [ ] **Step 5: Upload**

Upload `dist/Fowks-SineusArenaVersus-0.1.0.zip` on Thunderstore community `sineus-arena-survivors` (manual browser step; team/author account required).

---

### Task 11: Polish — SubViewport spectate (post-V1)

**Files:**
- Create: `src/SineusArenaVersus/Spectate/VersusSpectate.cs`

**Interfaces:**
- Consumes: rival peer pose snapshots (extend `RivalSnap` with optional transform)
- Produces: toggleable mini-view on rival card

- [ ] **Step 1:** Config `EnableSpectateViews` default false  
- [ ] **Step 2:** Implement lightweight camera RT only for one focused rival at a time (perf)  
- [ ] **Step 3:** Playtest 4-player FPS impact; ship as 0.2.0 if stable  

---

## Parallelism

- Tasks 2–4 parallel after Task 1
- Task 5 can start in parallel with 2–4 (needs game install)
- Tasks 6–8 after 3–5
- Task 7 after 4 + Steam bootstrap
- Task 9 after 6–8
- Task 10 after 9 acceptance
- Task 11 after 10

---

## Self-review checklist (author)

1. Spec coverage: lobby Friends, 2–4, Keep elimination, VP kills+10s passive+send scaling, single target, wave sends, HUD, protocol, Thunderstore, spectate polish — each mapped to a task  
2. No TBD placeholders in steps  
3. Types consistent: `VersusEconomy`, `SendOffering`, opcodes match across tasks  
