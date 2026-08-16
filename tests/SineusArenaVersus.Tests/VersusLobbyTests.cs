using SineusArenaVersus.Lobby;
using System.Threading.Tasks;
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
        Assert.Equal(expected, VersusLobbyInviteFilter.IsVersusLobby(value));
    }

    [Fact]
    public async Task Invite_refreshes_metadata_before_filtering()
    {
        var refreshed = false;

        var shouldJoin = await VersusLobbyInviteFilter.RefreshAndCheckVersusAsync(
            () =>
            {
                refreshed = true;
                return Task.FromResult(true);
            },
            () =>
            {
                Assert.True(refreshed);
                return "1";
            });

        Assert.True(shouldJoin);
    }

    [Fact]
    public async Task Invite_is_rejected_when_metadata_refresh_fails()
    {
        var metadataRead = false;

        var shouldJoin = await VersusLobbyInviteFilter.RefreshAndCheckVersusAsync(
            () => Task.FromResult(false),
            () =>
            {
                metadataRead = true;
                return "1";
            });

        Assert.False(shouldJoin);
        Assert.False(metadataRead);
    }
}
