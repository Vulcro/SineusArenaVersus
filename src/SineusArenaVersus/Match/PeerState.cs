namespace SineusArenaVersus.Match;

public sealed class PeerState
{
    public PeerState(ulong peerId)
    {
        PeerId = peerId;
    }

    public ulong PeerId { get; }
    public bool IsAlive { get; internal set; } = true;
    public float StrongholdHp01 { get; internal set; } = 1f;
}
