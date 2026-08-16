using UnityEngine;

namespace SineusArenaVersus.Ui;

/// <summary>
/// Gameplay often locks the cursor for camera look; IMGUI needs a free cursor.
/// </summary>
internal static class VersusCursor
{
    public static void UnlockForUi()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
