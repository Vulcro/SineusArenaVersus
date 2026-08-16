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

    private static void Postfix(object __instance, bool isDead)
    {
        GameFacades.HandleBuildingDeathStateChanged(__instance, isDead);
    }
}
