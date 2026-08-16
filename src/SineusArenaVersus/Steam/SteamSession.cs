using Steamworks;

namespace SineusArenaVersus.Steam;

/// <summary>
/// Steamworks.NET session probes.
/// Do not gate on <see cref="SteamAPI.IsSteamRunning"/> — under TMM / Heathen it stays false
/// even after the game has a valid Steam user (see BepInEx: Local User / SteamClientIdMapper).
/// </summary>
public static class SteamSession
{
    public static bool TryGetLocalId(out CSteamID id)
    {
        id = default;
        try
        {
            id = SteamUser.GetSteamID();
            return id.IsValid();
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAttached() => TryGetLocalId(out _);

    public static ulong LocalIdOrZero() =>
        TryGetLocalId(out var id) ? id.m_SteamID : 0UL;
}
