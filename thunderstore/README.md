# Sineus Arena Versus

BepInEx mod for **Sineus Arena Survivors**: parallel solo arenas, VP economy, monster sends, last stronghold standing.

## Install

1. Install [BepInEx](https://thunderstore.io/c/sineus-arena-survivors/p/BepInEx/BepInExPack/) via Thunderstore Mod Manager (TMM) for Sineus Arena Survivors.
2. Install **SineusArenaVersus** from the same community.
3. Launch the game through your TMM profile.

## How to play

- Press **F8** (configurable) to open the Versus menu.
- **Host** a match or **Join** via Steam Friends invite (2–4 players).
- Each player runs their own solo Sineus run; versus state syncs over Steam P2P.
- Earn **VP** from kills and passive income, spend VP in the shop to send monster packs at one rival.
- **Last stronghold standing wins.**

## VP rules

| Source | Default |
|--------|---------|
| Trash kill | 1 VP |
| Elite kill | 3 VP |
| Boss kill | 15 VP |
| Passive tick | every 10 s, 2 VP base |
| Send scaling | +1 VP/tick per successful send you made |

Host flushes send queues on a shared wave clock (default 20 s). If your target is already eliminated, VP is refunded.

## Config

Edit `BepInEx/config/Fowks.SineusArenaVersus.cfg` after first run.

| Key | Default | Notes |
|-----|---------|-------|
| `WaveIntervalSeconds` | 20 | Host send-wave interval |
| `VpTrash` / `VpElite` / `VpBoss` | 1 / 3 / 15 | Kill rewards |
| `PassiveIntervalSeconds` | 10 | Passive income tick |
| `PassiveBase` | 2 | VP per passive tick |
| `PassivePerSuccessfulSend` | 1 | Extra VP/tick per send |
| `MaxPlayers` | 4 | 2–4 |
| `OpenVersusMenuKey` | F8 | Menu / HUD toggle |
| `InjectRadius` | 15 | Enemy spawn radius |
| `CatalogOverridePath` | (empty) | Optional custom catalog.json |

Debug keys under `[Debug]` for offline testing and manual inject (see config comments).

## Pause behaviour

While a Versus match is active, **solo pause is disabled** (same idea as co-op): ESC / buff / building menus can open, but the arena clock keeps running so peers stay in sync.

## Known limitations (v0.1.1)

- No SubViewport / mini arena spectate on rival cards yet (config stub only).
- Independent match seeds per client (no shared seed).
- Requires Steam Friends / P2P; no dedicated relay.
- Disconnect counts as elimination.
- Catalog `spawnId` values may need in-game capture for reliable enemy injects.

## Changelog

### 0.1.1
- Disable pause during Versus (menus without `timeScale` freeze)
- Point `website_url` at the public GitHub repo
- Stop shipping `steam_api64.dll` (use the game’s Steam runtime)

### 0.1.0
- Initial Versus V1: lobby, VP economy, sends, HUD, Thunderstore package

## Links

- Source: https://github.com/Vulcro/SineusArenaVersus
