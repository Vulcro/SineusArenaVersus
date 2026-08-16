using System;
using System.Collections.Generic;
using SineusArenaVersus.Game;
using SineusArenaVersus.Match;
using SineusArenaVersus.Ui;
using UnityEngine;

namespace SineusArenaVersus.Hud;

public sealed class SendRadialLayout
{
    public float OverlayAlpha { get; set; } = 0.12f;
    public float RadiusFraction { get; set; } = 0.11f;
    public float ButtonWidth { get; set; } = 78f;
    public float ButtonHeight { get; set; } = 34f;
    public float CenterWidth { get; set; } = 132f;
    public float CenterHeight { get; set; } = 58f;
    public float MouseRelatchPixels { get; set; } = 12f;
    public float LocalDimRadiusPad { get; set; } = 28f;
}

public sealed class SendRadialMenu
{
    private readonly Func<(float Width, float Height)> _screenSize;
    private readonly Func<float> _stickDeadzone;
    private readonly Func<Vector2> _anchorScreen;
    private readonly SendRadialLayout _layout;

    private VersusMatch? _match;
    private IReadOnlyList<ulong> _living = Array.Empty<ulong>();
    private int _targetIndex;
    private bool _denyFlash;
    private bool _stickLatched;
    private Vector2 _latchedPointer;
    private Vector2 _anchorScreenPoint;

    public SendRadialMenu(
        Func<(float Width, float Height)>? screenSize = null,
        Func<float>? stickDeadzone = null,
        SendRadialLayout? layout = null,
        Func<Vector2>? anchorScreen = null)
    {
        _screenSize = screenSize ?? ReadScreenSize;
        _stickDeadzone = stickDeadzone ?? ReadStickDeadzone;
        _layout = layout ?? new SendRadialLayout();
        _anchorScreen = anchorScreen ?? GameFacades.GetLocalPlayerScreenPointOrCenter;
    }

    public bool IsOpen { get; private set; }
    public int HighlightIndex { get; private set; }

    public void Close() => SetOpen(false);

    public void Tick(
        VersusInputFrame frame,
        VersusMatch? match,
        ref int targetIndex,
        IReadOnlyList<ulong> livingTargets,
        IReadOnlyList<Rect>? pointerBlockRects = null)
    {
        _match = match;
        _living = livingTargets ?? Array.Empty<ulong>();
        _targetIndex = targetIndex;
        _denyFlash = false;
        _anchorScreenPoint = _anchorScreen();

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

        targetIndex = SendRadialLogic.CycleTarget(_living, targetIndex, frame.CycleTargetDelta);
        _targetIndex = targetIndex;

        UpdateHighlight(frame, match);

        if (!ShouldConfirm(frame, pointerBlockRects))
            return;

        TryConfirm(match, targetIndex);
    }

    public void Draw()
    {
        if (!IsOpen || _match is null)
            return;

        var (width, height) = _screenSize();
        _anchorScreenPoint = _anchorScreen();
        var centerGui = SendRadialLogic.ScreenToGui(_anchorScreenPoint, height);

        var offerings = _match.Catalog.All;
        var count = offerings.Count;
        if (count <= 0)
            return;

        var radius = Mathf.Min(width, height) * _layout.RadiusFraction;
        var dimPad = Math.Max(_layout.ButtonWidth, _layout.ButtonHeight) * 0.5f + _layout.LocalDimRadiusPad;
        var dimSize = (radius + dimPad) * 2f;
        VersusUiTheme.DrawFilled(
            new Rect(centerGui.x - dimSize * 0.5f, centerGui.y - dimSize * 0.5f, dimSize, dimSize),
            new Color(0f, 0f, 0f, _layout.OverlayAlpha));

        var vp = _match.Economy.Vp;

        for (var i = 0; i < count; i++)
        {
            var offering = offerings[i];
            var angle = i * (Mathf.PI * 2f / count);
            var rect = new Rect(
                centerGui.x + Mathf.Cos(angle) * radius - _layout.ButtonWidth * 0.5f,
                centerGui.y - Mathf.Sin(angle) * radius - _layout.ButtonHeight * 0.5f,
                _layout.ButtonWidth,
                _layout.ButtonHeight);

            var affordable = SendRadialLogic.CanConfirm(_match.ShopEnabled, vp, offering.Cost);
            VersusUiTheme.DrawPanel(rect, i == HighlightIndex);
            var previous = GUI.color;
            GUI.color = affordable ? VersusUiTheme.Text : VersusUiTheme.Muted;
            GUI.Label(rect, $"{offering.DisplayName}\n{offering.Cost}");
            GUI.color = previous;

            // Single left-click on a wedge: select + confirm (Update GetKeyDown alone was easy to miss).
            if (Event.current is { type: EventType.MouseDown, button: 0 } &&
                rect.Contains(Event.current.mousePosition))
            {
                HighlightIndex = i;
                Event.current.Use();
                TryConfirm(_match, _targetIndex);
            }
        }

        var selected = HighlightIndex >= 0 && HighlightIndex < count ? offerings[HighlightIndex] : null;
        var targetName = _targetIndex >= 0 && _targetIndex < _living.Count
            ? RivalCardView.FormatPeerName(_living[_targetIndex])
            : "—";
        var offeringLine = selected is null
            ? "—"
            : $"{selected.DisplayName} {selected.Cost}";
        var centerRect = new Rect(
            centerGui.x - _layout.CenterWidth * 0.5f,
            centerGui.y - _layout.CenterHeight * 0.5f,
            _layout.CenterWidth,
            _layout.CenterHeight);
        VersusUiTheme.DrawPanel(centerRect, highlighted: true);
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

        if (frame.RightStickMagnitude >= _stickDeadzone())
        {
            _stickLatched = true;
            _latchedPointer = frame.PointerScreen;
            var stickAngle = RadialMath.AngleFromVector(frame.RightStickX, frame.RightStickY);
            HighlightIndex = SendRadialLogic.ResolveHighlight(
                count,
                stickAngle,
                HighlightIndex,
                frame.RightStickMagnitude,
                _stickDeadzone());
            return;
        }

        if (_stickLatched)
        {
            var moveX = frame.PointerScreen.x - _latchedPointer.x;
            var moveY = frame.PointerScreen.y - _latchedPointer.y;
            var relatch = _layout.MouseRelatchPixels;
            if (moveX * moveX + moveY * moveY < relatch * relatch)
                return;
            _stickLatched = false;
        }

        var dx = frame.PointerScreen.x - _anchorScreenPoint.x;
        var dy = frame.PointerScreen.y - _anchorScreenPoint.y;
        var mouseAngle = RadialMath.AngleFromVector(dx, dy);
        HighlightIndex = RadialMath.SectorIndex(mouseAngle, count);
    }

    private bool ShouldConfirm(VersusInputFrame frame, IReadOnlyList<Rect>? pointerBlockRects)
    {
        if (frame.ConfirmEdge)
            return true;
        if (!frame.PointerConfirmEdge)
            return false;

        var (width, height) = _screenSize();
        return SendRadialLogic.AllowsPointerConfirm(
            frame.PointerScreen,
            _anchorScreenPoint.x,
            _anchorScreenPoint.y,
            width,
            height,
            _layout.RadiusFraction,
            _layout.ButtonWidth,
            _layout.ButtonHeight,
            pointerBlockRects);
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
        {
            _denyFlash = false;
            _stickLatched = false;
        }
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
