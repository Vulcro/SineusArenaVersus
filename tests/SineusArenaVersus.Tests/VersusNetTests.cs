using System;
using System.Collections.Generic;
using SineusArenaVersus.Catalog;
using SineusArenaVersus.Economy;
using SineusArenaVersus.Match;
using SineusArenaVersus.Net;
using Xunit;

namespace SineusArenaVersus.Tests;

public sealed class VersusNetTests
{
    private const ulong Host = 10;
    private const ulong Client = 20;
    private const ulong Other = 30;

    [Fact]
    public void Host_routes_validated_client_send_to_every_remote_peer()
    {
        using var match = CreateMatch(Host);
        var transport = new FakeTransport(Host);
        using var net = new VersusNet(match, transport, Host);
        match.StartMatch(new[] { Host, Client, Other }, isHost: true);
        var message = new QueueSendMsg(Client, Other, "swarm", 8);
        transport.Enqueue(Client, VersusSerializer.Serialize(message));

        net.Pump(0f);

        Assert.Single(match.IncomingQueue);
        Assert.Collection(
            transport.Sent,
            sent => AssertSend(sent, Client, VersusOpcode.QueueSend, reliable: true),
            sent => AssertSend(sent, Other, VersusOpcode.QueueSend, reliable: true));
    }

    [Fact]
    public void Client_routes_local_queue_request_only_to_host()
    {
        var economy = FundedEconomy();
        using var match = CreateMatch(Client, economy);
        var transport = new FakeTransport(Client);
        using var net = new VersusNet(match, transport, Host);
        match.StartMatch(new[] { Host, Client }, isHost: false);

        Assert.True(match.TryQueueSend(Host, "swarm"));

        var sent = Assert.Single(transport.Sent);
        AssertSend(sent, Host, VersusOpcode.QueueSend, reliable: true);
    }

    [Fact]
    public void Host_start_broadcasts_before_starting_local_match()
    {
        using var match = CreateMatch(Host);
        var transport = new FakeTransport(Host)
        {
            OnSend = () => Assert.Equal(VersusMatchState.Idle, match.State)
        };
        using var net = new VersusNet(match, transport, Host);

        net.StartMatchAsHost(99, new[] { Host, Client }, 20f);

        Assert.Equal(VersusMatchState.InMatch, match.State);
        var sent = Assert.Single(transport.Sent);
        AssertSend(sent, Client, VersusOpcode.MatchStart, reliable: true);
    }

    [Fact]
    public void Rival_snap_is_unreliable_throttled_and_updates_remote_peer()
    {
        using var match = CreateMatch(Client);
        var transport = new FakeTransport(Client);
        using var net = new VersusNet(
            match,
            transport,
            Host,
            () => new RivalSnapMsg(Client, 0.75f, true),
            snapshotIntervalSeconds: 0.3f);
        match.StartMatch(new[] { Host, Client }, isHost: false);

        net.Pump(0.29f);
        Assert.Empty(transport.Sent);
        net.Pump(0.02f);

        var sent = Assert.Single(transport.Sent);
        AssertSend(sent, Host, VersusOpcode.RivalSnap, reliable: false);

        transport.Enqueue(Host, VersusSerializer.Serialize(new RivalSnapMsg(Host, 0.4f, true)));
        net.Pump(0f);
        Assert.Equal(0.4f, match.Peers[Host].StrongholdHp01);
    }

    private static void AssertSend(SentPacket sent, ulong peer, VersusOpcode opcode, bool reliable)
    {
        Assert.Equal(peer, sent.PeerId);
        Assert.Equal(opcode, VersusSerializer.GetOpcode(sent.Payload));
        Assert.Equal(reliable, sent.Reliable);
    }

    private static VersusMatch CreateMatch(ulong localPeer, VersusEconomy? economy = null) =>
        new(
            localPeer,
            economy ?? new VersusEconomy(2, 1),
            VersusCatalog.LoadFromEmbeddedDefault(),
            passiveInterval: () => 10f,
            waveInterval: () => 20f);

    private static VersusEconomy FundedEconomy()
    {
        var economy = new VersusEconomy(2, 1);
        economy.AddKillVp(KillTier.Boss);
        return economy;
    }

    private sealed class FakeTransport : IVersusTransport
    {
        private readonly Queue<ReceivedPacket> _received = new();

        public FakeTransport(ulong localPeerId)
        {
            LocalPeerId = localPeerId;
        }

        public ulong LocalPeerId { get; }
        public List<SentPacket> Sent { get; } = new();
        public Action? OnSend { get; init; }

        public void Enqueue(ulong sender, byte[] payload) =>
            _received.Enqueue(new ReceivedPacket(sender, payload));

        public bool TryReceive(out ReceivedPacket packet)
        {
            if (_received.Count == 0)
            {
                packet = default;
                return false;
            }

            packet = _received.Dequeue();
            return true;
        }

        public bool Send(ulong peerId, byte[] payload, bool reliable)
        {
            OnSend?.Invoke();
            Sent.Add(new SentPacket(peerId, payload, reliable));
            return true;
        }
    }

    private readonly record struct SentPacket(ulong PeerId, byte[] Payload, bool Reliable);
}
