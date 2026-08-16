using System;
using System.Collections.Generic;
using SineusArenaVersus.Match;
using SineusArenaVersus.Ui;
using UnityEngine;

namespace SineusArenaVersus.Hud;

public sealed class SendRadialLayout
{
    public float OverlayAlpha { get; set; } = 0.45f;
    public float RadiusFraction { get; set; } = 0.28f;
    public float ButtonWidth { get; set; } = 108f;
    public float ButtonHeight { get; set; } = 48f;
    public float CenterWidth { get; set; } = 220f;
    public float CenterHeight { get; set; } = 96f;
}

public sealed class SendRadialMenu
{
    private readonly Func<(float Width, float Height)> _screenSize;
    private readonly Func<float> _stickDeadzone;
    private readonly SendRadialLayout _layout;

    private VersusMatch? _match;
    private IReadOnlyList<ulong> _living = Array.Empty<ulong>();
    private int _targetIndex;
    private bool _denyFlash;

    public SendRadialMenu(
        Func<(float Width, float Height)>? screenSize = null,
        Func<float>? stickDeadzone = null,
        SendRadialLayout? layout = null)
    {
        _screenSize = screenSize ?? ReadScreenSize;
        _stickDeadzone = stickDeadzone ?? ReadStickDeadzone;
        _layout = layout ?? new SendRadialLayout();
    }

    public bool IsOpen { get; private set; }
    public int HighlightIndex { get; private set; }

    public void Close() => SetOpen(false);

    public void Tick(
        VersusInputFrame frame,
        VersusMatch? match,
        ref int targetIndex,
        IReadOnlyList<ulong> livingTargets)
    {
        _match = match;
        _living = livingTargets ?? Array.Empty<ulong>();
        _targetIndex = targetIndex;
        _denyFlash = false;

        if (match is null ||
            match.State is VersusMatchState.Eliminated or VersusMatchState.Ended)
        {
            SetOpen(false);
            return;
        }

        if (_living.Count == 0)
        {
            targetIndex = -1;
            _targetIndex = -1;
            SetOpen(false);
            return;
        }

        targetIndex = SendRadialLogic.CycleTarget(_living, targetIndex, frame.CycleTargetDelta);
        _targetIndex = targetIndex;

        if (frame.VanillaUiBlocksVersus)
        {
            if (!IsOpen)
                return;
            SetOpen(false);
            return;
        }

        if (frame.ToggleRadialEdge)
        {
            if (IsOpen)
                SetOpen(false);
            else if (CanOpen(match))
                SetOpen(true);
            if (IsOpen)
                UpdateHighlight(frame, match);
            return;
        }

        if (!IsOpen)
            return;

        if (frame.CancelEdge)
        {
            SetOpen(false);
            return;
        }

        UpdateHighlight(frame, match);

        if (!frame.ConfirmEdge)
            return;

        TryConfirm(match, targetIndex);
    }

