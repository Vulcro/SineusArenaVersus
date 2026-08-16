using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SineusArenaVersus.Ui;

public static class VersusCameraLookGate
{
    private static readonly string[] SuppressTypeNames =
    {
        "CameraInputSettingsApplier",
        "CinemachineInputAxisController",
        "MouseLook",
        "CameraLook",
        "RotateToMouseScript",
        "CinemachineZoomController"
    };

    private static readonly string[] ExcludeTypeNames =
    {
        "FreeLookKinematicController",
        "LookAtCameraUI",
        "CinemachineAutoOrbit"
    };

    private static bool _radialOpen;
    private static readonly BehaviourRestoreStack Suppressor = new();

    public static bool RadialOpen => _radialOpen;

    public static void SetRadialOpen(bool open)
    {
        if (_radialOpen == open)
            return;

        _radialOpen = open;
        try
        {
            if (open)
                SuppressLookBehaviours();
            else
                Suppressor.RestoreAll();
        }
        catch (Exception)
        {
            // Unity scene APIs are unavailable outside the game process.
        }
    }

    public static void Tick()
    {
        if (!_radialOpen)
            return;

        ApplyOpenFrame();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyOpenFrame()
    {
        try
        {
            VersusCursor.UnlockForUi();
            SuppressLookBehaviours();
            Suppressor.EnsureSuppressed();
        }
        catch (Exception)
        {
            // Unity player APIs are unavailable outside the game process.
        }
    }

    internal static bool ShouldSuppressTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        foreach (var excluded in ExcludeTypeNames)
        {
            if (typeName.IndexOf(excluded, StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
        }

        foreach (var candidate in SuppressTypeNames)
        {
            if (typeName.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return typeName.IndexOf("MouseLook", StringComparison.OrdinalIgnoreCase) >= 0 ||
               typeName.IndexOf("CameraLook", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static void SetRadialOpenStateForTests(bool open) => _radialOpen = open;

    internal static void ResetForTests()
    {
        _radialOpen = false;
        Suppressor.Clear();
    }

    private static void SuppressLookBehaviours()
    {
        try
        {
            foreach (var behaviour in FindLookBehaviours())
                Suppressor.Suppress(behaviour);
        }
        catch
        {
            // Unity scene APIs are unavailable in headless xUnit.
        }
    }

    private static IEnumerable<Behaviour> FindLookBehaviours()
    {
        var found = new List<Behaviour>();

        foreach (var typeName in SuppressTypeNames)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type is null || !typeof(Behaviour).IsAssignableFrom(type))
                continue;

            foreach (var instance in UnityEngine.Object.FindObjectsByType(type))
            {
                if (instance is not Behaviour behaviour || IsExcluded(behaviour))
                    continue;

                if (!ContainsBehaviour(found, behaviour))
                    found.Add(behaviour);
            }
        }

        foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>())
        {
            if (behaviour is null || IsExcluded(behaviour))
                continue;

            if (!ShouldSuppressTypeName(behaviour.GetType().Name))
                continue;

            if (!ContainsBehaviour(found, behaviour))
                found.Add(behaviour);
        }

        return found;
    }

    private static bool ContainsBehaviour(List<Behaviour> behaviours, Behaviour candidate)
    {
        foreach (var behaviour in behaviours)
        {
            if (ReferenceEquals(behaviour, candidate))
                return true;
        }

        return false;
    }

    private static bool IsExcluded(Behaviour behaviour)
    {
        var typeName = behaviour.GetType().Name;
        foreach (var excluded in ExcludeTypeNames)
        {
            if (typeName.IndexOf(excluded, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    internal sealed class BehaviourRestoreStack
    {
        private readonly List<Entry> _entries = new();

        public void Suppress(Behaviour behaviour)
        {
            if (behaviour is null)
                return;

            foreach (var entry in _entries)
            {
                if (ReferenceEquals(entry.Behaviour, behaviour))
                    return;
            }

            _entries.Add(new Entry(behaviour, behaviour.enabled));
            behaviour.enabled = false;
        }

        public void EnsureSuppressed()
        {
            foreach (var entry in _entries)
            {
                if (entry.Behaviour is not null && entry.Behaviour.enabled)
                    entry.Behaviour.enabled = false;
            }
        }

        public void RestoreAll()
        {
            try
            {
                foreach (var entry in _entries)
                {
                    if (entry.Behaviour is not null)
                        entry.Behaviour.enabled = entry.WasEnabled;
                }
            }
            catch
            {
                // Unity objects may be unavailable outside the game process.
            }
            finally
            {
                _entries.Clear();
            }
        }

        public void Clear() => _entries.Clear();

        private sealed class Entry
        {
            public Entry(Behaviour behaviour, bool wasEnabled)
            {
                Behaviour = behaviour;
                WasEnabled = wasEnabled;
            }

            public Behaviour Behaviour { get; }
            public bool WasEnabled { get; }
        }
    }
}
