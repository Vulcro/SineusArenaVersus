namespace SineusArenaVersus.Ui;

/// <summary>
/// Lobby / end-screen helpers. Prefer <see cref="VersusGameCursor"/> for match play —
/// do not force Unity Cursor every frame (that breaks vanilla RMB free/capture).
/// </summary>
internal static class VersusCursor
{
    public static void UnlockForUi() =>
        VersusGameCursor.TrySetCursorLock(false);
}
