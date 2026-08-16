using System;
using System.Collections.Generic;
using System.Linq;
using SineusArenaVersus.Catalog;
using SineusArenaVersus.Economy;
using SineusArenaVersus.Game;
using SineusArenaVersus.Net;

namespace SineusArenaVersus.Match;

public enum VersusMatchState
{
    Idle,
    LobbyBound,
    InMatch,
    Eliminated,
    Ended
}

public sealed class VersusMatch : IDisposable
{
    private readonly ulong _localPeerId;
    private readonly VersusEconomy _economy;
    private readonly VersusCatalog _catalog;
    private readonly Func<string, int, bool> _injectPack;
    private readonly Func<float> _passiveInterval;
    private readonly Func<float> _waveInterval;
    private readonly ISoloRunLauncher _soloRunLauncher;
    private readonly bool _redirectTargetsToLocal;
    private readonly Dictionary<ulong, PeerState> _peers = new();
    private readonly List<PendingSend> _pending = new();
    private readonly List<PendingSend> _localPurchases = new();
    private readonly Dictionary<ulong, int> _peerVp = new();
    private float _passiveTimer;
    private float _waveTimer;
    private float _hostTime;
    private bool _isHost;
    private bool _eventsAttached;
    private float? _hostWaveInterval;

    public VersusMatch(
        ulong localPeerId,
        VersusEconomy economy,
        VersusCatalog catalog,
        Func<string, int, bool>? injectPack = null,
        Func<float>? passiveInterval = null,
        Func<float>? waveInterval = null,
        bool redirectTargetsToLocal = false,
        ISoloRunLauncher? soloRunLauncher = null)
    {
        _localPeerId = localPeerId;
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _injectPack = injectPack ?? GameFacades.TryInjectPack;
        _passiveInterval = passiveInterval ?? (() => VersusConfig.PassiveIntervalSeconds.Value);
        _waveInterval = waveInterval ?? (() => VersusConfig.WaveIntervalSeconds.Value);
        _redirectTargetsToLocal = redirectTargetsToLocal;
        _soloRunLauncher = soloRunLauncher ?? new ReflectionSoloRunLauncher();
    }

    public event Action<QueueSendMsg>? QueueSendRequested;
    public event Action<QueueSendMsg>? SendAcceptedForRelay;
    public event Action<WaveTickMsg>? WaveTickRequested;
    public event Action<ulong, ulong>? RefundRequested;
    public event Action<ulong>? StrongholdDownRequested;
    public event Action<ulong>? WinnerDetermined;

