using SineusArenaVersus.Hud;
using Xunit;

namespace SineusArenaVersus.Tests;

public sealed class RadialMathTests
{
    [Theory]
    [InlineData(0f, 4, 0)]
    [InlineData(1.5707963f, 4, 1)]   // ~pi/2
    [InlineData(3.1415926f, 4, 2)]   // ~pi
    [InlineData(-0.01f, 4, 0)]
    public void SectorIndex_maps_angle_into_equal_wedges(float angle, int count, int expected)
    {
        Assert.Equal(expected, RadialMath.SectorIndex(angle, count));
    }

    [Fact]
    public void SectorIndex_with_one_sector_always_zero()
    {
        Assert.Equal(0, RadialMath.SectorIndex(2.5f, 1));
    }

    [Fact]
    public void SectorIndex_returns_minus_one_when_count_invalid()
    {
        Assert.Equal(-1, RadialMath.SectorIndex(0f, 0));
    }

    [Fact]
    public void KeepOrUpdateSector_ignores_stick_inside_deadzone()
    {
        Assert.Equal(2, RadialMath.KeepOrUpdateSector(2, 0.1f, 0.35f, 0));
    }

    [Fact]
    public void KeepOrUpdateSector_updates_outside_deadzone()
    {
        Assert.Equal(0, RadialMath.KeepOrUpdateSector(2, 0.9f, 0.35f, 0));
    }

    [Fact]
    public void AngleFromVector_up_is_positive_y()
    {
        var angle = RadialMath.AngleFromVector(0f, 1f);
        Assert.InRange(angle, 1.5f, 1.6f);
    }
}
