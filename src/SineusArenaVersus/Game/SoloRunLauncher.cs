using System;
using HarmonyLib;
using UnityEngine;

namespace SineusArenaVersus.Game;

public interface ISoloRunLauncher
{
    bool IsSoloRunActive();
    bool TryStartSoloRun();
}

/// <summary>
/// Boots a local single-player NGO session. Must NOT call
/// <c>UILobbySteamController.StartGame()</c> — that uses Steam lobby member count
/// and starts shared co-op when the Versus lobby has 2+ friends.
/// </summary>
public sealed class ReflectionSoloRunLauncher : ISoloRunLauncher
{
    private readonly Action<string> _logError;

    public ReflectionSoloRunLauncher(Action<string>? logError = null)
    {
        _logError = logError ?? (message => Debug.LogError(message));
    }

    public bool TryStartSoloRun()
    {
        if (IsSoloRunActive())
            return true;

        try
        {
            // Drop any shared NGO session (previous co-op / lobby host with guests).
            InvokeInstance("UILobbySteamController", "DisconnectNetworkCompletely");

            var quickStartType = AccessTools.TypeByName("QuickStartLobby");
            var quickStart = quickStartType is null
                ? null
                : AccessTools.Property(quickStartType, "I")?.GetValue(null, null);
            var startHost = quickStartType is null
                ? null
                : AccessTools.Method(quickStartType, "StartAsHost", Type.EmptyTypes);
            if (quickStart is null || startHost is null)
            {
                _logError("[SineusArenaVersus] QuickStartLobby.StartAsHost unavailable. Start vanilla Solo (alone), then Versus Start.");
                return false;
            }

            startHost.Invoke(quickStart, null);

            var mapId = ResolveSelectedMapId();
            if (string.IsNullOrWhiteSpace(mapId))
            {
                _logError("[SineusArenaVersus] No map selected. Pick a map in the lobby, then Versus Start.");
                return false;
            }

            // Force expectedClients=1 so peers never share one arena.
            var sceneManagerType = AccessTools.TypeByName("ProjectSceneManager");
            var sceneManager = sceneManagerType is null
                ? null
                : AccessTools.Property(sceneManagerType, "I")?.GetValue(null, null);
            var prepare = sceneManagerType is null
                ? null
                : AccessTools.Method(
                    sceneManagerType,
                    "PrepareFirstMissionAfterSync",
                    new[] { typeof(string), typeof(int) });
            if (sceneManager is null || prepare is null)
            {
                // Fallback: private deferred load with expectedPlayers = 1 (keeps Versus Steam lobby).
                var steamUiType = AccessTools.TypeByName("UILobbySteamController");
                var steamUi = steamUiType is null
                    ? null
                    : AccessTools.Property(steamUiType, "I")?.GetValue(null, null);
                var deferred = steamUiType is null
                    ? null
                    : AccessTools.Method(
                        steamUiType,
                        "StartDeferredSceneLoad",
                        new[] { typeof(int), typeof(string) });
                if (steamUi is null || deferred is null)
                {
                    _logError("[SineusArenaVersus] Solo scene load hook unavailable.");
                    return false;
                }

                deferred.Invoke(steamUi, new object[] { 1, mapId });
                return true;
            }

            prepare.Invoke(sceneManager, new object[] { mapId, 1 });
            return true;
        }
        catch (Exception exception)
        {
            _logError($"[SineusArenaVersus] Solo run launch failed: {exception}. Start vanilla Solo alone, then Versus Start.");
            return false;
        }
    }

    public bool IsSoloRunActive() => GameFacades.IsSoloRunActive();

    private static string? ResolveSelectedMapId()
    {
        var selectorType = AccessTools.TypeByName("LobbySceneSelector");
        var selector = selectorType is null
            ? null
            : AccessTools.Property(selectorType, "I")?.GetValue(null, null);
        if (selector is null)
            return null;

        var selected = AccessTools.Property(selectorType, "SelectedMapId")?.GetValue(selector, null) as string;
        return string.IsNullOrWhiteSpace(selected) ? null : selected;
    }

    private static void InvokeInstance(string typeName, string methodName)
    {
        var type = AccessTools.TypeByName(typeName);
        var instance = type is null
            ? null
            : AccessTools.Property(type, "I")?.GetValue(null, null);
        var method = type is null
            ? null
            : AccessTools.Method(type, methodName, Type.EmptyTypes);
        if (instance is null || method is null)
            return;

        method.Invoke(instance, null);
    }
}
