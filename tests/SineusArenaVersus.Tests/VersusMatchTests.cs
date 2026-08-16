using SineusArenaVersus.Catalog;
using SineusArenaVersus.Economy;
using SineusArenaVersus.Game;
using SineusArenaVersus.Match;
using SineusArenaVersus.Net;
using Xunit;

namespace SineusArenaVersus.Tests;

public sealed class VersusMatchTests
{
    private const ulong LocalPeer = 10;
    private const ulong RivalPeer = 20;
    private const ulong OtherPeer = 30;

    [Fact]
    public void Queue_spends_vp_and_wave_injects_validated_send()
    {
        var economy = FundedEconomy();
        string? injectedKey = null;
        var injectedCount = 0;
        using var match = CreateMatch(
            economy,
            (key, count) =>
            {
                injectedKey = key;
                injectedCount = count;
                return true;
            },
            redirectTargetsToLocal: true);
        match.QueueSendRequested += send => match.OnQueueSendValidated(send);
        match.StartMatch(new[] { LocalPeer, RivalPeer }, isHost: true);

        Assert.True(match.TryQueueSend(RivalPeer, "swarm"));
        Assert.Equal(5, economy.Vp);
        Assert.Equal(8, match.IncomingPreview["swarm"]);

        match.OnWaveTick(new WaveTickMsg(1, 20f));

        Assert.Equal("trash", injectedKey);
        Assert.Equal(8, injectedCount);
        Assert.Empty(match.IncomingQueue);
        Assert.Equal(1, economy.SuccessfulSends);
    }

    [Fact]
    public void Wave_refunds_local_purchase_when_target_died_before_flush()
    {
        var economy = FundedEconomy();
        using var match = CreateMatch(economy);
        match.QueueSendRequested += send => match.OnQueueSendValidated(send);
        match.StartMatch(new[] { LocalPeer, RivalPeer, OtherPeer }, isHost: true);
        Assert.True(match.TryQueueSend(RivalPeer, "swarm"));

        match.OnStrongholdDown(RivalPeer);
        match.OnWaveTick(new WaveTickMsg(1, 20f));

        Assert.Equal(15, economy.Vp);
        Assert.Empty(match.IncomingQueue);
    }

    [Fact]
    public void Wave_settles_local_send_to_living_remote_target()
    {
        var economy = FundedEconomy();
        using var match = CreateMatch(economy, (_, _) => throw new Xunit.Sdk.XunitException("Remote send must not inject locally."));
        match.QueueSendRequested += send => match.OnQueueSendValidated(send);
        match.StartMatch(new[] { LocalPeer, RivalPeer }, isHost: true);
        Assert.True(match.TryQueueSend(RivalPeer, "swarm"));

        match.OnWaveTick(new WaveTickMsg(1, 20f));

        Assert.Equal(1, economy.SuccessfulSends);
        Assert.Empty(match.IncomingQueue);
        match.OnRefund(RivalPeer);
        Assert.Equal(5, economy.Vp);
    }

    [Fact]
    public void Wave_requests_remote_refund_when_target_died_after_acceptance()
    {
        using var match = CreateMatch(FundedEconomy());
        (ulong sender, ulong target)? refund = null;
        match.RefundRequested += (sender, target) => refund = (sender, target);
        match.StartMatch(new[] { LocalPeer, RivalPeer, OtherPeer }, isHost: true);
        match.OnVpReport(OtherPeer, 15);
        match.OnQueueSendValidated(new QueueSendMsg(OtherPeer, RivalPeer, "swarm", 8));
        match.OnStrongholdDown(RivalPeer);

        match.OnWaveTick(new WaveTickMsg(1, 20f));

        Assert.Equal((OtherPeer, RivalPeer), refund);
        Assert.Empty(match.IncomingQueue);
    }

    [Fact]
    public void Host_rejects_send_to_dead_target_and_requests_refund()
    {
        using var match = CreateMatch(FundedEconomy());
        (ulong sender, ulong target)? refund = null;
        match.RefundRequested += (sender, target) => refund = (sender, target);
        match.StartMatch(new[] { LocalPeer, RivalPeer, OtherPeer }, isHost: true);
        match.OnStrongholdDown(RivalPeer);

        match.OnQueueSendValidated(new QueueSendMsg(LocalPeer, RivalPeer, "swarm", 8));

        Assert.Equal((LocalPeer, RivalPeer), refund);
        Assert.Empty(match.IncomingQueue);
    }

    [Fact]
    public void Local_elimination_disables_shop_without_ending_multi_peer_match()
    {
        using var match = CreateMatch(FundedEconomy());
        match.StartMatch(new[] { LocalPeer, RivalPeer, OtherPeer }, isHost: false);

        match.OnStrongholdDown(LocalPeer);

        Assert.Equal(VersusMatchState.Eliminated, match.State);
        Assert.False(match.ShopEnabled);
        Assert.False(match.TryQueueSend(RivalPeer, "swarm"));
    }

