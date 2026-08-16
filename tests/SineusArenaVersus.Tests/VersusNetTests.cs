using System;
using System.Collections.Generic;
using SineusArenaVersus.Catalog;
using SineusArenaVersus.Economy;
using SineusArenaVersus.Game;
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
        match.OnVpReport(Client, 15);
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
    public void Client_reports_pre_spend_vp_before_queue_request()
    {
        var economy = FundedEconomy();
        using var match = CreateMatch(Client, economy);
        var transport = new FakeTransport(Client);
        using var net = new VersusNet(match, transport, Host);
        match.StartMatch(new[] { Host, Client }, isHost: false);

        Assert.True(match.TryQueueSend(Host, "swarm"));

        Assert.Collection(
            transport.Sent,
            report =>
            {
                AssertSend(report, Host, VersusOpcode.VpReport, reliable: true);
                Assert.Equal(new VpReportMsg(Client, 15), VersusSerializer.DeserializeVpReport(report.Payload));
            },
            queue => AssertSend(queue, Host, VersusOpcode.QueueSend, reliable: true));
    }

    [Fact]
    public void Host_broadcasts_match_start_then_starts_local_match()
    {
        using var match = CreateMatch(Host);
        var transport = new FakeTransport(Host);
        using var net = new VersusNet(match, transport, Host);

        Assert.True(net.StartMatchAsHost(99, new[] { Host, Client }, 20f));

        Assert.Equal(VersusMatchState.InMatch, match.State);
        var sent = Assert.Single(transport.Sent);
        AssertSend(sent, Client, VersusOpcode.MatchStart, reliable: true);
    }

    [Fact]
    public void Host_broadcasts_match_start_even_when_solo_launch_fails()
    {
        using var match = CreateMatch(Host, soloRunLauncher: new FakeSoloRunLauncher(false));
        var transport = new FakeTransport(Host);
        using var net = new VersusNet(match, transport, Host);

        Assert.False(net.StartMatchAsHost(99, new[] { Host, Client }, 20f));

        Assert.Equal(VersusMatchState.Idle, match.State);
        var sent = Assert.Single(transport.Sent);
        AssertSend(sent, Client, VersusOpcode.MatchStart, reliable: true);
    }

    [Fact]
    public void Host_broadcasts_fallback_interval_when_configured_value_is_invalid()
    {
        using var match = CreateMatch(Host);
        var transport = new FakeTransport(Host);
        using var net = new VersusNet(match, transport, Host);

        Assert.True(net.StartMatchAsHost(99, new[] { Host, Client }, float.NaN));

        var sent = Assert.Single(transport.Sent);
        Assert.Equal(20f, VersusSerializer.DeserializeMatchStart(sent.Payload).WaveInterval);
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
        net.Pump(0f);
        transport.Sent.Clear();

        net.Pump(0.29f);
        Assert.Empty(transport.Sent);
        net.Pump(0.02f);

        var sent = Assert.Single(transport.Sent);
        AssertSend(sent, Host, VersusOpcode.RivalSnap, reliable: false);

        transport.Enqueue(Host, VersusSerializer.Serialize(new RivalSnapMsg(Host, 0.4f, true)));
        net.Pump(0f);
        Assert.Equal(0.4f, match.Peers[Host].StrongholdHp01);
    }

    [Fact]
    public void Client_waits_for_host_winner_opcode_after_stronghold_down()
    {
        using var match = CreateMatch(Client);
        var transport = new FakeTransport(Client);
        using var net = new VersusNet(match, transport, Host);
        ulong? winner = null;
        net.WinnerReceived += peer => winner = peer;
        match.StartMatch(new[] { Host, Client }, isHost: false);

        transport.Enqueue(
            Host,
            VersusSerializer.SerializePeer(VersusOpcode.StrongholdDown, new PeerMsg(Host)));
        net.Pump(0f);

        Assert.Null(winner);
        transport.Enqueue(
            Host,
            VersusSerializer.SerializePeer(VersusOpcode.Winner, new PeerMsg(Client)));
        net.Pump(0f);
        Assert.Equal(Client, winner);
    }

    [Fact]
    public void Host_relays_stronghold_down_before_broadcasting_winner()
    {
        using var match = CreateMatch(Host);
        var transport = new FakeTransport(Host);
        using var net = new VersusNet(match, transport, Host);
        match.StartMatch(new[] { Host, Client }, isHost: true);
        transport.Enqueue(
            Client,
            VersusSerializer.SerializePeer(VersusOpcode.StrongholdDown, new PeerMsg(Client)));

        net.Pump(0f);

        Assert.Collection(
            transport.Sent,
            sent => AssertSend(sent, Client, VersusOpcode.StrongholdDown, reliable: true),
            sent => AssertSend(sent, Client, VersusOpcode.Winner, reliable: true));
    }

    [Fact]
    public void Host_treats_disconnected_peer_as_stronghold_down()
    {
        using var match = CreateMatch(Host);
        var transport = new FakeTransport(Host);
        using var net = new VersusNet(match, transport, Host);
        match.StartMatch(new[] { Host, Client }, isHost: true);

        net.HandlePeerDisconnected(Client);

        Assert.False(match.Peers[Client].IsAlive);
        Assert.Equal(Host, match.WinnerPeerId);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public void Client_applies_match_start_wave_interval_and_launches_solo_run()
    {
        var launcher = new FakeSoloRunLauncher(true);
        using var match = CreateMatch(Client, soloRunLauncher: launcher);
        var transport = new FakeTransport(Client);
        using var net = new VersusNet(match, transport, Host);
        transport.Enqueue(Host, VersusSerializer.Serialize(
            new MatchStartMsg(99, 12f, new[] { Host, Client })));

        net.Pump(0f);

        Assert.Equal(1, launcher.Attempts);
        Assert.Equal(VersusMatchState.InMatch, match.State);
        Assert.Equal(12f, match.WaveIntervalSeconds);
    }

    [Fact]
    public void Failed_solo_launch_rejects_match_start()
    {
        using var match = CreateMatch(Client, soloRunLauncher: new FakeSoloRunLauncher(false));
        var transport = new FakeTransport(Client);
        using var net = new VersusNet(match, transport, Host);
        Exception? rejected = null;
        net.PacketRejected += exception => rejected = exception;
        transport.Enqueue(Host, VersusSerializer.Serialize(
            new MatchStartMsg(99, 12f, new[] { Host, Client })));

        net.Pump(0f);

        Assert.Equal(VersusMatchState.Idle, match.State);
        Assert.NotNull(rejected);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public void Client_reports_vp_and_host_debits_accepted_send()
    {
        var clientEconomy = FundedEconomy();
        using var clientMatch = CreateMatch(Client, clientEconomy);
        var clientTransport = new FakeTransport(Client);
        using var clientNet = new VersusNet(clientMatch, clientTransport, Host);
        clientMatch.StartMatch(new[] { Host, Client }, isHost: false);

        clientNet.Pump(0f);

        var report = Assert.Single(clientTransport.Sent);
        AssertSend(report, Host, VersusOpcode.VpReport, reliable: true);
        Assert.Equal(new VpReportMsg(Client, 15), VersusSerializer.DeserializeVpReport(report.Payload));

        using var hostMatch = CreateMatch(Host);
        var hostTransport = new FakeTransport(Host);
        using var hostNet = new VersusNet(hostMatch, hostTransport, Host);
        hostMatch.StartMatch(new[] { Host, Client }, isHost: true);
        hostTransport.Enqueue(Client, report.Payload);
        hostTransport.Enqueue(Client, VersusSerializer.Serialize(
            new QueueSendMsg(Client, Host, "swarm", 8)));

        hostNet.Pump(0f);

        Assert.Equal(5, hostMatch.PeerVp[Client]);
        Assert.Single(hostMatch.IncomingQueue);
    }

    [Fact]
    public void Host_refunds_rejected_remote_send()
    {
        using var match = CreateMatch(Host);
        var transport = new FakeTransport(Host);
        using var net = new VersusNet(match, transport, Host);
        match.StartMatch(new[] { Host, Client }, isHost: true);
        match.OnVpReport(Client, 0);
        transport.Enqueue(Client, VersusSerializer.Serialize(
            new QueueSendMsg(Client, Host, "swarm", 8)));

        net.Pump(0f);

        var refund = Assert.Single(transport.Sent);
        AssertSend(refund, Client, VersusOpcode.Refund, reliable: true);
        Assert.Equal(Host, VersusSerializer.DeserializePeer(refund.Payload).PeerId);
        Assert.Empty(match.IncomingQueue);
    }

    [Fact]
    public void Lowest_surviving_peer_becomes_host_when_host_disconnects()
    {
        using var match = CreateMatch(Client);
        var transport = new FakeTransport(Client);
        using var net = new VersusNet(match, transport, Host);
        match.StartMatch(new[] { Host, Client, Other }, isHost: false);

        net.HandlePeerDisconnected(Host);

        Assert.True(net.IsHost);
        Assert.True(match.IsHost);
        Assert.False(match.Peers[Host].IsAlive);
        Assert.Equal(Client, net.HostPeerId);
    }

    [Fact]
    public void Last_survivor_wins_when_host_disconnects()
    {
        using var match = CreateMatch(Client);
        var transport = new FakeTransport(Client);
        using var net = new VersusNet(match, transport, Host);
        match.StartMatch(new[] { Host, Client }, isHost: false);

        net.HandlePeerDisconnected(Host);

        Assert.Equal(Client, match.WinnerPeerId);
        Assert.Equal(VersusMatchState.Ended, match.State);
    }

    private static void AssertSend(SentPacket sent, ulong peer, VersusOpcode opcode, bool reliable)
    {
        Assert.Equal(peer, sent.PeerId);
        Assert.Equal(opcode, VersusSerializer.GetOpcode(sent.Payload));
        Assert.Equal(reliable, sent.Reliable);
    }

    private static VersusMatch CreateMatch(
        ulong localPeer,
        VersusEconomy? economy = null,
        ISoloRunLauncher? soloRunLauncher = null) =>
        new(
            localPeer,
            economy ?? new VersusEconomy(2, 1),
            VersusCatalog.LoadFromEmbeddedDefault(),
            passiveInterval: () => 10f,
            waveInterval: () => 20f,
            soloRunLauncher: soloRunLauncher ?? new FakeSoloRunLauncher(true));

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

    private sealed class FakeSoloRunLauncher : ISoloRunLauncher
    {
        private readonly bool _result;

        public FakeSoloRunLauncher(bool result) => _result = result;

        public int Attempts { get; private set; }

        public bool IsSoloRunActive() => false;

        public bool TryStartSoloRun()
        {
            Attempts++;
            return _result;
        }
    }
}
