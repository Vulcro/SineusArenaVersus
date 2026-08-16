using System;
using UnityEngine;

namespace SineusArenaVersus.Game;

/// <summary>
/// Bridges BaseLairSpawner spawn callbacks to <see cref="VersusInjectMarker"/>.
/// </summary>
public static class VersusSpawnHook
{
    public static Action<Unit>? CreateCallback(string label, Color color)
    {
        if (!VersusConfig.ShowInjectSenderLabels.Value && !VersusConfig.ShowInjectSenderLights.Value)
            return null;

        return unit =>
        {
            if (unit is null)
                return;
            VersusInjectMarker.Attach(((Component)unit).gameObject, label, color);
        };
    }
}
