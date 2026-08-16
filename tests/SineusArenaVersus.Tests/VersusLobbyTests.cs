using SineusArenaVersus.Lobby;
using Xunit;

namespace SineusArenaVersus.Tests;

public sealed class VersusLobbyTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Invite_filter_requires_versus_metadata(string? value, bool expected)
    {
        Assert.Equal(expected, VersusLobby.IsVersusLobby(value));
    }
}
