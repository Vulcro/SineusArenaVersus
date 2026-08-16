using SineusArenaVersus.Ui;
using Xunit;

namespace SineusArenaVersus.Tests;

[CollectionDefinition(nameof(VersusLookGateCollection), DisableParallelization = true)]
public sealed class VersusLookGateCollection
{
}

[Collection(nameof(VersusLookGateCollection))]
public sealed class VersusCameraLookGateTests
{
    public VersusCameraLookGateTests()
    {
        VersusCameraLookGate.ResetForTests();
    }

    [Theory]
    [InlineData("CameraInputSettingsApplier", true)]
    [InlineData("CinemachineInputAxisController", true)]
    [InlineData("MouseLook", true)]
    [InlineData("CodeMonkey.MouseLook", true)]
    [InlineData("FreeLookKinematicController", false)]
    [InlineData("LookAtCameraUI", false)]
    [InlineData("CinemachineAutoOrbit", false)]
    [InlineData("PlayerInputModule", false)]
    public void ShouldSuppressTypeName_matches_game_look_types(string typeName, bool expected)
    {
        Assert.Equal(expected, VersusCameraLookGate.ShouldSuppressTypeName(typeName));
    }

    [Fact]
    public void SetRadialOpen_tracks_open_state()
    {
        VersusCameraLookGate.SetRadialOpenStateForTests(true);
        Assert.True(VersusCameraLookGate.RadialOpen);

        VersusCameraLookGate.SetRadialOpen(false);
        Assert.False(VersusCameraLookGate.RadialOpen);
    }

    [Fact]
    public void SetRadialOpen_is_idempotent()
    {
        VersusCameraLookGate.SetRadialOpenStateForTests(true);
        VersusCameraLookGate.SetRadialOpen(true);
        Assert.True(VersusCameraLookGate.RadialOpen);

        VersusCameraLookGate.SetRadialOpen(false);
        VersusCameraLookGate.SetRadialOpen(false);
        Assert.False(VersusCameraLookGate.RadialOpen);
    }

    [Fact]
    public void Tick_noops_when_radial_closed()
    {
        VersusCameraLookGate.SetRadialOpen(false);
        VersusCameraLookGate.Tick();
        Assert.False(VersusCameraLookGate.RadialOpen);
    }
}
