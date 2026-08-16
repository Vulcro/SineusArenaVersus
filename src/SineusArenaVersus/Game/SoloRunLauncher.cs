using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SineusArenaVersus.Game;

public interface ISoloRunLauncher
{
    bool IsSoloRunActive();
    bool TryStartSoloRun();
}

/// <summary>
/// Boots an isolated local solo NGO session.
/// Versus Friends share one Steam lobby, which QuickStart treats as co-op NGO —
/// we must leave that lobby and drop remote clients before loading the arena.
/// </summary>
public sealed class ReflectionSoloRunLauncher : ISoloRunLauncher
{
    private readonly Action<string> _logError;
    private readonly Action? _detachVersusLobby;

    public ReflectionSoloRunLauncher(
        Action<string>? logError = null,
        Action? detachVersusLobby = null)
    {
        _logError = logError ?? (message => Debug.LogError(message));
        _detachVersusLobby = detachVersusLobby;
    }

    public bool TryStartSoloRun()
    {
        if (IsSoloRunActive())
            return true;

        try
        {
            // 1) Leave shared Steam lobby so QuickStart cannot re-attach the friend as NGO client.
            _detachVersusLobby?.Invoke();
            InvokeInstance("UILobbySteamController", "LeaveSteamLobby");
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
                _logError("[SineusArenaVersus] QuickStartLobby.StartAsHost unavailable.");
                return false;
            }

            startHost.Invoke(quickStart, null);
            DisconnectRemoteClients();

            var mapId = ResolveSelectedMapId();
            if (string.IsNullOrWhiteSpace(mapId))
            {
                _logError("[SineusArenaVersus] No map selected. Pick a map in the lobby UI, then Start Versus.");
                return false;
            }

            var clientCount = GetConnectedClientCount();
            if (clientCount != 1)
            {
                _logError(
                    $"[SineusArenaVersus] Solo boot aborted: expected 1 NGO client, found {clientCount}. " +
                    "Friend must not stay connected to your lobby session — retry Start Versus.");
                return false;
            }

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
                _logError("[SineusArenaVersus] ProjectSceneManager.PrepareFirstMissionAfterSync unavailable.");
                return false;
            }

            prepare.Invoke(sceneManager, new object[] { mapId!, 1 });
            return true;
        }
        catch (Exception exception)
        {
            _logError($"[SineusArenaVersus] Solo run launch failed: {exception}");
            return false;
        }
    }

    public bool IsSoloRunActive() => GameFacades.IsSoloRunActive();

    private static void DisconnectRemoteClients()
    {
        var nmType = AccessTools.TypeByName("Unity.Netcode.NetworkManager");
        var nm = nmType is null
            ? null
            : AccessTools.Property(nmType, "Singleton")?.GetValue(null, null);
        if (nm is null)
            return;

        var localId = Convert.ToUInt64(
            AccessTools.Property(nmType, "LocalClientId")?.GetValue(nm, null) ?? 0UL);
        var idsObj = AccessTools.Property(nmType, "ConnectedClientsIds")?.GetValue(nm, null);
        if (idsObj is not IEnumerable ids)
            return;

        var disconnect = AccessTools.Method(nmType, "DisconnectClient", new[] { typeof(ulong) })
                         ?? AccessTools.Method(nmType, "DisconnectClient", new[] { typeof(ulong), typeof(string) });
        if (disconnect is null)
            return;

        var remoteIds = new List<ulong>();
        foreach (var id in ids)
        {
            var clientId = Convert.ToUInt64(id);
            if (clientId != localId)
                remoteIds.Add(clientId);
        }

        foreach (var clientId in remoteIds)
        {
            try
            {
                if (disconnect.GetParameters().Length == 1)
                    disconnect.Invoke(nm, new object[] { clientId });
                else
                    disconnect.Invoke(nm, new object[] { clientId, "Versus solo isolation" });
            }
            catch
            {
                // Best-effort; PrepareFirstMission will re-check count.
            }
        }
    }

    private static int GetConnectedClientCount()
    {
        var nmType = AccessTools.TypeByName("Unity.Netcode.NetworkManager");
        var nm = nmType is null
            ? null
            : AccessTools.Property(nmType, "Singleton")?.GetValue(null, null);
        if (nm is null)
            return 0;

        var list = AccessTools.Property(nmType, "ConnectedClientsList")?.GetValue(nm, null);
        if (list is ICollection collection)
            return collection.Count;
        return 0;
    }

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
