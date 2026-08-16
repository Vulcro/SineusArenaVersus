using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SineusArenaVersus.Match;
using SineusArenaVersus.Steam;
using Steamworks;

namespace SineusArenaVersus.Net;

public readonly record struct ReceivedPacket(ulong SenderId, byte[] Payload);

public interface IVersusTransport
{
    ulong LocalPeerId { get; }
    bool Send(ulong peerId, byte[] payload, bool reliable);
    bool TryReceive(out ReceivedPacket packet);
}

public sealed class VersusNet : IDisposable
{
    public const float DefaultSnapshotIntervalSeconds = 1f / 3f;
    public const float VpReportIntervalSeconds = 1f;

    /// <summary>
    /// Optional Unity coroutine starter (set by the plugin). Tests leave this null for sync start.
    /// </summary>
    public static Func<IEnumerator, object?>? StartCoroutine { get; set; }

    private readonly VersusMatch _match;
    private readonly IVersusTransport _transport;
    private readonly Func<RivalSnapMsg>? _localSnapshot;
    private readonly float _snapshotIntervalSeconds;
    private ulong _hostPeerId;
    private readonly HashSet<ulong> _disconnectedPeers = new();
    private float _snapshotTimer;
    private float _vpReportTimer;
    private int _lastReportedVp = -1;
    private bool _disposed;

    public VersusNet(
        VersusMatch match,
        IVersusTransport transport,
        ulong hostPeerId,
        Func<RivalSnapMsg>? localSnapshot = null,
        float snapshotIntervalSeconds = DefaultSnapshotIntervalSeconds)
    {
        _match = match ?? throw new ArgumentNullException(nameof(match));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        if (hostPeerId == 0)
            throw new ArgumentOutOfRangeException(nameof(hostPeerId));
        if (snapshotIntervalSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(snapshotIntervalSeconds));

        _hostPeerId = hostPeerId;
        _localSnapshot = localSnapshot;
        _snapshotIntervalSeconds = snapshotIntervalSeconds;
        AttachMatchEvents();
    }

    public event Action<ulong>? WinnerReceived;
    public event Action<Exception>? PacketRejected;

    public bool IsHost => _transport.LocalPeerId == _hostPeerId;
    public ulong HostPeerId => _hostPeerId;

    public bool StartMatchAsHost(ulong lobbyId, IReadOnlyList<ulong> peers, float waveInterval)
    {
        ThrowIfDisposed();
        if (!IsHost)
            throw new InvalidOperationException("Only the host can start a match.");
        if (peers is null)
            throw new ArgumentNullException(nameof(peers));

        var peerArray = new ulong[peers.Count];
        for (var i = 0; i < peers.Count; i++)
            peerArray[i] = peers[i];

        var resolvedWave = waveInterval > 0f ? waveInterval : 20f;
        _match.RegisterPeersForStart(peerArray);

        // Notify friends while shared Steam lobby / P2P allowlist still work.
        var packet = VersusSerializer.Serialize(new MatchStartMsg(lobbyId, resolvedWave, peerArray));
        Broadcast(VersusOpcode.MatchStart, packet, peerArray);

        // In-game: delay solo isolation so MatchStart can flush. Tests run sync (no coroutine starter).
        if (StartCoroutine is null)
            return _match.StartMatch(peerArray, isHost: true, resolvedWave);

        StartCoroutine(HostSoloAfterNotify(peerArray, resolvedWave));
        return true;
    }

    private IEnumerator HostSoloAfterNotify(ulong[] peerArray, float waveInterval)
    {
        yield return null;
        yield return null;
        if (!_match.StartMatch(peerArray, isHost: true, waveInterval))
        {
            // Logging without referencing BepInEx / VersusPlugin (keeps unit tests loadable).
            UnityEngine.Debug.LogError("[SineusArenaVersus] Versus host solo boot failed after MatchStart broadcast.");
        }
    }

    public void Pump(float deltaTime)
    {
        ThrowIfDisposed();
        if (deltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));

        while (_transport.TryReceive(out var packet))
        {
            try
            {
                Route(packet);
            }
            catch (Exception exception) when (
                exception is ArgumentException or EndOfStreamException or InvalidDataException or InvalidOperationException)
            {
                PacketRejected?.Invoke(exception);
            }
        }

