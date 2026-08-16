using System;
using System.Collections.Generic;
using System.IO;
using SineusArenaVersus.Match;

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

    private readonly VersusMatch _match;
    private readonly IVersusTransport _transport;
    private readonly Func<RivalSnapMsg>? _localSnapshot;
    private readonly float _snapshotIntervalSeconds;
    private readonly ulong _hostPeerId;
    private float _snapshotTimer;
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

    public void StartMatchAsHost(ulong lobbyId, IReadOnlyList<ulong> peers, float waveInterval)
    {
        ThrowIfDisposed();
        if (!IsHost)
            throw new InvalidOperationException("Only the host can start a match.");
        if (peers is null)
            throw new ArgumentNullException(nameof(peers));

        var peerArray = new ulong[peers.Count];
        for (var i = 0; i < peers.Count; i++)
            peerArray[i] = peers[i];

        var packet = VersusSerializer.Serialize(new MatchStartMsg(lobbyId, waveInterval, peerArray));
        Broadcast(VersusOpcode.MatchStart, packet, peerArray);
        _match.StartMatch(peerArray, isHost: true);
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
                _match.StartMatch(start.Peers, isHost: false);
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
                WinnerReceived?.Invoke(VersusSerializer.DeserializePeer(packet.Payload).PeerId);
                break;
            case VersusOpcode.Refund:
                RequireHost(packet.SenderId, opcode);
                _match.OnRefund(VersusSerializer.DeserializePeer(packet.Payload).PeerId);
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

    private void HandleQueueSendRequested(QueueSendMsg message)
    {
        if (IsHost)
            _match.OnQueueSendValidated(message);
        else
            SendTo(_hostPeerId, VersusOpcode.QueueSend, VersusSerializer.Serialize(message));
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
            SendTo(peer, opcode, payload);
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
    private bool _disposed;

    public SteamP2PTransport(Func<ulong, bool> isAllowedPeer, int channel = 0)
    {
        _isAllowedPeer = isAllowedPeer ?? throw new ArgumentNullException(nameof(isAllowedPeer));
        if (!global::Steamworks.SteamClient.IsValid)
            throw new InvalidOperationException("Steam must be initialized before creating the transport.");

        _channel = channel;
        global::Steamworks.SteamNetworking.OnP2PSessionRequest += HandleSessionRequest;
        global::Steamworks.SteamNetworking.AllowP2PPacketRelay(true);
    }

    public ulong LocalPeerId => global::Steamworks.SteamClient.SteamId;

    public bool Send(ulong peerId, byte[] payload, bool reliable)
    {
        ThrowIfDisposed();
        if (payload is null)
            throw new ArgumentNullException(nameof(payload));
        if (!_isAllowedPeer(peerId))
            return false;

        return global::Steamworks.SteamNetworking.SendP2PPacket(
            peerId,
            payload,
            payload.Length,
            _channel,
            reliable
                ? global::Steamworks.P2PSend.Reliable
                : global::Steamworks.P2PSend.Unreliable);
    }

    public bool TryReceive(out ReceivedPacket packet)
    {
        ThrowIfDisposed();
        packet = default;
        if (!global::Steamworks.SteamNetworking.IsP2PPacketAvailable(_channel))
            return false;

        var received = global::Steamworks.SteamNetworking.ReadP2PPacket(_channel);
        if (!received.HasValue || !_isAllowedPeer(received.Value.SteamId))
            return false;

        packet = new ReceivedPacket(received.Value.SteamId, received.Value.Data);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        global::Steamworks.SteamNetworking.OnP2PSessionRequest -= HandleSessionRequest;
        _disposed = true;
    }

    private void HandleSessionRequest(global::Steamworks.SteamId peer)
    {
        if (_isAllowedPeer(peer))
            global::Steamworks.SteamNetworking.AcceptP2PSessionWithUser(peer);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SteamP2PTransport));
    }
}
