using BepInEx.Configuration;

namespace SineusArenaVersus;

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
    public static ConfigEntry<bool> DebugForceInject = null!;
    public static ConfigEntry<bool> DebugOfflineVersus = null!;
    public static ConfigEntry<ulong> DebugLocalPeerId = null!;
    public static ConfigEntry<ulong> DebugRivalPeerId = null!;
    public static ConfigEntry<string> DebugInjectKey = null!;
    public static ConfigEntry<string> DebugEnemyKey = null!;
    public static ConfigEntry<int> DebugEnemyCount = null!;
    public static ConfigEntry<float> InjectRadius = null!;

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
        DebugForceInject = cfg.Bind("Debug", "DebugForceInject", false, "Enable the manual enemy inject key");
        DebugOfflineVersus = cfg.Bind("Debug", "DebugOfflineVersus", false, "Run a local host match without Steam");
        DebugLocalPeerId = cfg.Bind("Debug", "DebugLocalPeerId", 1UL, "Synthetic local peer id for offline Versus");
        DebugRivalPeerId = cfg.Bind("Debug", "DebugRivalPeerId", 2UL, "Synthetic rival peer id for offline Versus");
        DebugInjectKey = cfg.Bind("Debug", "DebugInjectKey", "F8", "Unity KeyCode used for manual enemy inject");
        DebugEnemyKey = cfg.Bind("Debug", "DebugEnemyKey", "trash", "Catalog enemyKey used for manual inject");
        DebugEnemyCount = cfg.Bind("Debug", "DebugEnemyCount", 3,
            new ConfigDescription("Manual inject pack size", new AcceptableValueRange<int>(1, 100)));
        InjectRadius = cfg.Bind("Versus", "InjectRadius", 15f,
            new ConfigDescription("Enemy inject radius around the local player", new AcceptableValueRange<float>(1f, 100f)));
    }
}