        TickSnapshot(deltaTime);
        TickVpReport(deltaTime);
    }

    public void Broadcast(VersusOpcode opcode, byte[] payload)
    {
        ThrowIfDisposed();
        Broadcast(opcode, payload, _match.Peers.Keys);
    }

    public void SendTo(ulong peer, VersusOpcode opcode, byte[] payload)
    {
        ThrowIfDisposed();
        ValidatePacket(opcode, payload);
        if (peer == _transport.LocalPeerId)
            return;
        if (!_transport.Send(peer, payload, IsReliable(opcode)))
            throw new IOException($"Steam transport rejected {opcode} for peer {peer}.");
    }

    public void HandlePeerDisconnected(ulong peerId)
    {
        ThrowIfDisposed();
        if (!_match.IsActive ||
            !_match.Peers.TryGetValue(peerId, out var peer) ||
            !peer.IsAlive)
            return;

        _disconnectedPeers.Add(peerId);
        if (peerId == _hostPeerId)
        {
            _match.OnStrongholdDown(peerId);
            var nextHost = FindLowestSurvivingPeer();
            if (!nextHost.HasValue)
                return;

            _hostPeerId = nextHost.Value;
            if (IsHost)
            {
                var hostDownPayload = VersusSerializer.SerializePeer(
                    VersusOpcode.StrongholdDown,
                    new PeerMsg(peerId));
                Broadcast(VersusOpcode.StrongholdDown, hostDownPayload);
            }
            _match.SetHost(IsHost);
            return;
        }

        if (!IsHost)
            return;

        var payload = VersusSerializer.SerializePeer(
            VersusOpcode.StrongholdDown,
            new PeerMsg(peerId));
        Broadcast(VersusOpcode.StrongholdDown, payload);
        _match.OnStrongholdDown(peerId);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _match.QueueSendRequested -= HandleQueueSendRequested;
        _match.SendAcceptedForRelay -= HandleSendAcceptedForRelay;
        _match.WaveTickRequested -= HandleWaveTickRequested;
        _match.RefundRequested -= HandleRefundRequested;
        _match.StrongholdDownRequested -= HandleStrongholdDownRequested;
        _match.WinnerDetermined -= HandleWinnerDetermined;
        _disposed = true;
    }

    private void AttachMatchEvents()
    {
        _match.QueueSendRequested += HandleQueueSendRequested;
        _match.SendAcceptedForRelay += HandleSendAcceptedForRelay;
        _match.WaveTickRequested += HandleWaveTickRequested;
        _match.RefundRequested += HandleRefundRequested;
        _match.StrongholdDownRequested += HandleStrongholdDownRequested;
        _match.WinnerDetermined += HandleWinnerDetermined;
    }

    private void Route(ReceivedPacket packet)
    {
        if (packet.Payload is null)
            throw new ArgumentNullException(nameof(packet.Payload));

        var opcode = VersusSerializer.GetOpcode(packet.Payload);
        switch (opcode)
        {
            case VersusOpcode.MatchStart:
                RequireHost(packet.SenderId, opcode);
                var start = VersusSerializer.DeserializeMatchStart(packet.Payload);
                if (!_match.StartMatch(start.Peers, isHost: false, start.WaveInterval))
                    throw new InvalidOperationException("Versus match start aborted because the local solo run could not start.");
                break;
            case VersusOpcode.WaveTick:
                RequireHost(packet.SenderId, opcode);
                _match.OnWaveTick(VersusSerializer.DeserializeWaveTick(packet.Payload));
                break;
            case VersusOpcode.QueueSend:
                RouteQueueSend(packet);
                break;
            case VersusOpcode.RivalSnap:
                var snap = VersusSerializer.DeserializeRivalSnap(packet.Payload);
                if (snap.PeerId != packet.SenderId)
                    throw new InvalidDataException("Rival snapshot sender does not match its peer id.");
                _match.OnRivalSnap(snap);
                break;
            case VersusOpcode.StrongholdDown:
                RouteStrongholdDown(packet);
                break;
            case VersusOpcode.Winner:
                RequireHost(packet.SenderId, opcode);
                var winner = VersusSerializer.DeserializePeer(packet.Payload).PeerId;
                _match.OnWinner(winner);
                WinnerReceived?.Invoke(winner);
                break;
            case VersusOpcode.Refund:
                RequireHost(packet.SenderId, opcode);
                _match.OnRefund(VersusSerializer.DeserializePeer(packet.Payload).PeerId);
                break;
            case VersusOpcode.VpReport:
                RouteVpReport(packet);
                break;
            case VersusOpcode.Ready:
                throw new InvalidDataException("Ready state is carried by Steam lobby member data.");
            default:
                throw new InvalidDataException($"Unknown Versus opcode {(byte)opcode}.");
        }
    }

    private void RouteQueueSend(ReceivedPacket packet)
    {
        var send = VersusSerializer.DeserializeQueueSend(packet.Payload);
        if (IsHost)
        {
            if (send.From != packet.SenderId)
                throw new InvalidDataException("Queue sender does not match its peer id.");
            _match.OnQueueSendValidated(send);
            return;
        }

        RequireHost(packet.SenderId, VersusOpcode.QueueSend);
        _match.OnQueueSendValidated(send);
    }

    private void RouteStrongholdDown(ReceivedPacket packet)
    {
        var message = VersusSerializer.DeserializePeer(packet.Payload);
        if (IsHost)
        {
            if (message.PeerId != packet.SenderId)
                throw new InvalidDataException("Eliminated peer does not match packet sender.");
            if (!_match.Peers.TryGetValue(message.PeerId, out var peer) || !peer.IsAlive)
                return;
            Broadcast(VersusOpcode.StrongholdDown, packet.Payload);
            _match.OnStrongholdDown(message.PeerId);
            return;
        }

        RequireHost(packet.SenderId, VersusOpcode.StrongholdDown);
        _match.OnStrongholdDown(message.PeerId);
    }

    private void RouteVpReport(ReceivedPacket packet)
    {
        if (!IsHost)
            throw new InvalidDataException("Only the host accepts VP reports.");

        var report = VersusSerializer.DeserializeVpReport(packet.Payload);
        if (report.PeerId != packet.SenderId)
            throw new InvalidDataException("VP report sender does not match its peer id.");
        if (report.Vp < 0)
            throw new InvalidDataException("VP report cannot be negative.");
        _match.OnVpReport(report.PeerId, report.Vp);
    }

    private void TickSnapshot(float deltaTime)
    {
        if (_localSnapshot is null ||
            _match.State != VersusMatchState.InMatch ||
            !_match.Peers.TryGetValue(_transport.LocalPeerId, out var localPeer) ||
            !localPeer.IsAlive)
            return;

        _snapshotTimer += deltaTime;
        if (_snapshotTimer < _snapshotIntervalSeconds)
            return;
        _snapshotTimer %= _snapshotIntervalSeconds;

        var snapshot = _localSnapshot();
        if (snapshot.PeerId != _transport.LocalPeerId)
            throw new InvalidOperationException("Local snapshot peer id does not match the transport.");
        Broadcast(VersusOpcode.RivalSnap, VersusSerializer.Serialize(snapshot));
    }

    private void TickVpReport(float deltaTime)
    {
        if (IsHost || _match.State != VersusMatchState.InMatch)
            return;

        _vpReportTimer += deltaTime;
        var currentVp = _match.Economy.Vp;
        if (currentVp == _lastReportedVp && _vpReportTimer < VpReportIntervalSeconds)
            return;

        SendVpReport(currentVp);
    }

    private void HandleQueueSendRequested(QueueSendMsg message)
    {
        if (IsHost)
            _match.OnQueueSendValidated(message);
        else
        {
            if (!_match.Catalog.TryGet(message.CatalogId, out var offering))
                throw new InvalidOperationException($"Unknown local send catalog id '{message.CatalogId}'.");
            SendVpReport(checked(_match.Economy.Vp + offering.Cost));
            SendTo(_hostPeerId, VersusOpcode.QueueSend, VersusSerializer.Serialize(message));
        }
    }

    private void HandleSendAcceptedForRelay(QueueSendMsg message)
    {
        if (IsHost)
            Broadcast(VersusOpcode.QueueSend, VersusSerializer.Serialize(message));
    }

    private void HandleWaveTickRequested(WaveTickMsg message)
    {
        if (IsHost)
            Broadcast(VersusOpcode.WaveTick, VersusSerializer.Serialize(message));
    }

    private void HandleRefundRequested(ulong sender, ulong target)
    {
        if (IsHost)
            SendTo(sender, VersusOpcode.Refund, VersusSerializer.SerializePeer(
                VersusOpcode.Refund,
                new PeerMsg(target)));
    }

    private void HandleStrongholdDownRequested(ulong peer)
    {
        var payload = VersusSerializer.SerializePeer(VersusOpcode.StrongholdDown, new PeerMsg(peer));
        if (IsHost)
            Broadcast(VersusOpcode.StrongholdDown, payload);
        else
            SendTo(_hostPeerId, VersusOpcode.StrongholdDown, payload);
    }

    private void HandleWinnerDetermined(ulong peer)
    {
        if (!IsHost)
            return;
        Broadcast(VersusOpcode.Winner, VersusSerializer.SerializePeer(
            VersusOpcode.Winner,
            new PeerMsg(peer)));
        WinnerReceived?.Invoke(peer);
    }

    private void Broadcast(VersusOpcode opcode, byte[] payload, IEnumerable<ulong> peers)
    {
        ValidatePacket(opcode, payload);
        foreach (var peer in peers)
        {
            if (_disconnectedPeers.Contains(peer))
                continue;
            SendTo(peer, opcode, payload);
        }
    }

    private void RequireHost(ulong sender, VersusOpcode opcode)
    {
        if (sender != _hostPeerId)
            throw new InvalidDataException($"{opcode} must come from the lobby host.");
    }

    private static void ValidatePacket(VersusOpcode opcode, byte[] payload)
    {
        if (payload is null)
            throw new ArgumentNullException(nameof(payload));
        if (VersusSerializer.GetOpcode(payload) != opcode)
            throw new InvalidDataException("Packet opcode does not match the requested route.");
    }

    private static bool IsReliable(VersusOpcode opcode) => opcode != VersusOpcode.RivalSnap;

    private void SendVpReport(int vp)
    {
        _vpReportTimer = 0f;
        _lastReportedVp = vp;
        SendTo(_hostPeerId, VersusOpcode.VpReport, VersusSerializer.Serialize(
            new VpReportMsg(_transport.LocalPeerId, vp)));
    }

    private ulong? FindLowestSurvivingPeer()
    {
        ulong? lowest = null;
        foreach (var peer in _match.Peers.Values)
        {
            if (!peer.IsAlive || _disconnectedPeers.Contains(peer.PeerId))
                continue;
            if (!lowest.HasValue || peer.PeerId < lowest.Value)
                lowest = peer.PeerId;
        }
        return lowest;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VersusNet));
    }
}

