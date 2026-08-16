using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SineusArenaVersus.Ui;

/// <summary>
/// While radial is open: free the cursor once. No scene FindObjects (was the FPS killer).
/// </summary>
public static class VersusCameraLookGate
{
    private static bool _radialOpen;
    private static bool _unlockedThisOpen;

    public static bool RadialOpen => _radialOpen;

    public static void SetRadialOpen(bool open)
    {
        if (_radialOpen == open)
            return;

        _radialOpen = open;
        _unlockedThisOpen = false;
        // Unlock happens in Tick only — Unity Cursor ECalls break xUnit and hitch open.
    }

    public static void Tick()
    {
        if (!_radialOpen || _unlockedThisOpen)
            return;

        TryUnlockOnce();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TryUnlockOnce()
    {
        try
        {
            VersusCursor.UnlockForUi();
            _unlockedThisOpen = true;
        }
        catch
        {
            // Unity Cursor ECalls unavailable in tests / rare player edge cases.
        }
    }

    internal static bool ShouldSuppressTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        // Kept for unit tests / future optional suppress list — not used at runtime anymore.
        return typeName.IndexOf("MouseLook", StringComparison.OrdinalIgnoreCase) >= 0 ||
               typeName.IndexOf("CameraLook", StringComparison.OrdinalIgnoreCase) >= 0 ||
               typeName.IndexOf("CameraInputSettingsApplier", StringComparison.OrdinalIgnoreCase) >= 0 ||
               typeName.IndexOf("CinemachineInputAxisController", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static bool TryRestoreBehaviour(Behaviour behaviour, bool wasEnabled)
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

    internal static void SetRadialOpenStateForTests(bool open)
    {
        _radialOpen = open;
        _unlockedThisOpen = false;
    }

    internal static void ResetForTests()
    {
        _radialOpen = false;
        _unlockedThisOpen = false;
    }
}
