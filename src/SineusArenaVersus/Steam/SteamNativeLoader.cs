using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SineusArenaVersus.Steam;

/// <summary>
/// Facepunch.Steamworks P/Invokes steam_api64.dll. The game ships it under
/// SineusArena_Data/Plugins/x86_64 — preload/mirror so BepInEx plugins can resolve it.
/// </summary>
internal static class SteamNativeLoader
{
    private const string SteamApiFileName = "steam_api64.dll";

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string? lpPathName);

    public static string? TryEnsureLoaded(Action<string>? log = null)
    {
        try
        {
            var pluginDir = Path.GetDirectoryName(typeof(SteamNativeLoader).Assembly.Location);
            var gameSteamApi = ResolveGameSteamApiPath();
            if (gameSteamApi is null)
            {
                log?.Invoke("steam_api64.dll not found under SineusArena_Data/Plugins/x86_64.");
                return null;
            }

            var pluginSteamApi = pluginDir is null
                ? null
                : Path.Combine(pluginDir, SteamApiFileName);

            if (pluginSteamApi is not null && !File.Exists(pluginSteamApi))
            {
                File.Copy(gameSteamApi, pluginSteamApi, overwrite: false);
                log?.Invoke($"Mirrored {SteamApiFileName} into plugin folder.");
            }

            var loadPath = pluginSteamApi is not null && File.Exists(pluginSteamApi)
                ? pluginSteamApi
                : gameSteamApi;

            var directory = Path.GetDirectoryName(loadPath);
            if (!string.IsNullOrEmpty(directory))
                SetDllDirectory(directory);

            var handle = LoadLibrary(loadPath);
            if (handle == IntPtr.Zero)
            {
                log?.Invoke($"LoadLibrary failed for {loadPath} (win32={Marshal.GetLastWin32Error()}).");
                SetDllDirectory(null);
                return null;
            }

            SetDllDirectory(null);
            log?.Invoke($"Loaded Steam native API from {loadPath}");
            return loadPath;
        }
        catch (Exception exception)
        {
            log?.Invoke($"Steam native preload failed: {exception.Message}");
            return null;
        }
    }

    private static string? ResolveGameSteamApiPath()
    {
        // Avoid compile-time UnityEngine.Application dependency for test hosts.
        var dataPath = TryReadUnityDataPath();
        if (string.IsNullOrEmpty(dataPath))
            return null;

        var candidate = Path.GetFullPath(
            Path.Combine(dataPath, "Plugins", "x86_64", SteamApiFileName));
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? TryReadUnityDataPath()
    {
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var appType = assembly.GetType("UnityEngine.Application", throwOnError: false);
                var prop = appType?.GetProperty("dataPath", BindingFlags.Public | BindingFlags.Static);
                if (prop?.GetValue(null, null) is string path && !string.IsNullOrEmpty(path))
                    return path;
            }
        }
        catch
        {
            // Unit tests / early boot may lack Unity.
        }

        return null;
    }
}
