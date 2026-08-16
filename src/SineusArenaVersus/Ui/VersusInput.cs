using System;
using UnityEngine;

namespace SineusArenaVersus.Ui;

public readonly struct VersusInputFrame
{
    public bool ToggleRadialEdge { get; init; }
    public bool ConfirmEdge { get; init; }
    public bool CancelEdge { get; init; }
    public int CycleTargetDelta { get; init; }
    public float RightStickX { get; init; }
    public float RightStickY { get; init; }
    public float RightStickMagnitude { get; init; }
    public Vector2 PointerScreen { get; init; }
    public bool VanillaUiBlocksVersus { get; init; }
}

public static class VersusInput
{
    private static bool _ltWasDown;

    public static VersusInputFrame Poll()
    {
        var toggle = WasKeyEdge(VersusConfig.OpenSendRadialKey.Value) || WasLtEdge();
        var confirm = WasKeyEdge(VersusConfig.ConfirmSendKey.Value) ||
                      Input.GetKeyDown(KeyCode.Return) ||
                      Input.GetKeyDown(KeyCode.KeypadEnter) ||
                      Input.GetKeyDown(KeyCode.JoystickButton0);
        var cancel = WasKeyEdge(VersusConfig.CancelSendKey.Value) ||
                     Input.GetKeyDown(KeyCode.JoystickButton1);
        var cycle = 0;
        if (WasKeyEdge(VersusConfig.CycleTargetPrevKey.Value) || Input.GetKeyDown(KeyCode.JoystickButton4))
            cycle = -1;
        if (WasKeyEdge(VersusConfig.CycleTargetNextKey.Value) || Input.GetKeyDown(KeyCode.JoystickButton5))
            cycle = 1;

        var sx = ReadAxis(VersusConfig.GamepadRightStickXAxis.Value);
        var sy = ReadAxis(VersusConfig.GamepadRightStickYAxis.Value);
        var mag = Mathf.Sqrt(sx * sx + sy * sy);

        return new VersusInputFrame
        {
            ToggleRadialEdge = toggle,
            ConfirmEdge = confirm,
            CancelEdge = cancel,
            CycleTargetDelta = cycle,
            RightStickX = sx,
            RightStickY = sy,
            RightStickMagnitude = mag,
            PointerScreen = Input.mousePosition,
            VanillaUiBlocksVersus = DetectVanillaUiBlock()
        };
    }

    private static bool WasKeyEdge(string keyName)
    {
        if (!Enum.TryParse(keyName, true, out KeyCode key))
            return false;
        return Input.GetKeyDown(key);
    }

    private static bool WasLtEdge()
    {
        var axis = VersusConfig.GamepadOpenAxis.Value;
        var threshold = VersusConfig.GamepadOpenAxisThreshold.Value;
        var value = ReadAxis(axis);
        var down = value >= threshold;
        var edge = down && !_ltWasDown;
        _ltWasDown = down;
        return edge;
    }

    private static float ReadAxis(string axisOrIndex)
    {
        try
        {
            if (int.TryParse(axisOrIndex, out var index))
            {
                var name = $"Joy1 Axis {index}";
                return Input.GetAxisRaw(name);
            }
            return Input.GetAxisRaw(axisOrIndex);
        }
        catch
        {
            return 0f;
        }
    }

    private static bool DetectVanillaUiBlock()
    {
        return false;
    }
}
