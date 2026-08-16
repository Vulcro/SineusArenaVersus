using System;
using System.Collections.Generic;
using System.Linq;
using SineusArenaVersus.Match;
using SineusArenaVersus.Spectate;
using SineusArenaVersus.Ui;
using UnityEngine;

namespace SineusArenaVersus.Hud;

public sealed class VersusHud : MonoBehaviour
{
    private const float PanelWidth = 280f;
    private const float RivalCardWidth = 150f;
    private const float RivalCardHeight = 72f;
    private const float RivalCardGap = 8f;

    private VersusMatch? _match;
    private VersusSpectate? _spectate;
    private readonly SendRadialMenu _radial = new();
    private int _targetIndex;
    private ulong[] _livingTargets = Array.Empty<ulong>();
    private bool _collapsed;
    private Rect _sidePanelRect = new(0f, 12f, PanelWidth, 420f);
    private const int SideWindowId = 0x56525332; // VRS2

    public event Action? LeaveMatchRequested;
    public int TargetIndex => _targetIndex;

    public void Bind(VersusMatch? match, VersusSpectate? spectate = null)
    {
        _match = match;
        _spectate = spectate;
        _targetIndex = 0;
        _collapsed = false;
        _radial.Close();
        _spectate?.Bind(match);
    }

    public void TickInput(VersusInputFrame frame)
    {
        if (_match is null)
        {
            _radial.Close();
            return;
        }

        RefreshLivingTargets();
        var previous = _targetIndex;
        _radial.Tick(frame, _match, ref _targetIndex, _livingTargets);
        if (_spectate is not null &&
            _targetIndex != previous &&
            _spectate.ShowMiniView &&
            _targetIndex >= 0 &&
            _targetIndex < _livingTargets.Length)
            _spectate.SetFocusedPeer(_livingTargets[_targetIndex]);
    }

    public void ToggleCollapsed() => _collapsed = !_collapsed;

    private void OnGUI()
    {
        if (_match is null ||
            !_match.IsActive && _match.State != VersusMatchState.Ended)
            return;

        if (_match.State is VersusMatchState.Eliminated or VersusMatchState.Ended)
            VersusCursor.UnlockForUi();

        if (!_collapsed)
        {
            DrawRivalStrip();
            DrawSidePanel();
        }

        _radial.Draw();
        DrawOverlay();
    }

    private void DrawRivalStrip()
    {
        var rivals = _match!.Peers.Values
            .Where(peer => peer.PeerId != _match.LocalPeerId)
            .Take(3)
            .ToArray();
        if (rivals.Length == 0)
            return;

        RefreshLivingTargets();
        var selectedId = _livingTargets.Length > 0 &&
                         _targetIndex >= 0 &&
                         _targetIndex < _livingTargets.Length
            ? _livingTargets[_targetIndex]
            : 0UL;
        var totalWidth = rivals.Length * RivalCardWidth + (rivals.Length - 1) * RivalCardGap;
        var startX = (Screen.width - totalWidth) * 0.5f;
        for (var i = 0; i < rivals.Length; i++)
        {
            var rect = new Rect(
                startX + i * (RivalCardWidth + RivalCardGap),
                12f,
                RivalCardWidth,
                RivalCardHeight);
            var selected = rivals[i].PeerId == selectedId;
            if (selected)
                VersusUiTheme.DrawFilled(rect, VersusUiTheme.HoverFill);
            RivalCardView.Draw(rect, rivals[i], RivalCardView.FormatPeerName(rivals[i].PeerId));
            TrySelectRivalFromClick(rect, rivals[i].PeerId);
        }
    }

    private void DrawSidePanel()
    {
        if (_match!.State is VersusMatchState.Eliminated or VersusMatchState.Ended)
            return;

        if (_sidePanelRect.x <= 0f)
            _sidePanelRect.x = Screen.width - PanelWidth - 12f;
        _sidePanelRect.width = PanelWidth;
        _sidePanelRect.height = Mathf.Min(520f, Screen.height - 24f);
        _sidePanelRect = VersusImguiWindow.Draw(SideWindowId, _sidePanelRect, DrawSideWindow, "Versus HUD");
    }

    private void DrawSideWindow(int id)
    {
        DrawInfoPanel();
        DrawSpectatePanel();
        DrawPreviewPanel();
    }

