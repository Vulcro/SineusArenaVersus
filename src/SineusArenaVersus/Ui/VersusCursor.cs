using UnityEngine;

namespace SineusArenaVersus.Ui;

/// <summary>
/// Gameplay often locks the cursor for camera look; IMGUI needs a free cursor.
/// Call <see cref="UnlockForUi"/> only while Versus interactive UI is open
/// (send radial, lobby menu, etc.). Do not call from read-only HUD ticks or when
/// radial/menu is closed. Never sets <see cref="Cursor.lockState"/> to Locked.
/// </summary>
internal static class VersusCursor
{
    public static void UnlockForUi()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
