using System;
using SineusArenaVersus.Catalog;
using SineusArenaVersus.Economy;
using SineusArenaVersus.Game;
using SineusArenaVersus.Hud;
using SineusArenaVersus.Match;
using SineusArenaVersus.Net;
using SineusArenaVersus.Ui;
using Xunit;

namespace SineusArenaVersus.Tests;

[Collection(nameof(VersusLookGateCollection))]
public sealed class SendRadialMenuTests : IDisposable
{
    private const ulong LocalPeer = 10;
    private const ulong RivalPeer = 20;
    private const ulong OtherPeer = 30;
    private const float Deadzone = 0.35f;

    public SendRadialMenuTests()
    {
        VersusCameraLookGate.ResetForTests();
    }

    public void Dispose()
    {
        VersusCameraLookGate.ResetForTests();
    }

    [Fact]
    public void Toggle_opens_when_shop_living_and_catalog_ready()
    {
        using var match = CreateMatch();
        var radial = CreateRadial();
        var target = 0;
        var living = new ulong[] { RivalPeer };

        radial.Tick(ToggleFrame(), match, ref target, living);

        Assert.True(radial.IsOpen);
        Assert.True(VersusCameraLookGate.RadialOpen);
    }

    [Fact]
    public void Toggle_ignored_when_vanilla_ui_blocks_and_closed()
    {
        using var match = CreateMatch();
        var radial = CreateRadial();
        var target = 0;
        var living = new ulong[] { RivalPeer };

        radial.Tick(ToggleFrame(vanillaBlocks: true), match, ref target, living);

        Assert.False(radial.IsOpen);
        Assert.False(VersusCameraLookGate.RadialOpen);
    }

    [Fact]
    public void Toggle_closes_and_clears_look_gate()
    {
        using var match = CreateMatch();
        var radial = CreateRadial();
        var target = 0;
        var living = new ulong[] { RivalPeer };
        radial.Tick(ToggleFrame(), match, ref target, living);

        radial.Tick(ToggleFrame(), match, ref target, living);

        Assert.False(radial.IsOpen);
        Assert.False(VersusCameraLookGate.RadialOpen);
    }

    [Fact]
    public void Cancel_closes_radial()
    {
        using var match = CreateMatch();
        var radial = CreateRadial();
        var target = 0;
        var living = new ulong[] { RivalPeer };
        radial.Tick(ToggleFrame(), match, ref target, living);

        radial.Tick(new VersusInputFrame { CancelEdge = true }, match, ref target, living);

        Assert.False(radial.IsOpen);
    }

    [Fact]
    public void Confirm_queues_send_through_match_and_stays_open()
    {
        using var match = CreateMatch();
        QueueSendMsg? queued = null;
        match.QueueSendRequested += msg => queued = msg;
        var radial = CreateRadial();
        var target = 0;
        var living = new ulong[] { RivalPeer };
        radial.Tick(ToggleFrame(), match, ref target, living);
        AimFirstSector(radial, match, ref target, living);

        radial.Tick(
            new VersusInputFrame
            {
                ConfirmEdge = true,
                RightStickX = 1f,
                RightStickY = 0f,
                RightStickMagnitude = 1f
            },
            match,
            ref target,
            living);

        Assert.True(radial.IsOpen);
        Assert.True(queued.HasValue);
        Assert.Equal(RivalPeer, queued.Value.To);
        Assert.Equal("swarm", queued.Value.CatalogId);
        Assert.Equal(5, match.Economy.Vp);
    }

    [Fact]
    public void Confirm_noop_when_unaffordable()
    {
        var economy = new VersusEconomy(2, 1);
        using var match = CreateMatch(economy);
        QueueSendMsg? queued = null;
        match.QueueSendRequested += msg => queued = msg;
        var radial = CreateRadial();
        var target = 0;
        var living = new ulong[] { RivalPeer };
        radial.Tick(ToggleFrame(), match, ref target, living);
        AimFirstSector(radial, match, ref target, living);

        radial.Tick(new VersusInputFrame { ConfirmEdge = true }, match, ref target, living);

        Assert.False(queued.HasValue);
        Assert.Equal(0, match.Economy.Vp);
        Assert.True(radial.IsOpen);
    }

