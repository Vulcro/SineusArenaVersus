using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SineusArenaVersus.Catalog;
using SineusArenaVersus.Economy;
using SineusArenaVersus.Game;
using SineusArenaVersus.Hud;
using SineusArenaVersus.Lobby;
using SineusArenaVersus.Match;
using SineusArenaVersus.Net;
using SineusArenaVersus.Steam;
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
    internal static VersusLobby? ActiveLobby { get; private set; }

    private Harmony? _harmony;
    private SteamBootstrap? _steam;
    private SteamP2PTransport? _transport;
    private VersusNet? _net;
    private VersusHud? _hud;

    private void Awake()
    {
        Instance = this;
        VersusConfig.Bind(Config);
        _hud = gameObject.AddComponent<VersusHud>();
        _hud.LeaveMatchRequested += LeaveActiveMatch;
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
        _steam = new SteamBootstrap(exception => Logger.LogError($"Steam error: {exception}"));
        if (_steam.Initialize())
        {
            ActiveLobby = new VersusLobby(
                () => VersusConfig.MaxPlayers.Value,
                () => VersusConfig.WaveIntervalSeconds.Value,
                () => _net);
            ActiveLobby.SessionChanged += BindSteamSession;
            ActiveLobby.LobbyError += exception => Logger.LogError($"Lobby error: {exception}");
        }
        else
        {
            Logger.LogWarning("Steam unavailable; Friends Versus is disabled.");
        }

        if (VersusConfig.DebugOfflineVersus.Value)
            StartOfflineMatch();
        SyncHudBinding();
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
    }

    private void OnDestroy()
    {
        _net?.Dispose();
        _transport?.Dispose();
        ActiveLobby?.Dispose();
        ActiveMatch?.Dispose();
        _steam?.Dispose();
        ActiveLobby = null;
        ActiveMatch = null;
        _hud?.Bind(null);
        _harmony?.UnpatchSelf();
    }

    private void Update()
    {
        _steam?.RunCallbacks();
        _net?.Pump(Time.deltaTime);

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
        SyncHudBinding();
    }

    internal static void LeaveActiveMatch()
    {
        Instance._net?.Dispose();
        Instance._transport?.Dispose();
        ActiveMatch?.Dispose();
        Instance._net = null;
        Instance._transport = null;
        ActiveMatch = null;
        Instance.SyncHudBinding();
    }

    private void SyncHudBinding() => _hud?.Bind(ActiveMatch);

    private void BindSteamSession()
    {
        if (ActiveLobby is null || !ActiveLobby.HasLobby)
            return;

        _net?.Dispose();
        _transport?.Dispose();
        ActiveMatch?.Dispose();

        var localPeerId = (ulong)Steamworks.SteamClient.SteamId;
        var match = CreateMatch(localPeerId);
        _transport = new SteamP2PTransport(ActiveLobby.ContainsPeer);
        _net = new VersusNet(
            match,
            _transport,
            ActiveLobby.HostPeerId,
            () => new RivalSnapMsg(
                localPeerId,
                GameFacades.TryGetLocalKeepHp01(),
                GameFacades.IsLocalKeepAlive()));
        _net.PacketRejected += exception => Logger.LogWarning($"Rejected Versus packet: {exception.Message}");
        ActiveMatch = match;
        SyncHudBinding();
    }

    private static VersusMatch CreateMatch(ulong localPeerId)
    {
        var economy = new VersusEconomy(
            VersusConfig.PassiveBase.Value,
            VersusConfig.PassivePerSuccessfulSend.Value,
            () => VersusConfig.VpTrash.Value,
            () => VersusConfig.VpElite.Value,
            () => VersusConfig.VpBoss.Value);
        return new VersusMatch(localPeerId, economy, VersusCatalog.Load());
    }
}
