using SineusArenaVersus.Economy;
using Xunit;

namespace SineusArenaVersus.Tests;

public class EconomyTests
{
    [Fact]
    public void Passive_scales_with_successful_sends()
    {
        var eco = new VersusEconomy(passiveBase: 2, passivePerSend: 1);
        Assert.Equal(2, eco.PassiveAmountPerTick);
        eco.RegisterSuccessfulSend();
        Assert.Equal(3, eco.PassiveAmountPerTick);
        eco.OnPassiveTick();
        Assert.Equal(3, eco.Vp);
    }

    [Fact]
    public void TrySpend_fails_when_insufficient_and_does_not_mutate()
    {
        var eco = new VersusEconomy(passiveBase: 2, passivePerSend: 1);
        eco.AddKillVp(KillTier.Trash);
        Assert.False(eco.TrySpend(10));
        Assert.Equal(1, eco.Vp);
    }
}
