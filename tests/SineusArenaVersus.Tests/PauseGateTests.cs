using SineusArenaVersus.Game;
using Xunit;

namespace SineusArenaVersus.Tests;

public class PauseGateTests
{
    [Fact]
    public void Suppresses_pause_only_while_versus_facades_active()
    {
        var previous = GameFacades.IsActive;
        try
        {
            GameFacades.IsActive = false;
            Assert.False(VersusPauseGate.ShouldSuppressPause());

            GameFacades.IsActive = true;
            Assert.True(VersusPauseGate.ShouldSuppressPause());
        }
        finally
        {
            GameFacades.IsActive = previous;
        }
    }
}
