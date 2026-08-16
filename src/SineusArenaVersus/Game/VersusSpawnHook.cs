using System;
using System.Linq.Expressions;
using HarmonyLib;
using UnityEngine;

namespace SineusArenaVersus.Game;

/// <summary>
/// Builds <c>Action&lt;Unit&gt;</c> spawn callbacks without a compile-time Unit/Netcode dependency.
/// </summary>
public static class VersusSpawnHook
{
    public static Delegate? CreateCallback(string label, float r, float g, float b, float a)
    {
        if (!VersusConfig.ShowInjectSenderLabels.Value && !VersusConfig.ShowInjectSenderLights.Value)
            return null;

        var unitType = AccessTools.TypeByName("Unit");
        if (unitType is null)
            return null;

        var binder = new MarkerBinder(label, new Color(r, g, b, a));
        var parameter = Expression.Parameter(unitType, "unit");
        var body = Expression.Call(
            Expression.Constant(binder),
            typeof(MarkerBinder).GetMethod(nameof(MarkerBinder.OnSpawned))!,
            Expression.Convert(parameter, typeof(object)));
        var actionType = typeof(Action<>).MakeGenericType(unitType);
        return Expression.Lambda(actionType, body, parameter).Compile();
    }

    private sealed class MarkerBinder
    {
        private readonly string _label;
        private readonly Color _color;

        public MarkerBinder(string label, Color color)
        {
            _label = label;
            _color = color;
        }

        public void OnSpawned(object unit)
        {
            if (unit is Component component)
                VersusInjectMarker.Attach(component.gameObject, _label, _color);
        }
    }
}