    public VersusMatchState State { get; private set; }
    public bool IsActive => State is VersusMatchState.InMatch or VersusMatchState.Eliminated;
    public bool ShopEnabled => State == VersusMatchState.InMatch;
    public ulong LocalPeerId => _localPeerId;
    public bool IsHost => _isHost;
    public VersusEconomy Economy => _economy;
    public VersusCatalog Catalog => _catalog;
    public int WaveIndex { get; private set; }
    public float WaveIntervalSeconds => RequireValidInterval(_hostWaveInterval ?? _waveInterval(), "wave");
    public float WaveSecondsRemaining => Math.Max(0f, WaveIntervalSeconds - _waveTimer);
    public ulong? WinnerPeerId { get; private set; }
    public IReadOnlyDictionary<ulong, PeerState> Peers => _peers;
    public IReadOnlyDictionary<ulong, int> PeerVp => _peerVp;
    public IReadOnlyList<QueueSendMsg> IncomingQueue => _pending.Select(pending => pending.Message).ToArray();
    public IReadOnlyDictionary<string, int> IncomingPreview => _pending
        .Where(IsTargetingLocal)
        .GroupBy(pending => pending.Message.CatalogId, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            group => group.Key,
            group => group.Sum(pending => pending.Message.Count),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers peers for P2P allowlisting before MatchStart is broadcast / solo isolation.
    /// </summary>
    public void RegisterPeersForStart(IReadOnlyList<ulong> peers)
    {
        if (peers is null)
            throw new ArgumentNullException(nameof(peers));
        if (State is not (VersusMatchState.Idle or VersusMatchState.Ended or VersusMatchState.LobbyBound))
            throw new InvalidOperationException($"Cannot register peers from state {State}.");
        if (!peers.Contains(_localPeerId))
            throw new ArgumentException("Peer list must contain the local peer.", nameof(peers));

        _peers.Clear();
        foreach (var peerId in peers.Distinct())
            _peers.Add(peerId, new PeerState(peerId));
        State = VersusMatchState.LobbyBound;
    }

    public bool StartMatch(IReadOnlyList<ulong> peers, bool isHost, float? waveInterval = null)
    {
        if (peers is null)
            throw new ArgumentNullException(nameof(peers));
        if (State is not (VersusMatchState.Idle or VersusMatchState.Ended or VersusMatchState.LobbyBound))
            throw new InvalidOperationException($"Cannot start a match from state {State}.");
        if (!peers.Contains(_localPeerId))
            throw new ArgumentException("Peer list must contain the local peer.", nameof(peers));
        var resolvedWaveInterval = ResolveWaveInterval(waveInterval);

        // Register peers before solo boot so P2P allowlist stays valid after lobby detach.
        _peers.Clear();
        foreach (var peerId in peers.Distinct())
            _peers.Add(peerId, new PeerState(peerId));
        _pending.Clear();
        _localPurchases.Clear();
        _peerVp.Clear();
        foreach (var peerId in _peers.Keys)
            _peerVp[peerId] = peerId == _localPeerId ? _economy.Vp : 0;
        _passiveTimer = 0f;
        _waveTimer = 0f;
        _hostTime = 0f;
        WaveIndex = 0;
        WinnerPeerId = null;
        _isHost = isHost;
        _hostWaveInterval = resolvedWaveInterval;
        State = VersusMatchState.LobbyBound;

        if (!_soloRunLauncher.TryStartSoloRun() && !_soloRunLauncher.IsSoloRunActive())
        {
            State = VersusMatchState.Idle;
            _peers.Clear();
            return false;
        }

        AttachGameEvents();
        State = VersusMatchState.InMatch;
        GameFacades.IsActive = true;
        VersusPauseGate.ForceRealtimeClock();
        return true;
    }

    public void Tick(float dt)
    {
        if (dt < 0f)
            throw new ArgumentOutOfRangeException(nameof(dt));
        if (!IsActive)
            return;

        // Co-op-style: menus must never freeze local sim during Versus.
        VersusPauseGate.EnsureRealtimeIfNeeded();

        _hostTime += dt;
        if (State == VersusMatchState.InMatch)
        {
            _passiveTimer += dt;
            TickPassiveIncome();
        }

        if (IsActive)
            _waveTimer += dt;

        if (!_isHost)
            return;

        var interval = WaveIntervalSeconds;
        while (_waveTimer >= interval)
        {
            _waveTimer -= interval;
            var tick = new WaveTickMsg(WaveIndex + 1, _hostTime);
            WaveTickRequested?.Invoke(tick);
            OnWaveTick(tick);
        }
    }

    public bool TryQueueSend(ulong target, string catalogId)
    {
        if (!ShopEnabled || target == _localPeerId ||
            !_peers.TryGetValue(target, out var peer) || !peer.IsAlive ||
            string.IsNullOrWhiteSpace(catalogId) ||
            !_catalog.TryGet(catalogId, out var offering) ||
            !_economy.TrySpend(offering.Cost))
            return false;

        var send = new QueueSendMsg(_localPeerId, target, offering.Id, offering.Count);
        _localPurchases.Add(new PendingSend(send, offering.Cost));
        QueueSendRequested?.Invoke(send);
        return true;
    }

    public void OnWaveTick(WaveTickMsg tick)
    {
        if (!IsActive || tick.WaveIndex <= WaveIndex)
            return;

        WaveIndex = tick.WaveIndex;
        if (!_isHost)
        {
            _waveTimer = 0f;
            _hostTime = tick.HostTime;
        }
        foreach (var pending in _pending.ToArray())
        {
            var send = pending.Message;
            if (!_peers.TryGetValue(send.To, out var target) || !target.IsAlive)
            {
                RefundPending(pending);
                _pending.Remove(pending);
                continue;
            }

            var injectSucceeded = true;
            if (IsTargetingLocal(pending))
                injectSucceeded = _catalog.TryGet(send.CatalogId, out var offering) &&
                                  _injectPack(offering.EnemyKey, send.Count);

            if (send.From == _localPeerId)
            {
                if (injectSucceeded)
                    _economy.RegisterSuccessfulSend();
                RemoveLocalPurchase(pending);
            }

            _pending.Remove(pending);
        }
    }

    public bool OnQueueSendValidated(QueueSendMsg send)
    {
        if (!IsActive ||
            !_catalog.TryGet(send.CatalogId, out var offering) ||
            offering.Count != send.Count ||
            !_peers.ContainsKey(send.From) ||
            !_peers.TryGetValue(send.To, out var target))
        {
            RequestRejectedRefund(send);
            return false;
        }

        if (_isHost && !target.IsAlive)
        {
            RequestRejectedRefund(send);
            return false;
        }

        if (_isHost && send.From != _localPeerId)
        {
            if (!_peerVp.TryGetValue(send.From, out var availableVp) || availableVp < offering.Cost)
            {
                RequestRejectedRefund(send);
                return false;
            }
            _peerVp[send.From] = availableVp - offering.Cost;
        }

        _pending.Add(new PendingSend(send, offering.Cost));
        if (_isHost)
            SendAcceptedForRelay?.Invoke(send);
        return true;
    }

    public void OnVpReport(ulong peerId, int vp)
    {
        if (!_isHost || !IsActive || peerId == _localPeerId || vp < 0 || !_peers.ContainsKey(peerId))
            return;
        _peerVp[peerId] = vp;
    }

    public void SetHost(bool isHost)
    {
        if (!IsActive)
            return;
        _isHost = isHost;
        if (_isHost)
            DetermineWinnerIfPossible();
    }

    public void OnRefund(ulong targetPeerId)
    {
        foreach (var purchase in _localPurchases
                     .Where(pending => pending.Message.To == targetPeerId)
                     .ToArray())
        {
            _economy.Refund(purchase.Cost);
            _localPurchases.Remove(purchase);
            _pending.RemoveAll(pending => pending.Message.Equals(purchase.Message));
        }
    }

    public void OnStrongholdDown(ulong peerId)
    {
        if (!IsActive || !_peers.TryGetValue(peerId, out var peer) || !peer.IsAlive)
            return;

        peer.IsAlive = false;
        peer.StrongholdHp01 = 0f;
        if (peerId == _localPeerId)
            State = VersusMatchState.Eliminated;
        if (!_isHost)
            return;

        DetermineWinnerIfPossible();
    }

    public void OnWinner(ulong peerId)
    {
        if (State == VersusMatchState.Ended)
            return;
        if (!_peers.ContainsKey(peerId))
            return;
        EndMatch(peerId);
    }

    public void OnRivalSnap(RivalSnapMsg snapshot)
    {
        if (!IsActive ||
            !_peers.TryGetValue(snapshot.PeerId, out var peer) ||
            snapshot.PeerId == _localPeerId ||
            float.IsNaN(snapshot.StrongholdHp01))
            return;

        peer.StrongholdHp01 = Math.Max(0f, Math.Min(1f, snapshot.StrongholdHp01));
    }

    public void Dispose()
    {
        if (!_eventsAttached)
            return;
        GameFacades.EnemyKilled -= HandleEnemyKilled;
        GameFacades.LocalKeepDestroyed -= HandleLocalKeepDestroyed;
        _eventsAttached = false;
        GameFacades.IsActive = false;
    }

    private void EndMatch(ulong winnerPeerId)
    {
        State = VersusMatchState.Ended;
        WinnerPeerId = winnerPeerId;
        GameFacades.IsActive = false;
        WinnerDetermined?.Invoke(winnerPeerId);
    }

    private bool IsTargetingLocal(PendingSend pending) =>
        pending.Message.To == _localPeerId || _redirectTargetsToLocal;

    private void RefundPending(PendingSend pending)
    {
        if (pending.Message.From == _localPeerId)
        {
            _economy.Refund(pending.Cost);
            RemoveLocalPurchase(pending);
            return;
        }

        if (_isHost)
        {
            if (_peerVp.TryGetValue(pending.Message.From, out var vp))
                _peerVp[pending.Message.From] = vp + pending.Cost;
            RefundRequested?.Invoke(pending.Message.From, pending.Message.To);
        }
    }

    private void RemoveLocalPurchase(PendingSend settled)
    {
        var purchase = _localPurchases.FirstOrDefault(candidate =>
            candidate.Message.Equals(settled.Message));
        if (purchase is not null)
            _localPurchases.Remove(purchase);
    }

    private void TickPassiveIncome()
    {
        var interval = RequireValidInterval(_passiveInterval(), "passive");
        while (_passiveTimer >= interval)
        {
            _passiveTimer -= interval;
            _economy.OnPassiveTick();
        }
    }

    private float ResolveWaveInterval(float? requested)
    {
        if (requested.HasValue && IsValidInterval(requested.Value))
            return requested.Value;
        return RequireValidInterval(_waveInterval(), "wave");
    }

    private static bool IsValidInterval(float interval) =>
        !float.IsNaN(interval) && !float.IsInfinity(interval) && interval > 0f;

    private static float RequireValidInterval(float interval, string name)
    {
        if (!IsValidInterval(interval))
            throw new InvalidOperationException($"Configured {name} interval must be finite and positive.");
        return interval;
    }

    private void RequestRejectedRefund(QueueSendMsg send)
    {
        if (_isHost)
            RefundRequested?.Invoke(send.From, send.To);
    }

    private void AttachGameEvents()
    {
        if (_eventsAttached)
            return;
        GameFacades.EnemyKilled += HandleEnemyKilled;
        GameFacades.LocalKeepDestroyed += HandleLocalKeepDestroyed;
        _eventsAttached = true;
    }

    private void HandleEnemyKilled(KillTier tier)
    {
        if (State == VersusMatchState.InMatch)
            _economy.AddKillVp(tier);
    }

    private void HandleLocalKeepDestroyed()
    {
        if (State != VersusMatchState.InMatch)
            return;
        StrongholdDownRequested?.Invoke(_localPeerId);
        OnStrongholdDown(_localPeerId);
    }

    private void DetermineWinnerIfPossible()
    {
        var survivors = _peers.Values.Where(candidate => candidate.IsAlive).ToArray();
        if (survivors.Length == 1)
            EndMatch(survivors[0].PeerId);
    }

    private sealed class PendingSend
    {
        public PendingSend(QueueSendMsg message, int cost)
        {
            Message = message;
            Cost = cost;
        }

        public QueueSendMsg Message { get; }
        public int Cost { get; }
    }
}
