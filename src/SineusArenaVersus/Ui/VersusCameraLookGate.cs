using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SineusArenaVersus.Ui;

/// <summary>
/// Freezes camera look while the send radial is open without hitching the open frame.
/// Look-type discovery is deferred across frames (one type per tick).
/// </summary>
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

    private static readonly Type?[] CachedTypes = new Type?[SuppressTypeNames.Length];
    private static bool _typesResolved;

    private static bool _radialOpen;
    private static int _suppressTypeIndex = -1;
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
            {
                // Defer UnlockForUi + FindObjects to Tick — avoids open-frame hitch and
                // keeps xUnit free of Unity Cursor ECalls during SetRadialOpen.
                _suppressTypeIndex = 0;
            }
            else
            {
                _suppressTypeIndex = -1;
                Suppressor.RestoreAll();
            }
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
            ProgressDeferredSuppress();
            Suppressor.EnsureSuppressed();
        }
        catch (Exception)
        {
            // Unity player APIs are unavailable outside the game process.
        }
    }

    private static void ProgressDeferredSuppress()
    {
        if (_suppressTypeIndex < 0 || _suppressTypeIndex >= SuppressTypeNames.Length)
            return;

        EnsureTypesResolved();
        var type = CachedTypes[_suppressTypeIndex];
        _suppressTypeIndex++;
        if (type is null || !typeof(Behaviour).IsAssignableFrom(type))
            return;

        foreach (var instance in UnityEngine.Object.FindObjectsByType(type))
        {
            if (instance is not Behaviour behaviour || IsExcluded(behaviour))
                continue;
            Suppressor.Suppress(behaviour);
        }
    }

    private static void EnsureTypesResolved()
    {
        if (_typesResolved)
            return;

        for (var i = 0; i < SuppressTypeNames.Length; i++)
        {
            try
            {
                CachedTypes[i] = AccessTools.TypeByName(SuppressTypeNames[i]);
            }
            catch
            {
                CachedTypes[i] = null;
            }
        }

        _typesResolved = true;
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

    internal static void SetRadialOpenStateForTests(bool open) => _radialOpen = open;

    internal static void ResetForTests()
    {
        _radialOpen = false;
        _suppressTypeIndex = -1;
        Suppressor.Clear();
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
            if (behaviour == null)
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
                try
                {
                    if (entry.Behaviour == null)
                        continue;
                    if (entry.Behaviour.enabled)
                        entry.Behaviour.enabled = false;
                }
                catch
                {
                    // Unity objects may be unavailable outside the game process.
                }
            }
        }

        public void RestoreAll()
        {
            RemoveRestored(_entries, entry => TryRestoreBehaviour(entry.Behaviour, entry.WasEnabled));
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
