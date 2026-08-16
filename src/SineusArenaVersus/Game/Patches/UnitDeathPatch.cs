using System;
using System.Reflection;
using HarmonyLib;

namespace SineusArenaVersus.Game.Patches;

[HarmonyPatch]
internal static class UnitDeathPatch
{
    private static MethodBase TargetMethod()
    {
        var unitType = AccessTools.TypeByName("Unit")
            ?? throw new TypeLoadException("Game type 'Unit' was not found.");
        var damageableType = AccessTools.TypeByName("IDamageable")
            ?? throw new TypeLoadException("Game type 'IDamageable' was not found.");

        return AccessTools.Method(unitType, "OnDied", new[] { damageableType })
            ?? throw new MissingMethodException(unitType.FullName, "OnDied(IDamageable)");
    }

    private static void Postfix(object __instance)
    {
        GameFacades.HandleUnitDied(__instance);
    }
}
