using SineusArenaVersus.Catalog;
using SineusArenaVersus.Economy;
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
        match.QueueSendRequested += match.OnQueueSendValidated;
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
    public void Refund_restores_all_local_purchases_for_dead_target()
    {
        var economy = FundedEconomy();
        using var match = CreateMatch(economy);
        match.QueueSendRequested += match.OnQueueSendValidated;
        match.StartMatch(new[] { LocalPeer, RivalPeer, OtherPeer }, isHost: true);
        Assert.True(match.TryQueueSend(RivalPeer, "swarm"));

        match.OnStrongholdDown(RivalPeer);
        match.OnRefund(RivalPeer);

        Assert.Equal(15, economy.Vp);
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
        bool redirectTargetsToLocal = false) =>
        new(
            LocalPeer,
            economy,
            VersusCatalog.LoadFromEmbeddedDefault(),
            inject,
            passiveInterval: () => 10f,
            waveInterval: waveInterval ?? (() => 20f),
            redirectTargetsToLocal: redirectTargetsToLocal);
}
