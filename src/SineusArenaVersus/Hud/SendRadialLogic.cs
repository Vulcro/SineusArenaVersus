using System;
using System.Collections.Generic;
using UnityEngine;

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

    public static Vector2 ScreenToGui(Vector2 screen, float screenHeight) =>
        new(screen.x, screenHeight - screen.y);

    public static bool PointerInWheel(
        float pointerX,
        float pointerY,
        float screenWidth,
        float screenHeight,
        float radiusFraction,
        float buttonWidth,
        float buttonHeight)
    {
        if (screenWidth <= 0f || screenHeight <= 0f)
            return false;

        var radius = Math.Min(screenWidth, screenHeight) * radiusFraction;
        var extent = 0.5f * Math.Max(buttonWidth, buttonHeight);
        var maxDist = radius + extent;
        var dx = pointerX - screenWidth * 0.5f;
        var dy = pointerY - screenHeight * 0.5f;
        return dx * dx + dy * dy <= maxDist * maxDist;
    }

    public static bool HitsAny(Vector2 guiPoint, IReadOnlyList<Rect>? rects)
    {
        if (rects is null || rects.Count == 0)
            return false;

        for (var i = 0; i < rects.Count; i++)
        {
            if (rects[i].Contains(guiPoint))
                return true;
        }

        return false;
    }

    public static bool AllowsPointerConfirm(
        Vector2 pointerScreen,
        float screenWidth,
        float screenHeight,
        float radiusFraction,
        float buttonWidth,
        float buttonHeight,
        IReadOnlyList<Rect>? blockRectsGui)
    {
        var guiPoint = ScreenToGui(pointerScreen, screenHeight);
        if (HitsAny(guiPoint, blockRectsGui))
            return false;

        return PointerInWheel(
            pointerScreen.x,
            pointerScreen.y,
            screenWidth,
            screenHeight,
            radiusFraction,
            buttonWidth,
            buttonHeight);
    }
}