    public void Draw()
    {
        if (!IsOpen || _match is null)
            return;

        var (width, height) = _screenSize();
        VersusUiTheme.DrawFilled(
            new Rect(0f, 0f, width, height),
            new Color(0f, 0f, 0f, _layout.OverlayAlpha));

        var offerings = _match.Catalog.All;
        var count = offerings.Count;
        if (count <= 0)
            return;

        var center = new Vector2(width * 0.5f, height * 0.5f);
        var radius = Mathf.Min(width, height) * _layout.RadiusFraction;
        var vp = _match.Economy.Vp;

        for (var i = 0; i < count; i++)
        {
            var offering = offerings[i];
            var angle = i * (Mathf.PI * 2f / count);
            var rect = new Rect(
                center.x + Mathf.Cos(angle) * radius - _layout.ButtonWidth * 0.5f,
                center.y - Mathf.Sin(angle) * radius - _layout.ButtonHeight * 0.5f,
                _layout.ButtonWidth,
                _layout.ButtonHeight);

            var affordable = SendRadialLogic.CanConfirm(_match.ShopEnabled, vp, offering.Cost);
            if (i == HighlightIndex)
                VersusUiTheme.DrawFilled(rect, VersusUiTheme.HoverFill);

            var previous = GUI.color;
            GUI.color = affordable ? VersusUiTheme.Text : VersusUiTheme.Muted;
            if (GUI.Button(rect, $"{offering.DisplayName}\n{offering.Cost} VP"))
                HighlightIndex = i;
            GUI.color = previous;
        }

        var selected = HighlightIndex >= 0 && HighlightIndex < count ? offerings[HighlightIndex] : null;
        var targetName = _targetIndex >= 0 && _targetIndex < _living.Count
            ? RivalCardView.FormatPeerName(_living[_targetIndex])
            : "No target";
        var offeringLine = selected is null
            ? "—"
            : $"{selected.DisplayName}  {selected.Cost} VP";
        var centerRect = new Rect(
            center.x - _layout.CenterWidth * 0.5f,
            center.y - _layout.CenterHeight * 0.5f,
            _layout.CenterWidth,
            _layout.CenterHeight);
        VersusUiTheme.DrawFilled(centerRect, VersusUiTheme.PanelBg);
        var labelColor = _denyFlash ? VersusUiTheme.Muted : VersusUiTheme.Text;
        var previousLabel = GUI.color;
        GUI.color = labelColor;
        GUI.Label(centerRect, $"{targetName}\n{offeringLine}\nVP {vp}");
        GUI.color = previousLabel;
    }

    private void UpdateHighlight(VersusInputFrame frame, VersusMatch match)
    {
        var count = match.Catalog.All.Count;
        if (count <= 0)
        {
            HighlightIndex = -1;
            return;
        }

        float angle;
        if (frame.RightStickMagnitude >= _stickDeadzone())
        {
            angle = RadialMath.AngleFromVector(frame.RightStickX, frame.RightStickY);
            HighlightIndex = SendRadialLogic.ResolveHighlight(
                count,
                angle,
                HighlightIndex,
                frame.RightStickMagnitude,
                _stickDeadzone());
            return;
        }

        var (width, height) = _screenSize();
        var dx = frame.PointerScreen.x - width * 0.5f;
        var dy = frame.PointerScreen.y - height * 0.5f;
        angle = RadialMath.AngleFromVector(dx, dy);
        HighlightIndex = RadialMath.SectorIndex(angle, count);
    }

    private void TryConfirm(VersusMatch match, int targetIndex)
    {
        var offerings = match.Catalog.All;
        if (HighlightIndex < 0 || HighlightIndex >= offerings.Count ||
            targetIndex < 0 || targetIndex >= _living.Count)
        {
            _denyFlash = true;
            return;
        }

        var offering = offerings[HighlightIndex];
        if (!SendRadialLogic.CanConfirm(match.ShopEnabled, match.Economy.Vp, offering.Cost) ||
            !match.TryQueueSend(_living[targetIndex], offering.Id))
            _denyFlash = true;
    }

    private bool CanOpen(VersusMatch match) =>
        match.ShopEnabled && _living.Count > 0 && match.Catalog.All.Count > 0;

    private void SetOpen(bool open)
    {
        if (IsOpen == open)
            return;
        IsOpen = open;
        VersusCameraLookGate.SetRadialOpen(open);
        if (!open)
            _denyFlash = false;
    }

    private static (float Width, float Height) ReadScreenSize()
    {
        try
        {
            return (Screen.width, Screen.height);
        }
        catch (Exception)
        {
            return (1f, 1f);
        }
    }

    private static float ReadStickDeadzone()
    {
        try
        {
            return VersusConfig.RadialStickDeadzone?.Value ?? 0.35f;
        }
        catch (Exception)
        {
            return 0.35f;
        }
    }
}
