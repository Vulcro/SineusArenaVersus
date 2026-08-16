# Sineus Arena Versus — Design Spec

**Date:** 2026-08-16  
**Status:** Approved (brainstorm)  
**Reference:** Links Surviving Bros / Zink Versus V1 (`2026-08-08-versus-mode-design.md`)  
**Delivery:** BepInEx plugin → Thunderstore community `sineus-arena-survivors`

---

## 1. Pitch

Versus = **last stronghold standing**. Each player runs a **full local solo Sineus match** (hero, waves, towers, stronghold, loot — vanilla behavior). A parallel **Steam Friends** channel syncs only versus state: VP, monster sends, rival snapshots, eliminations, winner.

Inspiration: Zink/Links Surviving Bros versus (independent arenas, VP shop, send waves) + Legion TD passive income that scales with sends.

---

## 2. Architecture (Approach 1 — monolithic BepInEx plugin)

```
Each client                              Host (lobby owner)
────────────────                         ─────────────────
Vanilla solo Sineus run                  Shared send-wave clock
Earn VP (kills + passive)                Validate costs / relay sends
Shop → queue pack to 1 target            Broadcast stronghold_down / winner
Receive packs → local spawn inject       Alive mask
Emit rival snapshot ~3 Hz
```

**Chosen networking model:** parallel solo sessions + light custom sync (not hijacking native co-op shared arena).

**Not in V1:** shared enemy pool, detouring native co-op combat, ranked, rollback netcode, dedicated relay.

**Polish (post-V1):** SubViewport / mini arena spectate on rival cards (toggleable, perf-budgeted).

---

## 3. Lobby & match flow

- Entry: mod **Versus** menu (title or in-game entry point) → **Host** or **Join via Steam Friends invite**.
- Lobby: 2–4 players, Ready flags, host **Start** when all ready.
- On Start: each client starts/continues a **local solo run**; VersusMatch attaches.
- Match seeds: independent per client in V1 (optional shared seed later for content fairness).
- Disconnect = treated as stronghold destroyed → eliminated.
- End: last living stronghold → winner overlay → return lobby / menu.

**Max players:** 2–4.

---

## 4. Elimination & win condition

| Condition | Result |
|-----------|--------|
| Stronghold destroyed | Eliminated (no more sends) |
| Hero death | Vanilla revive rules while stronghold stands |
| Last stronghold alive | Winner |
| Disconnect | Eliminated |

---

## 5. Economy & send waves

All knobs live in **BepInEx config** (and optionally data files). No magic numbers buried only in code defaults without config exposure.

| Knob | Default V1 | Notes |
|------|------------|--------|
| Send-wave interval | 20 s | Host clock; flush queues → inject on target |
| VP per kill | trash 1 / elite 3 / boss 15 | Tunable |
| Passive tick | every **10 s** | While stronghold alive |
| Passive base amount | 2 VP / tick | Tunable |
| Passive scaling | **+1 VP / tick per successful send** (Legion TD-like) | Sender’s income increases |
| Target | **Exactly one** living opponent per send | Shop picker |
| Refund | If target already out at flush | VP returned |

**Send catalog:** data-driven offerings (id, cost, enemy mix / event tag, icon). Example entries: cheap swarm, mid fast pack, expensive elites, very expensive mini-boss/event.

**Flow:** queue outgoing → show on target as **incoming preview** → on wave tick, host ACKs → target `VersusSpawner` injects via Harmony hooks into vanilla spawn pipeline.

---

## 6. HUD

Additive overlay; does not replace Sineus UI.

1. **Rival strip** (≤3 cards): Steam name, stronghold HP, alive/dead, threat tint.
2. **VP + Shop:** balance, current passive income rate, catalog buttons, target picker.
3. **Incoming preview:** icons/counts for next wave against local player.
4. **Shared wave countdown.**

**Polish:** SubViewport spectate feed on rival card.

Compat: expose settings via BepInEx `.cfg` so **ModSettingsMenu** can pick them up.

---

## 7. Net protocol

Transport: Steam Matchmaking lobby + Steam Networking P2P.

| Message | Reliability | Payload |
|---------|-------------|---------|
| `match_start` | reliable | lobby id, wave_interval, peer list |
| `wave_tick` | reliable | wave_index, host_time |
| `queue_send` | reliable | from, to, catalog_id, count |
| `rival_snap` | unreliable/ordered | peer_id, stronghold_hp01, alive, optional pos |
| `stronghold_down` | reliable | peer_id |
| `winner` | reliable | peer_id |

**Authority:** host owns wave clock, cost validation against shared catalog, winner declaration. Clients apply injects locally after validated relay.

---

## 8. Module map (modular, data-driven)

| Module | Responsibility |
|--------|----------------|
| `VersusPlugin` | BepInEx entry, config bind |
| `VersusLobby` | Steam Friends host/invite/ready/start |
| `VersusNet` | Protocol serialize/send/recv |
| `VersusEconomy` | VP, kills hook, passive timer, send scaling |
| `VersusCatalog` | Load offerings from data/config |
| `VersusSpawner` | Harmony inject packs into spawn system |
| `VersusHud` | Rival strip, shop, preview, countdown |
| `VersusMatch` | LMS lifecycle, attach/detach from run |
| `VersusSpectate` (polish) | SubViewport rival views |

---

## 9. Tech stack & dependencies

- Language: C# targeting Mono (.NET Framework 4.x as required by game)
- BepInEx 5.4.2305 + HarmonyX
- Steamworks C# wrapper (Facepunch.Steamworks or Steamworks.NET — pick at implement time based on game Steam init compatibility)
- Optional soft dep: `maanu113-SineusModdingApi` if it exposes stronghold HP / match state cleanly; otherwise Harmony reflection facades local to this mod

**Thunderstore package (final publish step):**

- Community: `sineus-arena-survivors`
- Zip layout: `manifest.json`, `README.md`, `icon.png`, plugin DLL(s) under `BepInEx/plugins/<Author>-<Name>/`
- `manifest.json` dependencies: at least `BepInEx-BepInExPack-5.4.2305`
- Upload: https://thunderstore.io/c/sineus-arena-survivors/ → Upload package

---

## 10. Success criteria (V1)

1. 2 players via Steam Friends: lobby → Ready → Start  
2. Each plays independent solo run with vanilla systems intact  
3. Kills + 10s passive grant VP; passive scales after successful sends  
4. Shop queues send to one target; wave delivers creeps; incoming preview visible  
5. Rival cards update; stronghold down eliminates; last standing wins  
6. Package installs via Thunderstore Mod Manager and loads under BepInEx  

---

## 11. Out of scope (V1)

- Native co-op session hijack / shared arena  
- Ranked / meta progression  
- Split library package (`VersusCore` separate)  
- NAT punch outside Steam  
- SubViewport spectate (explicitly **polish phase**, not V1 gate)

---

## 12. Decisions log

| Topic | Choice |
|-------|--------|
| Session model | B — parallel solo + light sync |
| Player count | 2–4 |
| Elimination | Stronghold destroyed |
| Lobby | Steam Friends invite |
| Send targeting | Single opponent |
| Economy | Kill VP + passive every 10s; income scales per successful send |
| Plugin shape | Monolithic BepInEx package |
| Spectate SubViewport | Polish phase |
