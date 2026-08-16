using System;
using System.Collections;
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

    public static bool TryStartSoloRun()
    {
        _soloRunLauncher ??= new ReflectionSoloRunLauncher();
        return _soloRunLauncher.TryStartSoloRun();
    }

    public static bool TryInjectPack(string enemyKey, int count)
    {
        return TryInjectPack(
            enemyKey,
            count,
            GetEnemyKeyResolver,
            TrySchedulePack,
            exception => Debug.LogError($"[SineusArenaVersus] Enemy inject failed: {exception}"));
    }

    internal static bool TryInjectPack(
        string enemyKey,
        int count,
        Func<EnemyKeyResolver> resolverFactory,
        Func<string, int, bool> scheduler,
        Action<Exception>? onError = null)
    {
        if (count <= 0)
            return false;

        try
        {
            if (!resolverFactory().TryResolve(enemyKey, out var spawnId))
                return false;

            return scheduler(spawnId, count);
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

    private static bool TrySchedulePack(string spawnId, int count)
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

        foreach (var candidate in UnityEngine.Object.FindObjectsByType(spawnerType))
        {
            if (!ReadBoolean(candidate, "IsServer"))
                continue;

            var prefab = FindPrefab(candidate, spawnId);
            if (prefab is null)
                continue;

            var radius = VersusConfig.InjectRadius?.Value ?? 15f;
            for (var index = 0; index < count; index++)
            {
                var angle = Mathf.PI * 2f * index / count;
                var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                scheduleMethod.Invoke(candidate, new[] { prefab, anchor, offset, null });
            }

            return true;
        }

        return false;
    }

    private static object? FindPrefab(object spawner, string spawnId)
    {
        if (ReadMember(spawner, "units") is not IEnumerable entries)
            return null;

        foreach (var entry in entries)
        {
            if (entry is null)
                continue;

            var prefab = ReadMember(entry, "prefab");
            if (prefab is not null && MatchesSpawnId(prefab, spawnId))
                return prefab;
        }

        return null;
    }

    private static bool MatchesSpawnId(object prefab, string spawnId)
    {
        if (EqualsIdentifier(ReadMember(prefab, "UnitName") as string, spawnId))
            return true;

        if (prefab is Component component)
            return EqualsIdentifier(component.name, spawnId) ||
                   EqualsIdentifier(component.gameObject.name, spawnId);

        return false;
    }

    private static bool EqualsIdentifier(string? candidate, string expected)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        const string cloneSuffix = "(Clone)";
        var identifier = candidate!;
        var normalized = identifier.EndsWith(cloneSuffix, StringComparison.Ordinal)
            ? identifier.Substring(0, identifier.Length - cloneSuffix.Length).TrimEnd()
            : identifier;

        return string.Equals(normalized, expected, StringComparison.OrdinalIgnoreCase);
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