    private void DrawInfoPanel()
    {
        var economy = _match!.Economy;
        GUILayout.Label($"VP: {economy.Vp}");
        GUILayout.Label($"Passive: +{economy.PassiveAmountPerTick} / {VersusConfig.PassiveIntervalSeconds.Value:0.#}s");
        GUILayout.Space(6f);

        RefreshLivingTargets();
        if (_livingTargets.Length == 0)
        {
            GUILayout.Label("No living rivals");
            return;
        }

        _targetIndex = Mathf.Clamp(_targetIndex, 0, _livingTargets.Length - 1);
        GUILayout.Label($"Target: {RivalCardView.FormatPeerName(_livingTargets[_targetIndex])}");
    }

    private void DrawSpectatePanel()
    {
        if (_spectate is null)
            return;

        GUILayout.Space(8f);
        GUILayout.Label("Spectate (0.2.0 polish)");
        GUI.enabled = VersusConfig.EnableSpectateViews.Value && _match!.IsActive;
        var nextShow = GUILayout.Toggle(_spectate.ShowMiniView, "Mini rival view");
        if (GUI.enabled)
        {
            if (nextShow && !_spectate.ShowMiniView && _livingTargets.Length > 0)
                _spectate.SetFocusedPeer(_livingTargets[_targetIndex]);
            else if (!nextShow)
                _spectate.ClearFocusedPeer();
            _spectate.ShowMiniView = nextShow;
        }

        GUI.enabled = true;
        if (!VersusConfig.EnableSpectateViews.Value)
            GUILayout.Label("Set EnableSpectateViews=true for 0.2.0 preview.");
    }

    private void DrawPreviewPanel()
    {
        GUILayout.Space(12f);
        GUILayout.Label($"Wave {_match!.WaveIndex + 1} in {_match.WaveSecondsRemaining:0.#}s");
        GUILayout.Label("Incoming");
        if (_match.IncomingPreview.Count == 0)
        {
            GUILayout.Label("None");
            return;
        }

        foreach (var entry in _match.IncomingPreview.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var label = _match.Catalog.TryGet(entry.Key, out var offering)
                ? $"{offering.DisplayName} x{entry.Value}"
                : $"{entry.Key} x{entry.Value}";
            GUILayout.Label(label);
        }
    }

    private void DrawOverlay()
    {
        if (_match!.State is not (VersusMatchState.Eliminated or VersusMatchState.Ended))
            return;

        var previousColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none);
        GUI.color = previousColor;

        var boxWidth = 360f;
        var boxHeight = 180f;
        var boxRect = new Rect(
            (Screen.width - boxWidth) * 0.5f,
            (Screen.height - boxHeight) * 0.5f,
            boxWidth,
            boxHeight);
        GUI.Box(boxRect, GUIContent.none);

        var title = _match.State switch
        {
            VersusMatchState.Eliminated => "Eliminated",
            VersusMatchState.Ended when _match.WinnerPeerId == _match.LocalPeerId => "Victory!",
            VersusMatchState.Ended => $"Winner: {RivalCardView.FormatPeerName(_match.WinnerPeerId ?? 0UL)}",
            _ => "Match Over"
        };

        GUI.Label(
            new Rect(boxRect.x, boxRect.y + 24f, boxRect.width, 40f),
            title);

        if (GUI.Button(new Rect(boxRect.x + 80f, boxRect.y + 100f, boxRect.width - 160f, 32f), "Return to Lobby"))
            LeaveMatchRequested?.Invoke();
    }

    private void TrySelectRivalFromClick(Rect rect, ulong peerId)
    {
        var current = Event.current;
        if (current is null ||
            current.type != EventType.MouseDown ||
            current.button != 0 ||
            !rect.Contains(current.mousePosition))
            return;

        var livingIndex = Array.IndexOf(_livingTargets, peerId);
        if (livingIndex < 0)
            return;

        _targetIndex = livingIndex;
        current.Use();
        if (_spectate is not null && _spectate.ShowMiniView)
            _spectate.SetFocusedPeer(peerId);
    }

    private void RefreshLivingTargets()
    {
        if (_match is null)
        {
            _livingTargets = Array.Empty<ulong>();
            return;
        }

        _livingTargets = _match.Peers.Values
            .Where(peer => peer.PeerId != _match.LocalPeerId && peer.IsAlive)
            .Select(peer => peer.PeerId)
            .ToArray();
        if (_targetIndex >= _livingTargets.Length)
            _targetIndex = Math.Max(0, _livingTargets.Length - 1);
    }
}
