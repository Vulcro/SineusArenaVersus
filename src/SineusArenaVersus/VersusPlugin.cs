using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SineusArenaVersus.Catalog;
using SineusArenaVersus.Economy;
using SineusArenaVersus.Game;
using SineusArenaVersus.Match;
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
    internal static VersusMatch? ActiveMatch { get; set; }

    private Harmony? _harmony;

    private void Awake()
    {
        Instance = this;
        VersusConfig.Bind(Config);
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
        if (VersusConfig.DebugOfflineVersus.Value)
            StartOfflineMatch();
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
    }

    private void OnDestroy()
    {
        ActiveMatch?.Dispose();
        _harmony?.UnpatchSelf();
    }

    private void Update()
    {
        if (ActiveMatch?.IsActive == true)
            ActiveMatch.Tick(Time.deltaTime);

        if (!VersusConfig.DebugForceInject.Value ||
            !System.Enum.TryParse(VersusConfig.DebugInjectKey.Value, true, out KeyCode key) ||
            !Input.GetKeyDown(key))
            return;

        var injected = GameFacades.TryInjectPack(
            VersusConfig.DebugEnemyKey.Value,
            VersusConfig.DebugEnemyCount.Value);
        Logger.LogInfo($"Debug enemy inject {(injected ? "scheduled" : "failed")}");
    }

    private void StartOfflineMatch()
    {
        var localPeerId = VersusConfig.DebugLocalPeerId.Value;
        var rivalPeerId = VersusConfig.DebugRivalPeerId.Value;
        if (localPeerId == rivalPeerId)
            throw new System.InvalidOperationException("Offline local and rival peer ids must differ.");

        var economy = new VersusEconomy(
            VersusConfig.PassiveBase.Value,
            VersusConfig.PassivePerSuccessfulSend.Value,
            () => VersusConfig.VpTrash.Value,
            () => VersusConfig.VpElite.Value,
            () => VersusConfig.VpBoss.Value);
        var match = new VersusMatch(
            localPeerId,
            economy,
            VersusCatalog.Load(),
            redirectTargetsToLocal: true);
        match.QueueSendRequested += match.OnQueueSendValidated;
        match.StartMatch(new[] { localPeerId, rivalPeerId }, isHost: true);
        ActiveMatch = match;
    }
}