public sealed class SteamP2PTransport : IVersusTransport, IDisposable
{
    private readonly Func<ulong, bool> _isAllowedPeer;
    private readonly int _channel;
    private readonly Callback<P2PSessionRequest_t> _sessionRequest;
    private readonly byte[] _receiveBuffer = new byte[1024 * 512];
    private bool _disposed;

    public SteamP2PTransport(Func<ulong, bool> isAllowedPeer, int channel = 0)
    {
        _isAllowedPeer = isAllowedPeer ?? throw new ArgumentNullException(nameof(isAllowedPeer));
        if (!SteamSession.IsAttached())
            throw new InvalidOperationException("Steam must be available before creating the transport.");

        _channel = channel;
        _sessionRequest = Callback<P2PSessionRequest_t>.Create(HandleSessionRequest);
        SteamNetworking.AllowP2PPacketRelay(true);
    }

    public ulong LocalPeerId => SteamUser.GetSteamID().m_SteamID;

    public bool Send(ulong peerId, byte[] payload, bool reliable)
    {
        ThrowIfDisposed();
        if (payload is null)
            throw new ArgumentNullException(nameof(payload));
        if (!_isAllowedPeer(peerId))
            return false;

        return SteamNetworking.SendP2PPacket(
            new CSteamID(peerId),
            payload,
            (uint)payload.Length,
            reliable
                ? EP2PSend.k_EP2PSendReliable
                : EP2PSend.k_EP2PSendUnreliable,
            _channel);
    }

    public bool TryReceive(out ReceivedPacket packet)
    {
        ThrowIfDisposed();
        packet = default;
        uint available;
        if (!SteamNetworking.IsP2PPacketAvailable(out available, _channel) || available == 0)
            return false;
        if (available > _receiveBuffer.Length)
            return false;

        CSteamID remote = default;
        if (!SteamNetworking.ReadP2PPacket(
                _receiveBuffer,
                available,
                out var msgSize,
                out remote,
                _channel))
            return false;
        if (!_isAllowedPeer(remote.m_SteamID) || msgSize == 0)
            return false;

        var data = new byte[msgSize];
        Buffer.BlockCopy(_receiveBuffer, 0, data, 0, (int)msgSize);
        packet = new ReceivedPacket(remote.m_SteamID, data);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _sessionRequest.Dispose();
        _disposed = true;
    }

    private void HandleSessionRequest(P2PSessionRequest_t request)
    {
        if (_isAllowedPeer(request.m_steamIDRemote.m_SteamID))
            SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SteamP2PTransport));
    }
}
