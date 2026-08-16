using System;
using System.Collections.Generic;

namespace SineusArenaVersus.Hud;

public static class SendRadialLogic
{
    public static int CycleTarget(IReadOnlyList<ulong> living, int currentIndex, int delta)
    {
        if (living is null || living.Count == 0)
            return -1;
        var index = currentIndex;
        if (index < 0 || index >= living.Count)
            index = 0;
        var count = living.Count;
        var next = (index + delta) % count;
        if (next < 0)
            next += count;
        return next;
    }

    public static bool CanConfirm(bool shopEnabled, int vp, int cost) =>
        shopEnabled && cost >= 0 && vp >= cost;

    public static int ResolveHighlight(int count, float angleRadians, int previous, float stickMag, float deadzone)
    {
        var candidate = RadialMath.SectorIndex(angleRadians, count);
        if (candidate < 0)
            return -1;
        if (previous < 0 || previous >= count)
            previous = candidate;
        return RadialMath.KeepOrUpdateSector(previous, stickMag, deadzone, candidate);
    }
}
