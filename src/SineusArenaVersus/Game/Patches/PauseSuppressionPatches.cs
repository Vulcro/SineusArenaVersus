using HarmonyLib;

namespace SineusArenaVersus.Game.Patches;

/// <summary>
/// Blocks singleplayer pause while Versus is active so menus never freeze the local sim.
/// </summary>
[HarmonyPatch]
internal static class ShouldSingleplayerPausePatch
{
    static System.Reflection.MethodBase? TargetMethod() =>
        AccessTools.Method(AccessTools.TypeByName("UIManager"), "ShouldSingleplayerPause");

    static void Postfix(ref bool __result)
    {
        if (VersusPauseGate.ShouldSuppressPause())
            __result = false;
    }
}

[HarmonyPatch]
internal static class RequestSingleplayerPausePatch
{
    static System.Reflection.MethodBase? TargetMethod() =>
        AccessTools.Method(AccessTools.TypeByName("UIManager"), "RequestSingleplayerPause");

    static bool Prefix() => !VersusPauseGate.ShouldSuppressPause();
}

[HarmonyPatch]
internal static class GameManagerPausePatch
{
    static System.Reflection.MethodBase? TargetMethod() =>
        AccessTools.Method(AccessTools.TypeByName("GameManager"), "Pause");

    static bool Prefix()
    {
        if (!VersusPauseGate.ShouldSuppressPause())
            return true;

        VersusPauseGate.SetTimeScale(1f);
        return false;
    }
}
