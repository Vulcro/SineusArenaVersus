using System;
using System.Collections.Generic;

namespace SineusArenaVersus.Ui;

/// <summary>
/// Tracks radial open state and syncs mouse/camera via the game's SetCursorLock
/// (right-click path). Does not write UnityEngine.Cursor directly.
/// </summary>
public static class VersusCameraLookGate
{
    private static bool _radialOpen;

    public static bool RadialOpen => _radialOpen;

    public static void SetRadialOpen(bool open)
    {
        if (_radialOpen == open)
            return;

        _radialOpen = open;

        if (open)
        {
            // Same as RMB unlock: free mouse + stop camera look.
            // Skip entirely when UIManager is absent (xUnit) — no Cursor ECall.
            if (VersusGameCursor.HasUiManager() &&
                !VersusGameCursor.TrySetCursorLock(false))
                VersusGameCursor.UnlockForUiFallback();
        }
        else
        {
            // Same as RMB capture: hide mouse + restore camera.
            if (VersusGameCursor.HasUiManager())
                VersusGameCursor.TrySetCursorLock(true);
        }
    }

    public static void Tick()
    {
        // Intentionally empty: do not touch Cursor every frame.
        // RMB remains fully owned by UIManager.Update → SetCursorLock.
    }

    internal static bool ShouldSuppressTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        return typeName.IndexOf("MouseLook", StringComparison.OrdinalIgnoreCase) >= 0 ||
               typeName.IndexOf("CameraLook", StringComparison.OrdinalIgnoreCase) >= 0 ||
               typeName.IndexOf("CameraInputSettingsApplier", StringComparison.OrdinalIgnoreCase) >= 0 ||
               typeName.IndexOf("CinemachineInputAxisController", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static bool TryRestoreBehaviour(UnityEngine.Behaviour behaviour, bool wasEnabled)
    {
        try
        {
            if (behaviour == null)
                return true;
            behaviour.enabled = wasEnabled;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static void RemoveRestored<T>(IList<T> entries, Func<T, bool> restored)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (!restored(entries[i]))
                continue;
            entries.RemoveAt(i);
        }
    }

    internal static void SetRadialOpenStateForTests(bool open) => _radialOpen = open;

    internal static void ResetForTests() => _radialOpen = false;
}
