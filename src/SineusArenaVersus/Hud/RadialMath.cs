using System;

namespace SineusArenaVersus.Hud;

public static class RadialMath
{
    public static float AngleFromVector(float x, float y) =>
        (float)Math.Atan2(y, x);

    public static int SectorIndex(float angleRadians, int sectorCount)
    {
        if (sectorCount <= 0)
            return -1;
        if (sectorCount == 1)
            return 0;

        var tau = (float)Math.PI * 2f;
        var pi = (float)Math.PI;
        var normalized = angleRadians % tau;
        if (normalized > pi)
            normalized -= tau;
        if (normalized <= -pi)
            normalized += tau;

        var wedge = tau / sectorCount;
        var shifted = normalized + wedge / 2f;
        if (shifted < 0f)
            shifted += tau;
        if (shifted >= tau)
            shifted -= tau;

        var index = (int)(shifted / wedge);
        if (index >= sectorCount)
            index = sectorCount - 1;
        return index;
    }

    public static int KeepOrUpdateSector(int previous, float stickMagnitude, float deadzone, int candidate)
    {
        if (stickMagnitude < deadzone)
            return previous;
        return candidate;
    }
}
