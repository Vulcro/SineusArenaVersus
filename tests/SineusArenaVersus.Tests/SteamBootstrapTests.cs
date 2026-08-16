using SineusArenaVersus.Steam;
using Xunit;

namespace SineusArenaVersus.Tests;

public sealed class SteamBootstrapTests
{
    [Fact]
    public void Dispose_does_not_throw_when_steam_was_never_available()
    {
        using var bootstrap = new SteamBootstrap();
        Assert.False(bootstrap.IsAvailable);
    }
}
