using SineusArenaVersus.Match;

namespace SineusArenaVersus.Spectate;

/// <summary>
/// Polish scaffolding for SubViewport rival spectate (target release: 0.2.0).
/// V1 ships config + HUD toggle only — no SubViewport or camera render texture.
/// Future: extend <see cref="Net.RivalSnapMsg"/> with optional pose, render one focused rival.
/// </summary>
public sealed class VersusSpectate
{
    private VersusMatch? _match;

    public bool ShowMiniView { get; set; }

    public ulong FocusedPeerId { get; private set; }

    public bool IsConfigured => VersusConfig.EnableSpectateViews.Value;

    public bool IsActive =>
        IsConfigured &&
        ShowMiniView &&
        _match is { IsActive: true } &&
        FocusedPeerId != 0UL;

    public void Bind(VersusMatch? match)
    {
        _match = match;
        ShowMiniView = false;
        FocusedPeerId = 0UL;
    }

    public void SetFocusedPeer(ulong peerId)
    {
        if (!IsConfigured || peerId == 0UL)
            return;

        FocusedPeerId = peerId;
    }

    public void ClearFocusedPeer() => FocusedPeerId = 0UL;
}
