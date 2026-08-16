using System;
using SineusArenaVersus.Catalog;
using SineusArenaVersus.Economy;
using SineusArenaVersus.Game;
using SineusArenaVersus.Match;
using SineusArenaVersus.Steam;

namespace SineusArenaVersus.Dev;

/// <summary>
/// Local-only Versus harness: fake rival, sends redirect to self, optional solo arena boot.
/// Gated by <see cref="VersusConfig.EnableSoloDevTest"/> — not for production Friends play.
/// </summary>
public static class SoloDevTest
{
    public static bool IsEnabled => VersusConfig.EnableSoloDevTest.Value;

    public static bool TryStart(out string? error)
    {
        error = null;
        if (!IsEnabled)
        {
            error = "Solo Dev Test is disabled (Debug.EnableSoloDevTest).";
            return false;
        }

        if (VersusPlugin.ActiveMatch?.IsActive == true)
        {
            error = "A Versus match is already active.";
            return false;
        }

        var localPeerId = ResolveLocalPeerId();
        var rivalPeerId = VersusConfig.DebugRivalPeerId.Value;
        if (localPeerId == 0UL || rivalPeerId == 0UL || localPeerId == rivalPeerId)
        {
            error = "Invalid Solo Dev peer ids (check DebugLocalPeerId / DebugRivalPeerId).";
            return false;
        }

        VersusPlugin.LeaveActiveMatch();

        ISoloRunLauncher launcher = VersusConfig.SoloDevBootArena.Value
            ? new ReflectionSoloRunLauncher(
                message => VersusPlugin.Log.LogError(message),
                detachVersusLobby: null, // keep hub Steam/NGO session for soft arena boot
                softBoot: true)
            : new AlreadyRunningSoloLauncher();

        var economy = new VersusEconomy(
            VersusConfig.PassiveBase.Value,
            VersusConfig.PassivePerSuccessfulSend.Value,
            () => VersusConfig.VpTrash.Value,
            () => VersusConfig.VpElite.Value,
            () => VersusConfig.VpBoss.Value);
        var match = new VersusMatch(
            localPeerId,
            economy,
            VersusCatalog.Load(),
            redirectTargetsToLocal: true,
            soloRunLauncher: launcher);
        match.QueueSendRequested += send => match.OnQueueSendValidated(send);

        if (!match.StartMatch(new[] { localPeerId, rivalPeerId }, isHost: true))
        {
            match.Dispose();
            error = VersusConfig.SoloDevBootArena.Value
                ? "Solo arena boot failed. Start a vanilla Solo run, then retry Solo Dev Test."
                : "No active solo run. Start Solo in the game hub, or enable SoloDevBootArena.";
            return false;
        }

        VersusPlugin.ActiveMatch = match;
        VersusPlugin.Log.LogInfo(
            $"Solo Dev Test started (local={localPeerId}, rival={rivalPeerId}, bootArena={VersusConfig.SoloDevBootArena.Value}).");
        return true;
    }

    private static ulong ResolveLocalPeerId()
    {
        if (SteamSession.TryGetLocalId(out var id) && id.IsValid())
            return id.m_SteamID;
        return VersusConfig.DebugLocalPeerId.Value;
    }

    private sealed class AlreadyRunningSoloLauncher : ISoloRunLauncher
    {
        public bool IsSoloRunActive() => GameFacades.IsSoloRunActive();

        public bool TryStartSoloRun() => IsSoloRunActive();
    }
}
