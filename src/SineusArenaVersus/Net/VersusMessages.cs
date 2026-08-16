namespace SineusArenaVersus.Net;

public enum VersusOpcode : byte
{
    MatchStart = 1,
    WaveTick = 2,
    QueueSend = 3,
    RivalSnap = 4,
    StrongholdDown = 5,
    Winner = 6,
    Ready = 7,
    Refund = 8,
    VpReport = 9
}

public readonly record struct MatchStartMsg(ulong LobbyId, float WaveInterval, ulong[] Peers);
public readonly record struct WaveTickMsg(int WaveIndex, float HostTime);
public readonly record struct QueueSendMsg(ulong From, ulong To, string CatalogId, int Count);
public readonly record struct RivalSnapMsg(ulong PeerId, float StrongholdHp01, bool Alive);
public readonly record struct VpReportMsg(ulong PeerId, int Vp);
public readonly record struct PeerMsg(ulong PeerId);
