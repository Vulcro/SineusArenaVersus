using System;
using System.Reflection;
using HarmonyLib;

namespace SineusArenaVersus.Ui;

/// <summary>
/// Uses the game's <c>UIManager.SetCursorLock</c> — the same path as right-click —
/// so Versus never fights vanilla mouse free/capture or camera axis enable.
/// </summary>
public static class VersusGameCursor
{
    private static readonly BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static object? TryGetUiManager()
    {
        var type = AccessTools.TypeByName("UIManager");
        if (type is null)
            return null;
        return AccessTools.Property(type, "I")?.GetValue(null, null);
    }

    public static bool TryGetIsCursorLocked(out bool locked)
    {
        locked = true;
        try
        {
            var ui = TryGetUiManager();
            if (ui is null)
                return false;
            var prop = ui.GetType().GetProperty("IsCursorLocked", Instance);
            if (prop?.GetValue(ui, null) is bool value)
            {
                locked = value;
                return true;
            }
        }
        catch
        {
            // Headless tests / missing UIManager.
        }

        return false;
    }

    /// <summary>
    /// <paramref name="locked"/> true = capture mouse + enable camera look (gameplay).
    /// false = free mouse + disable Cinemachine axis (same as RMB unlock).
    /// </summary>
    public static bool TrySetCursorLock(bool locked)
    {
        try
        {
            var ui = TryGetUiManager();
            if (ui is null)
                return false;
            var method = ui.GetType().GetMethod("SetCursorLock", Instance, null, new[] { typeof(bool) }, null);
            if (method is null)
                return false;
            method.Invoke(ui, new object[] { locked });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