    [Fact]
    public void Cycle_target_wraps_from_first_to_last()
    {
        using var match = CreateMatch();
        var radial = CreateRadial();
        var target = 0;
        var living = new ulong[] { RivalPeer, OtherPeer };
        radial.Tick(ToggleFrame(), match, ref target, living);

        radial.Tick(new VersusInputFrame { CycleTargetDelta = -1 }, match, ref target, living);

        Assert.Equal(1, target);
    }

    [Fact]
    public void Stick_outside_deadzone_sets_highlight_from_stick_angle()
    {
        using var match = CreateMatch();
        var radial = CreateRadial();
        var target = 0;
        var living = new ulong[] { RivalPeer };
        radial.Tick(ToggleFrame(), match, ref target, living);

        AimStick(radial, match, ref target, living, x: 0f, y: 1f);

        Assert.Equal(1, radial.HighlightIndex);
    }

    [Fact]
    public void Mouse_sets_highlight_when_stick_inside_deadzone()
    {
        using var match = CreateMatch();
        var radial = CreateRadial();
        var target = 0;
        var living = new ulong[] { RivalPeer };
        radial.Tick(ToggleFrame(), match, ref target, living);

        radial.Tick(new VersusInputFrame { RightStickMagnitude = 0.1f }, match, ref target, living);

        Assert.Equal(2, radial.HighlightIndex);
    }

    [Fact]
    public void Eliminated_match_auto_closes()
    {
        using var match = CreateMatch();
        var radial = CreateRadial();
        var target = 0;
        var living = new ulong[] { RivalPeer, OtherPeer };
        radial.Tick(ToggleFrame(), match, ref target, living);
        match.OnStrongholdDown(LocalPeer);

        radial.Tick(default, match, ref target, living);

        Assert.False(radial.IsOpen);
        Assert.False(VersusCameraLookGate.RadialOpen);
    }

    [Fact]
    public void Empty_living_targets_prevent_open()
    {
        using var match = CreateMatch();
        var radial = CreateRadial();
        var target = 0;

        radial.Tick(ToggleFrame(), match, ref target, Array.Empty<ulong>());

        Assert.False(radial.IsOpen);
    }

    private static SendRadialMenu CreateRadial() =>
        new(() => (800f, 600f), () => Deadzone);

    private static void AimFirstSector(
        SendRadialMenu radial,
        VersusMatch match,
        ref int target,
        ulong[] living) =>
        AimStick(radial, match, ref target, living, x: 1f, y: 0f);

    private static void AimStick(
        SendRadialMenu radial,
        VersusMatch match,
        ref int target,
        ulong[] living,
        float x,
        float y) =>
        radial.Tick(
            new VersusInputFrame
            {
                RightStickX = x,
                RightStickY = y,
                RightStickMagnitude = 1f
            },
            match,
            ref target,
            living);

    private static VersusInputFrame ToggleFrame(bool vanillaBlocks = false) =>
        new()
        {
            ToggleRadialEdge = true,
            VanillaUiBlocksVersus = vanillaBlocks
        };

    private static VersusMatch CreateMatch(VersusEconomy? economy = null)
    {
        var funded = economy ?? FundedEconomy();
        var match = new VersusMatch(
            LocalPeer,
            funded,
            VersusCatalog.LoadFromEmbeddedDefault(),
            injectPack: (_, _, _) => true,
            passiveInterval: () => 10f,
            waveInterval: () => 20f,
            soloRunLauncher: new FakeSoloRunLauncher());
        match.StartMatch(new[] { LocalPeer, RivalPeer, OtherPeer }, isHost: false);
        return match;
    }

    private static VersusEconomy FundedEconomy()
    {
        var economy = new VersusEconomy(2, 1);
        economy.AddKillVp(KillTier.Boss);
        return economy;
    }

    private sealed class FakeSoloRunLauncher : ISoloRunLauncher
    {
        public bool IsSoloRunActive() => false;

        public bool TryStartSoloRun() => true;
    }
}
