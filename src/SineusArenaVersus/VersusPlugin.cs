using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SineusArenaVersus.Catalog;
using SineusArenaVersus.Dev;
using SineusArenaVersus.Economy;
using SineusArenaVersus.Game;
using SineusArenaVersus.Hud;
using SineusArenaVersus.Lobby;
using SineusArenaVersus.Match;
using SineusArenaVersus.Net;
using SineusArenaVersus.Spectate;
using SineusArenaVersus.Steam;
using SineusArenaVersus.Ui;
using UnityEngine;

namespace SineusArenaVersus;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class VersusPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "Fowks.SineusArenaVersus";
    public const string PluginName = "Sineus Arena Versus";
    public const string PluginVersion = "0.1.15";

    internal static VersusPlugin Instance { get; private set; } = null!;
    internal static ManualLogSource Log => Instance.Logger;
    internal static VersusMatch? ActiveMatch { get; set; }
    internal static VersusLobby? ActiveLobby { get; private set; }

    private Harmony? _harmony;
    private SteamBootstrap? _steam;
    private SteamP2PTransport? _transport;
    private VersusNet? _net;
    private VersusHud? _hud;
    private VersusSpectate? _spectate;
    private VersusMenu? _menu;

    private void Awake()
    {
        Instance = this;
        VersusConfig.Bind(Config);
        _spectate = new VersusSpectate();
        _hud = gameObject.AddComponent<VersusHud>();
        _hud.LeaveMatchRequested += LeaveActiveMatch;
        _menu = gameObject.AddComponent<VersusMenu>();
        _menu.Initialize(
            () => ActiveLobby,
            () => ActiveMatch,
            _hud,
            TryEnsureSteam,
            StartSoloDevTest);
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
        VersusNet.StartCoroutine = routine => StartCoroutine(routine);
        _steam = new SteamBootstrap(
            exception => Logger.LogError($"Steam error: {exception}"),
            message => Logger.LogInfo(message));
        TryEnsureSteam();

        if (VersusConfig.DebugOfflineVersus.Value)
            StartOfflineMatch();
        SyncHudBinding();
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
    }

    private void Start()
    {
        // Game Steamworks.NET often finishes after plugin Awake — retry once.
        TryEnsureSteam();
    }

    /// <summary>
    /// Attaches to the game's Steamworks.NET session. Safe to call repeatedly.
    /// </summary>
    public bool TryEnsureSteam()
    {
        if (ActiveLobby is not null)
            return true;
        if (_steam is null)
            return false;

        if (!_steam.Initialize())
            return false;

        ActiveLobby = new VersusLobby(
            () => VersusConfig.MaxPlayers.Value,
            () => VersusConfig.WaveIntervalSeconds.Value,
            () => _net);
        ActiveLobby.SessionChanged += BindSteamSession;
        ActiveLobby.MemberLeft += HandleLobbyMemberLeft;
        ActiveLobby.LobbyError += exception => Logger.LogError($"Lobby error: {exception}");
        Logger.LogInfo("Steam Friends Versus lobby ready.");
        SyncHudBinding();
        return true;
    }

    private void OnDestroy()
    {
        _net?.Dispose();
        _transport?.Dispose();
        if (ActiveLobby is not null)
        {
            ActiveLobby.SessionChanged -= BindSteamSession;
            ActiveLobby.MemberLeft -= HandleLobbyMemberLeft;
        }
        ActiveLobby?.Dispose();
        ActiveMatch?.Dispose();
        _steam?.Dispose();
        ActiveLobby = null;
        ActiveMatch = null;
        _hud?.Bind(null);
        VersusNet.StartCoroutine = null;
        _harmony?.UnpatchSelf();
    }

    private void Update()
    {
        // Heathen/SteamTools often finishes after plugin Start — keep trying until attached.
        if (ActiveLobby is null)
            TryEnsureSteam();

        _steam?.RunCallbacks();
        _net?.Pump(Time.deltaTime);

        if (ActiveMatch?.IsActive == true)
            ActiveMatch.Tick(Time.deltaTime);

        if (ActiveMatch is not null &&
            ActiveMatch.State is VersusMatchState.InMatch
                or VersusMatchState.Eliminated
                or VersusMatchState.Ended)
        {
            var frame = VersusInput.Poll();
            _hud?.TickInput(frame);
            VersusCameraLookGate.Tick();
        }

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
        if (!SoloDevTest.TryStart(out var error))
            Logger.LogError(error ?? "Offline Versus start failed.");
        else
            SyncHudBinding();
    }

    /// <summary>Returns null on success, otherwise an error message for the UI.</summary>
    private string? StartSoloDevTest()
    {
        if (!SoloDevTest.TryStart(out var error))
            return error;
        SyncHudBinding();
        return null;
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

    private void SyncHudBinding() => _hud?.Bind(ActiveMatch, _spectate);

    private void HandleLobbyMemberLeft(ulong peerId)
    {
        try
        {
            // Solo boot / map load may leave the shared Steam lobby while P2P peers stay valid.
            if (ActiveMatch?.IsActive == true ||
                ActiveMatch?.State == VersusMatchState.LobbyBound)
                return;

            _net?.HandlePeerDisconnected(peerId);
        }
        catch (System.Exception exception)
        {
            Logger.LogError($"Failed to eliminate disconnected peer {peerId}: {exception}");
        }
    }

    private void BindSteamSession()
    {
        if (ActiveLobby is null || !ActiveLobby.HasLobby)
            return;
        // Do not tear down an in-progress Versus match when lobby membership churns during solo boot.
        if (ActiveMatch is not null &&
            ActiveMatch.State is not (VersusMatchState.Idle or VersusMatchState.Ended))
            return;

        _net?.Dispose();
        _transport?.Dispose();
        ActiveMatch?.Dispose();

        var localPeerId = SteamBootstrap.LocalSteamId();
        var match = CreateMatch(localPeerId);
        ActiveMatch = match;
        _transport = new SteamP2PTransport(IsVersusPeerAllowed);
        _net = new VersusNet(
            match,
            _transport,
            ActiveLobby.HostPeerId,
            () => new RivalSnapMsg(
                localPeerId,
                GameFacades.TryGetLocalKeepHp01(),
                GameFacades.IsLocalKeepAlive()));
        _net.PacketRejected += exception => Logger.LogWarning($"Rejected Versus packet: {exception.Message}");
        SyncHudBinding();
    }

    private bool IsVersusPeerAllowed(ulong peerId)
    {
        if (ActiveMatch is not null && ActiveMatch.Peers.ContainsKey(peerId))
            return true;
        return ActiveLobby?.ContainsPeer(peerId) == true;
    }

    private static VersusMatch CreateMatch(ulong localPeerId)
    {
        var economy = new VersusEconomy(
            VersusConfig.PassiveBase.Value,
            VersusConfig.PassivePerSuccessfulSend.Value,
            () => VersusConfig.VpTrash.Value,
            () => VersusConfig.VpElite.Value,
            () => VersusConfig.VpBoss.Value);
        return new VersusMatch(
            localPeerId,
            economy,
            VersusCatalog.Load(),
            soloRunLauncher: new ReflectionSoloRunLauncher(
                message => Log.LogError(message),
                detachVersusLobby: () => ActiveLobby?.DetachLobbyForMatch()),
            peerDisplayName: Hud.RivalCardView.FormatPeerName);
    }
}