    [Fact]
    public void Client_does_not_determine_winner_after_stronghold_down()
    {
        using var match = CreateMatch(FundedEconomy());
        ulong? winner = null;
        match.WinnerDetermined += peer => winner = peer;
        match.StartMatch(new[] { LocalPeer, RivalPeer }, isHost: false);

        match.OnStrongholdDown(RivalPeer);

        Assert.Null(winner);
        Assert.Equal(VersusMatchState.InMatch, match.State);
    }

    [Fact]
    public void Host_tick_emits_and_applies_wave()
    {
        using var match = CreateMatch(FundedEconomy(), waveInterval: () => 2f);
        var emittedWave = 0;
        match.WaveTickRequested += tick => emittedWave = tick.WaveIndex;
        match.StartMatch(new[] { LocalPeer, RivalPeer }, isHost: true);

        match.Tick(2f);

        Assert.Equal(1, emittedWave);
        Assert.Equal(1, match.WaveIndex);
    }

    [Fact]
    public void Eliminated_host_continues_wave_clock_without_passive_income()
    {
        var economy = FundedEconomy();
        using var match = CreateMatch(economy, waveInterval: () => 2f);
        match.StartMatch(new[] { LocalPeer, RivalPeer, OtherPeer }, isHost: true);
        match.OnStrongholdDown(LocalPeer);

        match.Tick(10f);

        Assert.Equal(VersusMatchState.Eliminated, match.State);
        Assert.Equal(5, match.WaveIndex);
        Assert.Equal(15, economy.Vp);
    }

    [Fact]
    public void Host_exposes_only_accepted_sends_for_relay()
    {
        using var match = CreateMatch(FundedEconomy());
        QueueSendMsg? relayed = null;
        match.SendAcceptedForRelay += send => relayed = send;
        match.StartMatch(new[] { LocalPeer, RivalPeer }, isHost: true);
        match.OnVpReport(RivalPeer, 15);

        match.OnQueueSendValidated(new QueueSendMsg(RivalPeer, LocalPeer, "swarm", 7));
        Assert.Null(relayed);

        var accepted = new QueueSendMsg(RivalPeer, LocalPeer, "swarm", 8);
        match.OnQueueSendValidated(accepted);

        Assert.Equal(accepted, relayed);
    }

    [Fact]
    public void Match_start_aborts_when_solo_run_cannot_start()
    {
        using var match = CreateMatch(FundedEconomy(), soloRunLauncher: new FakeSoloRunLauncher(false));

        var started = match.StartMatch(new[] { LocalPeer, RivalPeer }, isHost: true);

        Assert.False(started);
        Assert.Equal(VersusMatchState.Idle, match.State);
        Assert.False(match.IsActive);
    }

    [Fact]
    public void Match_start_uses_host_wave_interval()
    {
        using var match = CreateMatch(FundedEconomy(), waveInterval: () => 99f);

        Assert.True(match.StartMatch(new[] { LocalPeer, RivalPeer }, isHost: false, waveInterval: 12f));

        Assert.Equal(12f, match.WaveIntervalSeconds);
        match.Tick(6f);
        Assert.Equal(6f, match.WaveSecondsRemaining);
    }

    [Fact]
    public void Promoted_host_finishes_match_after_original_host_disconnects()
    {
        using var match = CreateMatch(FundedEconomy());
        match.StartMatch(new[] { LocalPeer, RivalPeer }, isHost: false);
        match.OnStrongholdDown(RivalPeer);

        match.SetHost(true);

        Assert.Equal(LocalPeer, match.WinnerPeerId);
        Assert.Equal(VersusMatchState.Ended, match.State);
    }

    [Fact]
    public void Host_rejects_remote_send_without_reported_vp()
    {
        using var match = CreateMatch(FundedEconomy());
        match.StartMatch(new[] { LocalPeer, RivalPeer }, isHost: true);

        Assert.False(match.OnQueueSendValidated(new QueueSendMsg(RivalPeer, LocalPeer, "swarm", 8)));
        Assert.Empty(match.IncomingQueue);

        match.OnVpReport(RivalPeer, 15);

        Assert.True(match.OnQueueSendValidated(new QueueSendMsg(RivalPeer, LocalPeer, "swarm", 8)));
        Assert.Equal(5, match.PeerVp[RivalPeer]);
    }

    private static VersusEconomy FundedEconomy()
    {
        var economy = new VersusEconomy(2, 1);
        economy.AddKillVp(KillTier.Boss);
        return economy;
    }

    private static VersusMatch CreateMatch(
        VersusEconomy economy,
        System.Func<string, int, bool>? inject = null,
        System.Func<float>? waveInterval = null,
        bool redirectTargetsToLocal = false,
        ISoloRunLauncher? soloRunLauncher = null) =>
        new(
            LocalPeer,
            economy,
            VersusCatalog.LoadFromEmbeddedDefault(),
            inject,
            passiveInterval: () => 10f,
            waveInterval: waveInterval ?? (() => 20f),
            redirectTargetsToLocal: redirectTargetsToLocal,
            soloRunLauncher: soloRunLauncher ?? new FakeSoloRunLauncher(true));

    private sealed class FakeSoloRunLauncher : ISoloRunLauncher
    {
        private readonly bool _result;

        public FakeSoloRunLauncher(bool result) => _result = result;

        public bool IsSoloRunActive() => false;

        public bool TryStartSoloRun() => _result;
    }
}
