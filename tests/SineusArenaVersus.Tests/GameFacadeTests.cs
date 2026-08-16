using System.Collections.Generic;
using SineusArenaVersus.Catalog;
using SineusArenaVersus.Economy;
using SineusArenaVersus.Game;
using Xunit;

namespace SineusArenaVersus.Tests;

public sealed class GameFacadeTests
{
    [Fact]
    public void Resolver_maps_enemy_key_to_catalog_spawn_id()
    {
        var resolver = EnemyKeyResolver.FromOfferings(new[]
        {
            new SendOffering { EnemyKey = "elite", SpawnId = "EliteOrc" },
        });

        Assert.True(resolver.TryResolve("ELITE", out var spawnId));
        Assert.Equal("EliteOrc", spawnId);
    }

    [Fact]
    public void Resolver_rejects_duplicate_enemy_keys()
    {
        var offerings = new List<SendOffering>
        {
            new() { EnemyKey = "trash", SpawnId = "Goblin" },
            new() { EnemyKey = "TRASH", SpawnId = "Orc" },
        };

        Assert.Throws<System.IO.InvalidDataException>(() => EnemyKeyResolver.FromOfferings(offerings));
    }

    [Theory]
    [InlineData(50f, 100f, 0.5f)]
    [InlineData(-10f, 100f, 0f)]
    [InlineData(150f, 100f, 1f)]
    [InlineData(10f, 0f, 0f)]
    public void Keep_hp_is_normalized(float current, float maximum, float expected)
    {
        Assert.Equal(expected, GameFacades.NormalizeHealth(current, maximum));
    }

    [Fact]
    public void Enemy_tier_prefers_boss_over_elite()
    {
        var unit = new FakeUnit { isBoss = true, isEliteUnit = true };

        Assert.Equal(KillTier.Boss, GameFacades.ClassifyEnemy(unit));
    }

    [Fact]
    public void Enemy_tier_uses_elite_flag()
    {
        Assert.Equal(KillTier.Elite, GameFacades.ClassifyEnemy(new FakeUnit { isEliteUnit = true }));
        Assert.Equal(KillTier.Trash, GameFacades.ClassifyEnemy(new FakeUnit()));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    public void Keep_destroyed_requires_false_to_true_transition(
        bool wasDead,
        bool isDead,
        bool expected)
    {
        Assert.Equal(expected, GameFacades.IsNewDeathTransition(wasDead, isDead));
    }

    [Fact]
    public void Inject_returns_false_when_resolver_loading_fails()
    {
        var schedulerCalled = false;

        var result = GameFacades.TryInjectPack(
            "trash",
            3,
            () => throw new System.IO.InvalidDataException("Malformed catalog"),
            (_, _) =>
            {
                schedulerCalled = true;
                return true;
            });

        Assert.False(result);
        Assert.False(schedulerCalled);
    }

    private sealed class FakeUnit
    {
        public bool isBoss { get; set; }
        public bool isEliteUnit { get; set; }
    }
}
