using System;
using System.Reflection;
using HarmonyLib;

namespace SineusArenaVersus.Game.Patches;

[HarmonyPatch]
internal static class KeepDestroyedPatch
{
    private static MethodBase TargetMethod()
    {
        var damageableType = AccessTools.TypeByName("BuildingDamageable")
            ?? throw new TypeLoadException("Game type 'BuildingDamageable' was not found.");

        return AccessTools.Method(damageableType, "HandleDeathStateChanged", new[] { typeof(bool) })
            ?? throw new MissingMethodException(damageableType.FullName, "HandleDeathStateChanged(Boolean)");
    }

    private static void Prefix(object __instance, out bool __state)
    {
        var stateField = AccessTools.Field(__instance.GetType(), "_isDeadLocal")
            ?? throw new MissingFieldException(__instance.GetType().FullName, "_isDeadLocal");
        __state = (bool)stateField.GetValue(__instance);
    }

    private static void Postfix(object __instance, bool isDead, bool __state)
    {
        if (GameFacades.IsNewDeathTransition(__state, isDead))
            GameFacades.HandleBuildingDeathStateChanged(__instance, true);
    }
}
