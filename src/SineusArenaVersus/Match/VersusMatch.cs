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
    private readonly bool _redirectTargetsToLocal;
    private readonly Dictionary<ulong, PeerState> _peers = new();
    private readonly List<QueueSendMsg> _pending = new();
    private readonly List<QueueSendMsg> _localPurchases = new();
    private float _passiveTimer;
    private float _waveTimer;
    private float _hostTime;
    private bool _isHost;
    private bool _eventsAttached;

    public VersusMatch(
        ulong localPeerId,
        VersusEconomy economy,
        VersusCatalog catalog,
        Func<string, int, bool>? injectPack = null,
        Func<float>? passiveInterval = null,
        Func<float>? waveInterval = null,
        bool redirectTargetsToLocal = false)
    {
        _localPeerId = localPeerId;
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _injectPack = injectPack ?? GameFacades.TryInjectPack;
        _passiveInterval = passiveInterval ?? (() => VersusConfig.PassiveIntervalSeconds.Value);
        _waveInterval = waveInterval ?? (() => VersusConfig.WaveIntervalSeconds.Value);
        _redirectTargetsToLocal = redirectTargetsToLocal;
    }

    public event Action<QueueSendMsg>? QueueSendRequested;
    public event Action<WaveTickMsg>? WaveTickRequested;
    public event Action<ulong, ulong>? RefundRequested;
    public event Action<ulong>? StrongholdDownRequested;
    public event Action<ulong>? WinnerDetermined;

    public VersusMatchState State { get; private set; }
    public bool IsActive => State is VersusMatchState.InMatch or VersusMatchState.Eliminated;
    public bool ShopEnabled => State == VersusMatchState.InMatch;
    public int WaveIndex { get; private set; }
    public IReadOnlyDictionary<ulong, PeerState> Peers => _peers;
    public IReadOnlyList<QueueSendMsg> IncomingQueue => _pending;
    public IReadOnlyDictionary<string, int> IncomingPreview => _pending
        .Where(IsTargetingLocal)
        .GroupBy(send => send.CatalogId, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Sum(send => send.Count), StringComparer.OrdinalIgnoreCase);

    public void StartMatch(IReadOnlyList<ulong> peers, bool isHost)
    {
        if (peers is null)
            throw new ArgumentNullException(nameof(peers));
        if (State is not (VersusMatchState.Idle or VersusMatchState.Ended))
            throw new InvalidOperationException($"Cannot start a match from state {State}.");
        if (!peers.Contains(_localPeerId))
            throw new ArgumentException("Peer list must contain the local peer.", nameof(peers));

        State = VersusMatchState.LobbyBound;
        _peers.Clear();
        foreach (var peerId in peers.Distinct())
            _peers.Add(peerId, new PeerState(peerId));

        _pending.Clear();
        _localPurchases.Clear();
        _passiveTimer = 0f;
        _waveTimer = 0f;
        _hostTime = 0f;
        WaveIndex = 0;
        _isHost = isHost;
        AttachGameEvents();
        State = VersusMatchState.InMatch;
        GameFacades.IsActive = true;
    }

    public void Tick(float dt)
    {
        if (dt < 0f)
            throw new ArgumentOutOfRangeException(nameof(dt));
        if (State != VersusMatchState.InMatch)
            return;

        _hostTime += dt;
        _passiveTimer += dt;
        TickPassiveIncome();

        if (!_isHost)
            return;

        _waveTimer += dt;
        var interval = RequirePositiveInterval(_waveInterval(), "wave");
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
        _localPurchases.Add(send);
        QueueSendRequested?.Invoke(send);
        return true;
    }

    public void OnWaveTick(WaveTickMsg tick)
    {
        if (!IsActive || tick.WaveIndex <= WaveIndex)
            return;

        WaveIndex = tick.WaveIndex;
        var localSends = _pending.Where(IsTargetingLocal).ToArray();
        foreach (var send in localSends)
        {
            if (_catalog.TryGet(send.CatalogId, out var offering) &&
                _injectPack(offering.EnemyKey, send.Count) &&
                send.From == _localPeerId)
                _economy.RegisterSuccessfulSend();

            _pending.Remove(send);
            _localPurchases.Remove(send);
        }
    }

    public void OnQueueSendValidated(QueueSendMsg send)
    {
        if (State != VersusMatchState.InMatch ||
            !_catalog.TryGet(send.CatalogId, out var offering) ||
            offering.Count != send.Count ||
            !_peers.ContainsKey(send.From) ||
            !_peers.TryGetValue(send.To, out var target))
            return;

        if (_isHost && !target.IsAlive)
        {
            RefundRequested?.Invoke(send.From, send.To);
            return;
        }

        _pending.Add(send);
    }

    public void OnRefund(ulong targetPeerId)
    {
        foreach (var purchase in _localPurchases.Where(send => send.To == targetPeerId).ToArray())
        {
            if (_catalog.TryGet(purchase.CatalogId, out var offering))
                _economy.Refund(offering.Cost);
            _localPurchases.Remove(purchase);
            _pending.Remove(purchase);
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

        var survivors = _peers.Values.Where(candidate => candidate.IsAlive).ToArray();
        if (survivors.Length == 1)
        {
            State = VersusMatchState.Ended;
            GameFacades.IsActive = false;
            WinnerDetermined?.Invoke(survivors[0].PeerId);
        }
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

    private bool IsTargetingLocal(QueueSendMsg send) =>
        send.To == _localPeerId || _redirectTargetsToLocal;

    private void TickPassiveIncome()
    {
        var interval = RequirePositiveInterval(_passiveInterval(), "passive");
        while (_passiveTimer >= interval)
        {
            _passiveTimer -= interval;
            _economy.OnPassiveTick();
        }
    }

    private static float RequirePositiveInterval(float interval, string name)
    {
        if (interval <= 0f)
            throw new InvalidOperationException($"Configured {name} interval must be positive.");
        return interval;
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
}
