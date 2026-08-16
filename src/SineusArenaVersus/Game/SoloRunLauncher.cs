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
/// Boots / attaches a local solo NGO session and loads the selected arena.
/// Avoids full NetworkManager shutdown when already alone as host — that aborts
/// scene sync and leaves the "waiting for players" overlay stuck.
/// </summary>
public sealed class ReflectionSoloRunLauncher : ISoloRunLauncher
{
    private readonly Action<string> _logError;
    private readonly Action? _detachVersusLobby;
    private readonly bool _softBoot;

    public ReflectionSoloRunLauncher(
        Action<string>? logError = null,
        Action? detachVersusLobby = null,
        bool softBoot = false)
    {
        _logError = logError ?? (message => Debug.LogError(message));
        _detachVersusLobby = detachVersusLobby;
        _softBoot = softBoot;
    }

    public bool TryStartSoloRun()
    {
        if (IsSoloRunActive())
            return true;

        try
        {
            _detachVersusLobby?.Invoke();

            var mapId = ResolveSelectedMapId();
            if (string.IsNullOrWhiteSpace(mapId))
            {
                _logError("[SineusArenaVersus] No map selected. Pick a map in the lobby UI first.");
                return false;
            }

            if (_softBoot || CanSoftBoot())
                return SoftBootArena(mapId!);

            return HardBootArena(mapId!);
        }
        catch (Exception exception)
        {
            _logError($"[SineusArenaVersus] Solo run launch failed: {exception}");
            return false;
        }
    }

    public bool IsSoloRunActive() => GameFacades.IsSoloRunActive();

    private bool CanSoftBoot()
    {
        var nm = GetNetworkManager();
        if (nm is null || !ReadBool(nm, "IsListening"))
            return false;
        if (!ReadBool(nm, "IsHost") && !ReadBool(nm, "IsServer"))
            return false;
        return GetConnectedClientCount() == 1;
    }

    private bool SoftBootArena(string mapId)
    {
        // Already a single-player host in the hub: do not Shutdown — just load the mission.
        DisconnectRemoteClients();
        if (GetConnectedClientCount() != 1)
        {
            _logError(
                $"[SineusArenaVersus] Soft solo boot needs exactly 1 NGO client (have {GetConnectedClientCount()}).");
            return false;
        }

        if (!PrepareFirstMission(mapId, expectedClients: 1))
            return false;

        ScheduleReadyNudge();
        return true;
    }

    private bool HardBootArena(string mapId)
    {
        // Friends Versus path: drop shared session, then local host + mission.
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

        if (GetConnectedClientCount() != 1)
        {
            _logError(
                $"[SineusArenaVersus] Hard solo boot aborted: expected 1 NGO client, found {GetConnectedClientCount()}.");
            return false;
        }

        if (!PrepareFirstMission(mapId, expectedClients: 1))
            return false;

        ScheduleReadyNudge();
        return true;
    }

    private static void ScheduleReadyNudge()
    {
        TryNotifyLocalClientReady();
        var start = Net.VersusNet.StartCoroutine;
        start?.Invoke(ReadyNudgeCoroutine());
    }

    private static IEnumerator ReadyNudgeCoroutine()
    {
        for (var i = 0; i < 40; i++)
        {
            yield return new WaitForSecondsRealtime(0.25f);
            TryNotifyLocalClientReady();
            if (GameFacades.IsSoloRunActive())
                yield break;
        }
    }

    private static bool PrepareFirstMission(string mapId, int expectedClients)
    {
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
            return false;

        prepare.Invoke(sceneManager, new object[] { mapId, expectedClients });
        return true;
    }

    /// <summary>
    /// Marks the local client ready so GameFlowManager.TryStartGameplay can leave the wait overlay.
    /// </summary>
    private static void TryNotifyLocalClientReady()
    {
        try
        {
            var flowType = AccessTools.TypeByName("GameFlowManager");
            var flow = flowType is null
                ? null
                : AccessTools.Property(flowType, "I")?.GetValue(null, null);
            if (flow is null)
                return;

            var notify = AccessTools.Method(flowType, "TryAutoNotifyClientReady", Type.EmptyTypes)
                         ?? AccessTools.Method(flowType, "NotifyClientReadyServerRpc", Type.EmptyTypes);
            notify?.Invoke(flow, null);

            var tryStart = AccessTools.Method(flowType, "TryStartGameplay", Type.EmptyTypes);
            tryStart?.Invoke(flow, null);
        }
        catch
        {
            // Best-effort; scene handshake may still complete via vanilla callbacks.
        }
    }

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
                // Best-effort.
            }
        }
    }

    private static object? GetNetworkManager()
    {
        var nmType = AccessTools.TypeByName("Unity.Netcode.NetworkManager");
        return nmType is null
            ? null
            : AccessTools.Property(nmType, "Singleton")?.GetValue(null, null);
    }

    private static bool ReadBool(object target, string propertyName)
    {
        var value = AccessTools.Property(target.GetType(), propertyName)?.GetValue(target, null);
        return value is bool b && b;
    }

    private static int GetConnectedClientCount()
    {
        var nm = GetNetworkManager();
        if (nm is null)
            return 0;

        var list = AccessTools.Property(nm.GetType(), "ConnectedClientsList")?.GetValue(nm, null);
        return list is ICollection collection ? collection.Count : 0;
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
