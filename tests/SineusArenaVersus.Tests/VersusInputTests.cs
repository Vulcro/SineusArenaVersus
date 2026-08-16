using SineusArenaVersus.Ui;
using Xunit;

namespace SineusArenaVersus.Tests;

public sealed class VersusInputTests
{
    [Theory]
    [InlineData("Mouse0", true)]
    [InlineData("Mouse1", true)]
    [InlineData("Return", false)]
    [InlineData("JoystickButton0", false)]
    public void IsPointerKey_detects_mouse_buttons(string key, bool expected)
    {
        Assert.Equal(expected, VersusInput.IsPointerKey(key));
    }

    [Fact]
    public void ClassifyConfirm_mouse0_is_pointer_only()
    {
        VersusInput.ClassifyConfirm(
            "Mouse0",
            confirmKeyDown: true,
            enterDown: false,
            keypadEnterDown: false,
            gamepadSouthDown: false,
            out var confirmEdge,
            out var pointerConfirmEdge);

        Assert.False(confirmEdge);
        Assert.True(pointerConfirmEdge);
    }

    [Fact]
    public void ClassifyConfirm_enter_and_gamepad_are_unscoped()
    {
        VersusInput.ClassifyConfirm(
            "Mouse0",
            confirmKeyDown: false,
            enterDown: true,
            keypadEnterDown: false,
            gamepadSouthDown: true,
            out var confirmEdge,
            out var pointerConfirmEdge);

        Assert.True(confirmEdge);
        Assert.False(pointerConfirmEdge);
    }
}
