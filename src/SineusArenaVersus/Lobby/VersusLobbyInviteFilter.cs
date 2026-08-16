using System;
using System.Threading.Tasks;

namespace SineusArenaVersus.Lobby;

/// <summary>
/// Pure invite-filter helpers (no Steamworks reference) for unit tests.
/// </summary>
public static class VersusLobbyInviteFilter
{
    public static bool IsVersusLobby(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal);

    public static async Task<bool> RefreshAndCheckVersusAsync(
        Func<Task<bool>> refresh,
        Func<string?> readVersusData)
    {
        if (refresh is null)
            throw new ArgumentNullException(nameof(refresh));
        if (readVersusData is null)
            throw new ArgumentNullException(nameof(readVersusData));

        return await refresh() && IsVersusLobby(readVersusData());
    }
}
