using System;
using System.Reflection;

namespace SineusArenaVersus.Game;

/// <summary>
/// Versus runs as local solo sessions, so vanilla singleplayer pause would desync peers.
/// While a Versus match is active, menus may open but time must keep running (co-op style).
/// </summary>
public static class VersusPauseGate
{
    public static bool ShouldSuppressPause() => GameFacades.IsActive;

    public static void EnsureRealtimeIfNeeded()
    {
        if (!ShouldSuppressPause())
            return;

        ForceRealtimeClock();
    }

    public static void ForceRealtimeClock()
    {
        try
        {
            TryInvokeSingletonMethod("GameManager", "Unpause");
            TryInvokeSingletonMethod("UIManager", "ResetSingleplayerPause");
            SetTimeScale(1f);
        }
        catch
        {
            // Best-effort; Harmony prefixes remain the primary guard.
            // Must not throw in unit tests where game/Harmony assemblies are absent.
        }
    }

    internal static void SetTimeScale(float value)
    {
        try
        {
            var timeType = FindType("UnityEngine.Time");
            var prop = timeType?.GetProperty("timeScale", BindingFlags.Public | BindingFlags.Static);
            if (prop is null)
                return;

            var current = prop.GetValue(null, null);
            if (current is float scale && scale >= 0.99f && Math.Abs(scale - value) < 0.001f)
                return;

            prop.SetValue(null, value, null);
        }
        catch
        {
            // Unity Time may be unavailable outside the game process.
        }
    }

    private static void TryInvokeSingletonMethod(string typeName, string methodName)
    {
        var type = FindType(typeName);
        if (type is null)
            return;

        var instance = type.GetProperty("I", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
        if (instance is null)
            return;

        type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.Invoke(instance, null);
    }

    private static Type? FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(fullName, throwOnError: false);
                if (type is not null)
                    return type;
            }
            catch
            {
                // Skip assemblies that cannot be reflected in this AppDomain.
            }
        }

        return Type.GetType(fullName + ", UnityEngine.CoreModule", throwOnError: false)
               ?? Type.GetType(fullName + ", UnityEngine", throwOnError: false)
               ?? Type.GetType(fullName + ", Assembly-CSharp", throwOnError: false);
    }
}
