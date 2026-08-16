using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SineusArenaVersus.Game;
using UnityEngine;

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

    private void Update()
    {
        if (!VersusConfig.DebugForceInject.Value ||
            !System.Enum.TryParse(VersusConfig.DebugInjectKey.Value, true, out KeyCode key) ||
            !Input.GetKeyDown(key))
            return;

        var injected = GameFacades.TryInjectPack(
            VersusConfig.DebugEnemyKey.Value,
            VersusConfig.DebugEnemyCount.Value);
        Logger.LogInfo($"Debug enemy inject {(injected ? "scheduled" : "failed")}");
    }
}
