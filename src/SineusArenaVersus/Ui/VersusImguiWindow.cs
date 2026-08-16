using UnityEngine;

namespace SineusArenaVersus.Ui;

/// <summary>
/// Shared IMGUI window chrome: opaque panel + full-body drag (snk ObjectTracker / QuestTracker style).
/// </summary>
internal static class VersusImguiWindow
{
    private static Texture2D? _windowBg;
    private static Texture2D? _titleBg;
    private static GUIStyle? _windowStyle;

    public static Rect Draw(
        int id,
        Rect rect,
        GUI.WindowFunction body,
        string title)
    {
        EnsureStyles();
        rect = GUI.Window(id, rect, windowId =>
        {
            body(windowId);
            // Full client area is a drag handle; controls still receive clicks first.
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 10000f));
        }, title, _windowStyle!);

        return ClampToScreen(rect);
    }

    private static void EnsureStyles()
    {
        if (_windowStyle is not null)
            return;

        _windowBg = MakeTex(new Color(0.10f, 0.10f, 0.12f, 0.97f));
        _titleBg = MakeTex(new Color(0.16f, 0.16f, 0.18f, 1f));

        _windowStyle = new GUIStyle(GUI.skin.window)
        {
            padding = new RectOffset(10, 10, 24, 10),
        };
        _windowStyle.normal.background = _windowBg;
        _windowStyle.onNormal.background = _windowBg;
        _windowStyle.normal.textColor = Color.white;
        _windowStyle.onNormal.textColor = Color.white;
        _windowStyle.border = new RectOffset(6, 6, 22, 6);

        // Title bar tint via focused/active states when available.
        _windowStyle.active.background = _titleBg;
        _windowStyle.onActive.background = _titleBg;
        _windowStyle.focused.background = _windowBg;
        _windowStyle.onFocused.background = _windowBg;
    }

    private static Texture2D MakeTex(Color color)
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point,
        };
        tex.SetPixel(0, 0, color);
        tex.Apply(false, true);
        return tex;
    }

    private static Rect ClampToScreen(Rect rect)
    {
        const float margin = 4f;
        var maxX = Mathf.Max(margin, Screen.width - rect.width - margin);
        var maxY = Mathf.Max(margin, Screen.height - rect.height - margin);
        rect.x = Mathf.Clamp(rect.x, margin, maxX);
        rect.y = Mathf.Clamp(rect.y, margin, maxY);
        return rect;
    }
}
