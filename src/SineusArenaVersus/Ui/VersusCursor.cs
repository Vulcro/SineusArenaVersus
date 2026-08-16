namespace SineusArenaVersus.Ui;

/// <summary>
/// Lobby / end-screen helpers. Prefer <see cref="VersusGameCursor"/> for match play —
/// do not force Unity Cursor every frame (that breaks vanilla RMB free/capture).
/// </summary>
internal static class VersusCursor
{
    /// <summary>
    /// Only used by lobby menu / match end screen — never during match HUD.
    /// </summary>
    public static void UnlockForUi()
    {
        if (!VersusGameCursor.TrySetCursorLock(false))
            VersusGameCursor.UnlockForUiFallback();
    }
}
