using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SineusArenaVersus.Match;
using SineusArenaVersus.Spectate;
using SineusArenaVersus.Ui;
using UnityEngine;

namespace SineusArenaVersus.Hud;

/// <summary>
/// Slim Versus match chrome: rival strip + compact status strip (no floating shop window).
/// </summary>
public sealed class VersusHud : MonoBehaviour
{
    private const float StatusWidth = 248f;
    private const float StatusHeight = 96f;
    private const float RivalCardWidth = 140f;
    private const float RivalCardHeight = 56f;
    private const float RivalCardGap = 6f;
    private const float Margin = 10f;

    private VersusMatch? _match;
    private VersusSpectate? _spectate;
    private readonly SendRadialMenu _radial = new();
    private readonly StringBuilder _incomingBuilder = new(64);
    private int _targetIndex;
    private ulong[] _livingTargets = Array.Empty<ulong>();
    private bool _collapsed;
    private Rect _statusRect = new(Margin, 0f, StatusWidth, StatusHeight);

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
        _radial.Tick(frame, _match, ref _targetIndex, _livingTargets, CollectPointerBlockRects());
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
            DrawStatusStrip();
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

        // Bottom-left stack above the status strip — avoid vanilla top HP / timer / minimap.
        var selectedId = _livingTargets.Length > 0 &&
                         _targetIndex >= 0 &&
                         _targetIndex < _livingTargets.Length
            ? _livingTargets[_targetIndex]
            : 0UL;
        var stackBottom = Screen.height - StatusHeight - Margin - 6f;
        for (var i = 0; i < rivals.Length; i++)
        {
            var fromBottom = rivals.Length - 1 - i;
            var rect = new Rect(
                Margin,
                stackBottom - (fromBottom + 1) * (RivalCardHeight + RivalCardGap),
                RivalCardWidth,
                RivalCardHeight);
            RivalCardView.Draw(rect, rivals[i], RivalCardView.FormatPeerName(rivals[i].PeerId));
            if (rivals[i].PeerId == selectedId)
                VersusUiTheme.DrawBorder(rect, VersusUiTheme.Accent, 2f);
            TrySelectRivalFromClick(rect, rivals[i].PeerId);
        }
    }

    private void DrawStatusStrip()
    {
        if (_match!.State is VersusMatchState.Eliminated or VersusMatchState.Ended)
            return;

        _statusRect = new Rect(
            Margin,
            Screen.height - StatusHeight - Margin,
            StatusWidth,
            StatusHeight);
        VersusUiTheme.DrawFilled(_statusRect, VersusUiTheme.PanelBg);
        VersusUiTheme.DrawBorder(_statusRect, VersusUiTheme.PanelBorder, 1f);

        var economy = _match.Economy;
        var y = _statusRect.y + 6f;
        var x = _statusRect.x + 8f;
        var w = _statusRect.width - 16f;
        var prev = GUI.color;
        GUI.color = VersusUiTheme.Text;
        GUI.Label(new Rect(x, y, w, 16f), $"VP {economy.Vp}  ·  +{economy.PassiveAmountPerTick}/tick");
        y += 16f;
        GUI.Label(
            new Rect(x, y, w, 16f),
            $"Wave {_match.WaveIndex + 1}  ·  {_match.WaveSecondsRemaining:0.#}s");
        y += 16f;

        var targetLine = _livingTargets.Length == 0
            ? "Target —"
            : $"Target  {RivalCardView.FormatPeerName(_livingTargets[Mathf.Clamp(_targetIndex, 0, _livingTargets.Length - 1)])}";
        GUI.color = VersusUiTheme.Accent;
        GUI.Label(new Rect(x, y, w, 16f), targetLine);
        y += 16f;

        GUI.color = VersusUiTheme.Muted;
        GUI.Label(new Rect(x, y, w, 28f), FormatIncomingLine());
        GUI.color = prev;

        if (_spectate is not null && VersusConfig.EnableSpectateViews.Value)
        {
            var toggleRect = new Rect(x, _statusRect.yMax - 22f, w, 18f);
            _spectate.ShowMiniView = GUI.Toggle(toggleRect, _spectate.ShowMiniView, "Mini view");
        }
    }

    private string FormatIncomingLine()
    {
        if (_match!.IncomingPreview.Count == 0)
            return "Incoming  none";

        _incomingBuilder.Clear();
        _incomingBuilder.Append("Incoming  ");
        var first = true;
        foreach (var entry in _match.IncomingPreview.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!first)
                _incomingBuilder.Append(" · ");
            first = false;
            if (_match.Catalog.TryGet(entry.Key, out var offering))
                _incomingBuilder.Append(offering.DisplayName).Append(' ').Append('x').Append(entry.Value);
            else
                _incomingBuilder.Append(entry.Key).Append(' ').Append('x').Append(entry.Value);
        }

        return _incomingBuilder.ToString();
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
        VersusUiTheme.DrawPanel(boxRect, highlighted: true);

        var title = _match.State switch
        {
            VersusMatchState.Eliminated => "Eliminated",
            VersusMatchState.Ended when _match.WinnerPeerId == _match.LocalPeerId => "Victory!",
            VersusMatchState.Ended => $"Winner: {RivalCardView.FormatPeerName(_match.WinnerPeerId ?? 0UL)}",
            _ => "Match Over"
        };

        GUI.color = VersusUiTheme.Text;
        GUI.Label(new Rect(boxRect.x + 16f, boxRect.y + 24f, boxRect.width - 32f, 40f), title);
        GUI.color = previousColor;

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

    private IReadOnlyList<Rect> CollectPointerBlockRects()
    {
        if (_collapsed)
            return Array.Empty<Rect>();

        var rects = new List<Rect>(2);
        if (TryComputeRivalStripRect(out var strip))
            rects.Add(strip);

        try
        {
            _statusRect = new Rect(
                Margin,
                Screen.height - StatusHeight - Margin,
                StatusWidth,
                StatusHeight);
        }
        catch (Exception)
        {
            _statusRect = new Rect(Margin, 600f, StatusWidth, StatusHeight);
        }

        rects.Add(_statusRect);
        return rects;
    }

    private bool TryComputeRivalStripRect(out Rect rect)
    {
        rect = default;
        if (_match is null)
            return false;

        var rivalCount = 0;
        foreach (var peer in _match.Peers.Values)
        {
            if (peer.PeerId == _match.LocalPeerId)
                continue;
            rivalCount++;
            if (rivalCount >= 3)
                break;
        }

        if (rivalCount <= 0)
            return false;

        float screenHeight;
        try
        {
            screenHeight = Screen.height;
        }
        catch (Exception)
        {
            return false;
        }

        var stackBottom = screenHeight - StatusHeight - Margin - 6f;
        var top = stackBottom - rivalCount * (RivalCardHeight + RivalCardGap) + RivalCardGap;
        rect = new Rect(Margin, top, RivalCardWidth, rivalCount * (RivalCardHeight + RivalCardGap) - RivalCardGap);
        return true;
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
