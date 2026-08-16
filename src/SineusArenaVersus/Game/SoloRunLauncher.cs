using System;
using HarmonyLib;
using UnityEngine;

namespace SineusArenaVersus.Game;

public interface ISoloRunLauncher
{
    bool IsSoloRunActive();
    bool TryStartSoloRun();
}

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
            var controllerType = AccessTools.TypeByName("UILobbySteamController");
            var controller = controllerType is null
                ? null
                : AccessTools.Property(controllerType, "I")?.GetValue(null, null);
            var start = controllerType is null
                ? null
                : AccessTools.Method(controllerType, "StartGame", Type.EmptyTypes);
            if (controller is null || start is null)
            {
                _logError("[SineusArenaVersus] Solo start hook unavailable. Start a vanilla Solo run, then retry Versus Start. See tools/dump_game_hooks.md.");
                return false;
            }

            start.Invoke(controller, null);
            return true;
        }
        catch (Exception exception)
        {
            _logError($"[SineusArenaVersus] Solo run launch failed: {exception}. Start a vanilla Solo run, then retry Versus Start.");
            return false;
        }
    }

    public bool IsSoloRunActive() => GameFacades.IsSoloRunActive();
}
