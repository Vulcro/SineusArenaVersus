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
    public static ConfigEntry<string> OpenVersusMenuKey = null!;
    public static ConfigEntry<string> CatalogOverridePath = null!;
    public static ConfigEntry<bool> DebugForceInject = null!;
    public static ConfigEntry<bool> DebugOfflineVersus = null!;
    public static ConfigEntry<bool> EnableSoloDevTest = null!;
    public static ConfigEntry<bool> SoloDevBootArena = null!;
    public static ConfigEntry<ulong> DebugLocalPeerId = null!;
    public static ConfigEntry<ulong> DebugRivalPeerId = null!;
    public static ConfigEntry<string> DebugInjectKey = null!;
    public static ConfigEntry<string> DebugEnemyKey = null!;
    public static ConfigEntry<int> DebugEnemyCount = null!;
    public static ConfigEntry<float> InjectRadius = null!;
    public static ConfigEntry<bool> ShowInjectSenderLabels = null!;
    public static ConfigEntry<bool> ShowInjectSenderLights = null!;
    public static ConfigEntry<float> InjectMarkerLightRange = null!;
    public static ConfigEntry<float> InjectMarkerLightIntensity = null!;
    public static ConfigEntry<bool> EnableSpectateViews = null!;

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
        OpenVersusMenuKey = cfg.Bind("Versus", "OpenVersusMenuKey", "F8", "Unity KeyCode used to open Versus or collapse its HUD");
        CatalogOverridePath = cfg.Bind("Versus", "CatalogOverridePath", "", "Optional absolute path to catalog.json override");
        DebugForceInject = cfg.Bind("Debug", "DebugForceInject", false, "Enable the manual enemy inject key");
        DebugOfflineVersus = cfg.Bind("Debug", "DebugOfflineVersus", false, "Auto-start offline Versus on plugin load (legacy)");
        EnableSoloDevTest = cfg.Bind(
            "Debug",
            "EnableSoloDevTest",
            false,
            "Show Solo Dev Test in the F8 menu (local fake rival; sends inject on you)");
        SoloDevBootArena = cfg.Bind(
            "Debug",
            "SoloDevBootArena",
            true,
            "Solo Dev Test also boots a local solo arena; if false, attach only when already in a solo run");
        DebugLocalPeerId = cfg.Bind("Debug", "DebugLocalPeerId", 1UL, "Synthetic local peer id when Steam is unavailable");
        DebugRivalPeerId = cfg.Bind("Debug", "DebugRivalPeerId", 2UL, "Synthetic rival peer id for Solo Dev / offline Versus");
        DebugInjectKey = cfg.Bind("Debug", "DebugInjectKey", "F9", "Unity KeyCode used for manual enemy inject");
        DebugEnemyKey = cfg.Bind("Debug", "DebugEnemyKey", "trash", "Catalog enemyKey used for manual inject");
        DebugEnemyCount = cfg.Bind("Debug", "DebugEnemyCount", 3,
            new ConfigDescription("Manual inject pack size", new AcceptableValueRange<int>(1, 100)));
        InjectRadius = cfg.Bind("Versus", "InjectRadius", 15f,
            new ConfigDescription("Enemy inject radius around the local player", new AcceptableValueRange<float>(1f, 100f)));
        ShowInjectSenderLabels = cfg.Bind(
            "Versus",
            "ShowInjectSenderLabels",
            true,
            "Show P1/P2 (+ name) labels above Versus-injected enemies");
        ShowInjectSenderLights = cfg.Bind(
            "Versus",
            "ShowInjectSenderLights",
            true,
            "Tinted point light on Versus-injected enemies matching the sender slot color");
        InjectMarkerLightRange = cfg.Bind(
            "Versus",
            "InjectMarkerLightRange",
            4f,
            new ConfigDescription("Point light range for inject markers", new AcceptableValueRange<float>(1f, 20f)));
        InjectMarkerLightIntensity = cfg.Bind(
            "Versus",
            "InjectMarkerLightIntensity",
            1.8f,
            new ConfigDescription("Point light intensity for inject markers", new AcceptableValueRange<float>(0.1f, 8f)));
        EnableSpectateViews = cfg.Bind(
            "Polish",
            "EnableSpectateViews",
            false,
            "SubViewport rival mini-view (0.2.0 polish; V1 stub only, default off)");
    }
}
