using System;
using SineusArenaVersus.Hud;
using Xunit;

namespace SineusArenaVersus.Tests;

public sealed class SendRadialLogicTests
{
    [Fact]
    public void CycleTarget_wraps_forward_and_back()
    {
        var living = new ulong[] { 10, 20, 30 };
        Assert.Equal(1, SendRadialLogic.CycleTarget(living, 0, +1));
        Assert.Equal(0, SendRadialLogic.CycleTarget(living, 0, -1));
        Assert.Equal(0, SendRadialLogic.CycleTarget(living, 2, +1));
    }

    [Fact]
    public void CycleTarget_empty_returns_minus_one()
    {
        Assert.Equal(-1, SendRadialLogic.CycleTarget(Array.Empty<ulong>(), 0, 1));
    }

    [Theory]
    [InlineData(true, 10, 10, true)]
    [InlineData(true, 9, 10, false)]
    [InlineData(false, 100, 10, false)]
    public void CanConfirm_requires_shop_and_funds(bool shop, int vp, int cost, bool expected)
    {
        Assert.Equal(expected, SendRadialLogic.CanConfirm(shop, vp, cost));
    }

    [Fact]
    public void ResolveHighlight_uses_deadzone()
    {
        var kept = SendRadialLogic.ResolveHighlight(4, 0f, previous: 2, stickMag: 0.1f, deadzone: 0.35f);
        Assert.Equal(2, kept);
        var moved = SendRadialLogic.ResolveHighlight(4, 0f, previous: 2, stickMag: 1f, deadzone: 0.35f);
        Assert.Equal(0, moved);
    }
}
