using SineusArenaVersus.Game;
using Xunit;

namespace SineusArenaVersus.Tests;

public sealed class VersusSenderStyleTests
{
    [Fact]
    public void Slot_index_follows_sorted_peer_order()
    {
        var order = VersusSenderStyle.OrderedPeers(new ulong[] { 30, 10, 20 });

        Assert.Equal(0, VersusSenderStyle.SlotIndex(order, 10));
        Assert.Equal(1, VersusSenderStyle.SlotIndex(order, 20));
        Assert.Equal(2, VersusSenderStyle.SlotIndex(order, 30));
        Assert.Equal("P1 Alice", VersusSenderStyle.ShortLabel(0, "Alice"));
        Assert.Equal("P2", VersusSenderStyle.ShortLabel(1, null));
    }
}
