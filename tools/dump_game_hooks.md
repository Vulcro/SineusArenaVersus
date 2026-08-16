# Assembly-CSharp game hooks

Inspected `SineusArena_Data/Managed/Assembly-CSharp.dll` with Mono.Cecil from the
active BepInEx profile on 2026-08-16. Signatures below are from the installed
game assembly, not inferred names.

## Local keep and health

- `static PlayerGameDataManager PlayerGameDataManager::get_I()`
- `Unit PlayerGameDataManager::GetPlayerKeep()`
- `Unit PlayerGameDataManager::GetLocalPlayerUnit()`
- `IDamageable Unit::get_Damageable()`
- `bool IDamageable::get_IsAlive()`
- `float IDamageable::get_CurrentHealth()`
- `float IDamageable::get_MaxHealth()`
- `void PlayerGameDataManager::SetPlayerKeep(Unity.Netcode.NetworkObject)`
- `void PlayerGameDataManager::ApplySetPlayerKeep(Unit)`
- `event System.Action<Unit> PlayerGameDataManager::OnKeepSet`
- backing field: `Unit PlayerGameDataManager::playersKeep`

`GameFacades` reads the singleton, keep, and `IDamageable` properties by
reflection. Missing state produces HP `0` / alive `false`.

## Keep destruction

- `void BuildingDamageable::HandleDeathStateChanged(bool)` (private)
- `Unit BuildingDamageable::get_Owner()`
- `void Unit::OnDied(IDamageable)` (private)
- `event System.Action<PlayerTeam> Unit::keyBuildingDestroyed`
- `void GameFlowManager::HandleKeyBuildingDestroyed(PlayerTeam)` (private)

`BuildingDamageable.HandleDeathStateChanged(true)` is patched because its
`_isDeadLocal` guard makes it the replicated local death-state transition.
The patch raises `LocalKeepDestroyed` only when `Owner` is the object returned
by `GetPlayerKeep()` and the facade event has subscribers.

## Enemy death and tier

- `void Unit::OnDied(IDamageable)` (private)
- `PlayerTeam Unit::get_Team()`
- `bool Unit::get_isBuilding()`
- `bool Unit::get_isBoss()`
- `bool Unit::get_isFinalBoss()`
- `bool Unit::get_isEliteUnit()`
- fields: `bool Unit::isPlayerCharacter`, `bool Unit::keyBuilding`
- `PlayerTeam.Neutral = 0`; player teams are `Player1 = 1` through `Player4 = 4`

The postfix ignores buildings, player characters, and non-neutral units.
Boss/final-boss wins over elite; remaining neutral units classify as trash.
No event work occurs without `EnemyKilled` subscribers.

## Forced spawn

- public field: `List<BaseLairSpawner.SpawnEntry> BaseLairSpawner::units`
- public field: `Unit BaseLairSpawner.SpawnEntry::prefab`
- protected `void BaseLairSpawner::ScheduleSpawnUnit(Unit, Transform, Vector3, Action<Unit>)`
- protected `Unit BaseLairSpawner::SpawnUnit(Unit, Transform, Vector3)`
- `void NetworkObjectPool::ScheduleSpawn(GameObject, Vector3, Quaternion, Action<NetworkObject>, bool, Action<NetworkObject>)`
- `void PlayerSpawnerBootstrap::EnsureSpawnerExists(ulong)`

`ScheduleSpawnUnit` is server-gated in its IL, adds the supplied offset to the
anchor position, and uses `NetworkObjectPool.ScheduleSpawn` with a vanilla
instantiate fallback. Injection finds a live server `BaseLairSpawner`, resolves
its serialized prefab entry by catalog `spawnId`, and schedules a ring around
the local player transform.

The installed assembly does not contain serialized prefab asset names.
Default `spawnId` values are data placeholders and must be replaced in
`catalog.json` (or its configured override) with live prefab `UnitName` or
GameObject names observed in-game.

## Manual inject smoke

1. Set `Debug.DebugForceInject = true`.
2. Set `Debug.DebugEnemyKey`, `Debug.DebugEnemyCount`, and optionally
   `Versus.InjectRadius`.
3. Start a host/solo run and press configured `Debug.DebugInjectKey` (default
   `F8`).
4. A successful lookup logs `Debug enemy inject scheduled`; `failed` means no
   server spawner/prefab matched the configured `spawnId`.
