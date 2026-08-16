using UnityEngine;

namespace SineusArenaVersus.Hud;

public static class VersusUiTheme
{
    public static readonly Color PanelBg = new(0.08f, 0.09f, 0.11f, 0.88f);
    public static readonly Color PanelBorder = new(0.72f, 0.55f, 0.28f, 0.95f);
    public static readonly Color Accent = new(0.90f, 0.72f, 0.35f, 1f);
    public static readonly Color Text = new(0.93f, 0.90f, 0.82f, 1f);
    public static readonly Color Muted = new(0.45f, 0.45f, 0.48f, 0.85f);
    public static readonly Color HoverFill = new(0.90f, 0.72f, 0.35f, 0.35f);
    public static readonly Color SectorFill = new(0.12f, 0.13f, 0.16f, 0.92f);

    public static void DrawFilled(Rect rect, Color color)
    {
        var prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    public static void DrawBorder(Rect rect, Color edge, float thickness = 2f)
    {
        DrawFilled(new Rect(rect.x, rect.y, rect.width, thickness), edge);
        DrawFilled(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), edge);
        DrawFilled(new Rect(rect.x, rect.y, thickness, rect.height), edge);
        DrawFilled(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), edge);
    }

    public static void DrawPanel(Rect rect, bool highlighted)
    {
        DrawFilled(rect, PanelBg);
        DrawBorder(rect, highlighted ? Accent : PanelBorder);
        if (highlighted)
            DrawFilled(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, 3f), HoverFill);
    }
}
