using SineusArenaVersus.Catalog;
using Xunit;

namespace SineusArenaVersus.Tests;

public class CatalogTests
{
    [Fact]
    public void Default_catalog_has_four_offerings_with_positive_costs()
    {
        var cat = VersusCatalog.LoadFromEmbeddedDefault();
        Assert.True(cat.All.Count >= 4);
        Assert.All(cat.All, o => Assert.True(o.Cost > 0 && o.Count > 0 && !string.IsNullOrEmpty(o.Id)));
        Assert.True(cat.TryGet("swarm", out var swarm));
        Assert.Equal(8, swarm.Count);
        Assert.Contains("Mob_Skeleton", swarm.SpawnId);
        Assert.True(cat.TryGet("fast_pack", out var fast));
        Assert.Contains("Mob_Dark_Soul", fast.SpawnId);
    }
}
