using System;
using System.Collections.Generic;
using System.Linq;
using SineusArenaVersus.Match;
using UnityEngine;

namespace SineusArenaVersus.Hud;

public sealed class VersusHud : MonoBehaviour
{
    private const float PanelWidth = 280f;
    private const float RivalCardWidth = 150f;
    private const float RivalCardHeight = 72f;
    private const float RivalCardGap = 8f;

    private VersusMatch? _match;
    private int _targetIndex;
    private ulong[] _livingTargets = Array.Empty<ulong>();
    private bool _collapsed;

    public event Action? LeaveMatchRequested;

    public void Bind(VersusMatch? match)
    {
        _match = match;
        _targetIndex = 0;
        _collapsed = false;
    }

    public void ToggleCollapsed() => _collapsed = !_collapsed;

    private void OnGUI()
    {
        if (_collapsed ||
            _match is null ||
            !_match.IsActive && _match.State != VersusMatchState.Ended)
            return;

        DrawRivalStrip();
        DrawSidePanel();
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

        var totalWidth = rivals.Length * RivalCardWidth + (rivals.Length - 1) * RivalCardGap;
        var startX = (Screen.width - totalWidth) * 0.5f;
        for (var i = 0; i < rivals.Length; i++)
        {
            var rect = new Rect(
                startX + i * (RivalCardWidth + RivalCardGap),
                12f,
                RivalCardWidth,
                RivalCardHeight);
            RivalCardView.Draw(rect, rivals[i], RivalCardView.FormatPeerName(rivals[i].PeerId));
        }
    }

    private void DrawSidePanel()
    {
        if (_match!.State is VersusMatchState.Eliminated or VersusMatchState.Ended)
            return;

        var panelRect = new Rect(Screen.width - PanelWidth - 12f, 12f, PanelWidth, Screen.height - 24f);
        GUILayout.BeginArea(panelRect, GUI.skin.box);
        DrawShopPanel();
        DrawPreviewPanel();
        GUILayout.EndArea();
    }

    private void DrawShopPanel()
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

        var labels = _livingTargets.Select(RivalCardView.FormatPeerName).ToArray();
        _targetIndex = Mathf.Clamp(_targetIndex, 0, _livingTargets.Length - 1);
        _targetIndex = GUILayout.SelectionGrid(_targetIndex, labels, 1);

        GUILayout.Space(8f);
        var shopEnabled = _match.ShopEnabled;
        foreach (var offering in _match.Catalog.All)
        {
            var canAfford = economy.Vp >= offering.Cost;
            GUI.enabled = shopEnabled && canAfford;
            if (GUILayout.Button($"{offering.DisplayName} ({offering.Cost} VP)"))
                _match.TryQueueSend(_livingTargets[_targetIndex], offering.Id);
            GUI.enabled = true;
        }
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

    private void RefreshLivingTargets()
    {
        _livingTargets = _match!.Peers.Values
            .Where(peer => peer.PeerId != _match.LocalPeerId && peer.IsAlive)
            .Select(peer => peer.PeerId)
            .ToArray();
        if (_targetIndex >= _livingTargets.Length)
            _targetIndex = Math.Max(0, _livingTargets.Length - 1);
    }
}
