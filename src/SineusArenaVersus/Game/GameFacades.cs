using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SineusArenaVersus.Catalog;
using SineusArenaVersus.Economy;
using UnityEngine;

namespace SineusArenaVersus.Game;

public static class GameFacades
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static EnemyKeyResolver? _enemyKeyResolver;
    private static ISoloRunLauncher? _soloRunLauncher;

    public static event Action<KillTier>? EnemyKilled;
    public static event Action? LocalKeepDestroyed;
    public static bool IsActive { get; internal set; }

    public static float TryGetLocalKeepHp01()
    {
        var keep = TryGetLocalKeep();
        var damageable = keep is null ? null : ReadMember(keep, "Damageable");
        if (damageable is null)
            return 0f;

        return NormalizeHealth(
            ReadSingle(damageable, "CurrentHealth"),
            ReadSingle(damageable, "MaxHealth"));
    }

    public static bool IsLocalKeepAlive()
    {
        var keep = TryGetLocalKeep();
        var damageable = keep is null ? null : ReadMember(keep, "Damageable");
        return damageable is not null && ReadBoolean(damageable, "IsAlive");
    }

    public static bool IsSoloRunActive()
    {
        var flowType = AccessTools.TypeByName("GameFlowManager");
        var flow = flowType is null
            ? null
            : AccessTools.Property(flowType, "I")?.GetValue(null, null);
        return flow is not null &&
               ReadBoolean(flow, "GameplayStarted") &&
               ReadBoolean(flow, "IsSinglePlayer");
    }

    /// <summary>
    /// Local hero position in screen pixels (origin bottom-left, same as <see cref="Input.mousePosition"/>).
    /// Falls back to screen center when the player/camera is unavailable.
    /// </summary>
    public static Vector2 GetLocalPlayerScreenPointOrCenter()
    {
        try
        {
            var transform = TryGetLocalPlayerTransform();
            var camera = Camera.main;
            if (transform is not null && camera is not null)
            {
                var world = transform.position + Vector3.up * 1.1f;
                var screen = camera.WorldToScreenPoint(world);
                if (screen.z > 0f)
                    return new Vector2(screen.x, screen.y);
            }

            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }
        catch
        {
            return new Vector2(960f, 540f);
        }
    }

    public static bool TryStartSoloRun()
    {
        _soloRunLauncher ??= new ReflectionSoloRunLauncher();
        return _soloRunLauncher.TryStartSoloRun();
    }

    public static bool TryInjectPack(string enemyKey, int count) =>
        TryInjectPack(enemyKey, count, marker: null);

    public static bool TryInjectPack(string enemyKey, int count, InjectMarkerInfo? marker)
    {
        return TryInjectPack(
            enemyKey,
            count,
            GetEnemyKeyResolver,
            (spawnId, packCount) => TrySchedulePack(spawnId, packCount, marker),
            exception => Debug.LogError($"[SineusArenaVersus] Enemy inject failed: {exception}"),
            message => Debug.LogWarning($"[SineusArenaVersus] {message}"),
            message => Debug.Log($"[SineusArenaVersus] {message}"));
    }

    internal static bool TryInjectPack(
        string enemyKey,
        int count,
        Func<EnemyKeyResolver> resolverFactory,
        Func<string, int, bool> scheduler,
        Action<Exception>? onError = null,
        Action<string>? onWarn = null,
        Action<string>? onInfo = null)
    {
        if (count <= 0)
            return false;

        try
        {
            if (!resolverFactory().TryResolve(enemyKey, out var spawnId))
            {
                onWarn?.Invoke($"Inject skipped: unknown enemyKey '{enemyKey}' (check catalog spawnId).");
                return false;
            }

            var ok = scheduler(spawnId, count);
            if (!ok)
                onWarn?.Invoke($"Inject failed for spawnId '{spawnId}' x{count} (no matching BaseLairSpawner prefab?).");
            else
                onInfo?.Invoke($"Inject scheduled: {enemyKey} -> {spawnId} x{count}");
            return ok;
        }
        catch (Exception exception)
        {
            onError?.Invoke(exception);
            return false;
        }
    }

    internal static float NormalizeHealth(float current, float maximum)
    {
        if (maximum <= 0f)
            return 0f;

        return Math.Max(0f, Math.Min(1f, current / maximum));
    }

    internal static bool IsNewDeathTransition(bool wasDead, bool isDead) =>
        !wasDead && isDead;

    internal static KillTier ClassifyEnemy(object unit)
    {
        if (ReadBoolean(unit, "isBoss") || ReadBoolean(unit, "isFinalBoss"))
            return KillTier.Boss;

        return ReadBoolean(unit, "isEliteUnit") ? KillTier.Elite : KillTier.Trash;
    }

    internal static void HandleUnitDied(object unit)
    {
        if (!IsActive || EnemyKilled is null || !IsEnemy(unit))
            return;

        EnemyKilled.Invoke(ClassifyEnemy(unit));
    }

    internal static void HandleBuildingDeathStateChanged(object damageable, bool isDead)
    {
        if (!IsActive || !isDead || LocalKeepDestroyed is null)
            return;

        var localKeep = TryGetLocalKeep();
        var owner = ReadMember(damageable, "Owner");
        if (localKeep is not null && owner is not null && IsSameObject(localKeep, owner))
            LocalKeepDestroyed.Invoke();
    }

    internal static void ConfigureEnemyKeyResolver(EnemyKeyResolver resolver)
    {
        _enemyKeyResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    private static EnemyKeyResolver GetEnemyKeyResolver()
    {
        if (_enemyKeyResolver is not null)
            return _enemyKeyResolver;

        _enemyKeyResolver = EnemyKeyResolver.FromOfferings(VersusCatalog.Load().All);
        return _enemyKeyResolver;
    }

    private static bool TrySchedulePack(string spawnId, int count, InjectMarkerInfo? marker)
    {
        var spawnerType = AccessTools.TypeByName("BaseLairSpawner");
        if (spawnerType is null)
            return false;

        var anchor = TryGetLocalPlayerTransform();
        if (anchor is null)
            return false;

        var scheduleMethod = spawnerType.GetMethod("ScheduleSpawnUnit", InstanceMembers);
        if (scheduleMethod is null)
            return false;

        var spawnCallback = marker is { } markerInfo
            ? VersusSpawnHook.CreateCallback(
                markerInfo.Label,
                markerInfo.R,
                markerInfo.G,
                markerInfo.B,
                markerInfo.A)
            : null;

        var aliases = SplitSpawnAliases(spawnId);
        List<string>? available = null;

        foreach (var candidate in UnityEngine.Object.FindObjectsByType(spawnerType))
        {
            if (!ReadBoolean(candidate, "IsServer"))
                continue;

            var prefab = FindPrefab(candidate, aliases, ref available);
            if (prefab is null)
                continue;

            var radius = VersusConfig.InjectRadius?.Value ?? 15f;
            for (var index = 0; index < count; index++)
            {
                var angle = Mathf.PI * 2f * index / count;
                var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                scheduleMethod.Invoke(candidate, new object?[] { prefab, anchor, offset, spawnCallback });
            }

            return true;
        }

        if (available is { Count: > 0 })
            Debug.LogWarning(
                $"[SineusArenaVersus] No prefab matched spawnId '{spawnId}'. Spawner units: {string.Join(", ", available)}");
        return false;
    }

    private static string[] SplitSpawnAliases(string spawnId)
    {
        if (string.IsNullOrWhiteSpace(spawnId))
            return Array.Empty<string>();

        return spawnId.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static object? FindPrefab(object spawner, IReadOnlyList<string> aliases, ref List<string>? available)
    {
        if (ReadMember(spawner, "units") is not IEnumerable entries)
            return null;

        available ??= new List<string>();

        foreach (var entry in entries)
        {
            if (entry is null)
                continue;

            var prefab = ReadMember(entry, "prefab");
            if (prefab is null)
                continue;

            CollectIdentifiers(prefab, available);
            if (MatchesAnyAlias(prefab, aliases))
                return prefab;
        }

        return null;
    }

    private static void CollectIdentifiers(object prefab, List<string> sink)
    {
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            var normalized = NormalizeIdentifier(value!);
            if (normalized.Length == 0)
                return;
            foreach (var existing in sink)
            {
                if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            sink.Add(normalized);
        }

        Add(ReadMember(prefab, "UnitName") as string);
        if (prefab is Component component)
        {
            Add(component.name);
            Add(component.gameObject.name);
        }
    }

    private static bool MatchesAnyAlias(object prefab, IReadOnlyList<string> aliases)
    {
        foreach (var alias in aliases)
        {
            var needle = NormalizeIdentifier(alias.Trim());
            if (needle.Length == 0)
                continue;
            if (MatchesSpawnId(prefab, needle))
                return true;
        }

        return false;
    }

    private static bool MatchesSpawnId(object prefab, string spawnId)
    {
        if (IdentifierEquals(ReadMember(prefab, "UnitName") as string, spawnId))
            return true;

        if (prefab is Component component)
            return IdentifierEquals(component.name, spawnId) ||
                   IdentifierEquals(component.gameObject.name, spawnId);

        return false;
    }

    private static bool IdentifierEquals(string? candidate, string expected)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        var left = NormalizeIdentifier(candidate!);
        var right = NormalizeIdentifier(expected);
        if (left.Length == 0 || right.Length == 0)
            return false;

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return true;

        // Accept Mob_Skeleton ↔ Skeleton and partial prefab names.
        if (left.EndsWith(right, StringComparison.OrdinalIgnoreCase) ||
            right.EndsWith(left, StringComparison.OrdinalIgnoreCase))
            return true;

        return left.IndexOf(right, StringComparison.OrdinalIgnoreCase) >= 0 ||
               right.IndexOf(left, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormalizeIdentifier(string value)
    {
        const string cloneSuffix = "(Clone)";
        var identifier = value.Trim();
        if (identifier.EndsWith(cloneSuffix, StringComparison.Ordinal))
            identifier = identifier.Substring(0, identifier.Length - cloneSuffix.Length).TrimEnd();

        if (identifier.StartsWith("Mob_", StringComparison.OrdinalIgnoreCase) ||
            identifier.StartsWith("Boss_", StringComparison.OrdinalIgnoreCase) ||
            identifier.StartsWith("Enemy_", StringComparison.OrdinalIgnoreCase))
        {
            // Keep full form for exact matches; comparison also uses EndsWith.
        }

        return identifier;
    }

    private static bool IsEnemy(object unit)
    {
        if (ReadBoolean(unit, "isPlayerCharacter") || ReadBoolean(unit, "isBuilding"))
            return false;

        var team = ReadMember(unit, "Team");
        return team is not null && Convert.ToInt32(team) == 0;
    }

    private static object? TryGetLocalKeep()
    {
        var manager = TryGetPlayerDataManager();
        return manager is null
            ? null
            : AccessTools.Method(manager.GetType(), "GetPlayerKeep")?.Invoke(manager, null);
    }

    private static Transform? TryGetLocalPlayerTransform()
    {
        var manager = TryGetPlayerDataManager();
        var player = manager is null
            ? null
            : AccessTools.Method(manager.GetType(), "GetLocalPlayerUnit")?.Invoke(manager, null);
        return player is Component component ? component.transform : null;
    }

    private static object? TryGetPlayerDataManager()
    {
        var managerType = AccessTools.TypeByName("PlayerGameDataManager");
        return managerType is null
            ? null
            : AccessTools.Property(managerType, "I")?.GetValue(null, null);
    }

    private static object? ReadMember(object instance, string name)
    {
        var type = instance.GetType();
        var property = type.GetProperty(name, InstanceMembers);
        if (property is not null)
            return property.GetValue(instance, null);

        return type.GetField(name, InstanceMembers)?.GetValue(instance);
    }

    private static bool ReadBoolean(object instance, string name)
    {
        var value = ReadMember(instance, name);
        return value is bool result && result;
    }

    private static float ReadSingle(object instance, string name)
    {
        var value = ReadMember(instance, name);
        return value is null ? 0f : Convert.ToSingle(value);
    }

    private static bool IsSameObject(object left, object right)
    {
        if (ReferenceEquals(left, right))
            return true;

        return left is UnityEngine.Object leftObject &&
               right is UnityEngine.Object rightObject &&
               leftObject == rightObject;
    }
}
