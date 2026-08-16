using System;
using System.Threading.Tasks;
using SineusArenaVersus.Hud;
using SineusArenaVersus.Lobby;
using SineusArenaVersus.Match;
using UnityEngine;

namespace SineusArenaVersus.Ui;

public sealed class VersusMenu : MonoBehaviour
{
    private const float PanelWidth = 360f;
    private const float PanelHeight = 420f;
    private const float PanelMargin = 20f;

    private Func<VersusLobby?>? _getLobby;
    private Func<VersusMatch?>? _getMatch;
    private VersusHud? _hud;
    private bool _isOpen;
    private bool _operationPending;
    private string? _error;
    private string? _reportedInvalidKey;

    public void Initialize(
        Func<VersusLobby?> getLobby,
        Func<VersusMatch?> getMatch,
        VersusHud hud)
    {
        _getLobby = getLobby ?? throw new ArgumentNullException(nameof(getLobby));
        _getMatch = getMatch ?? throw new ArgumentNullException(nameof(getMatch));
        _hud = hud ?? throw new ArgumentNullException(nameof(hud));
    }

    private void Update()
    {
        var configuredKey = VersusConfig.OpenVersusMenuKey.Value;
        if (!Enum.TryParse(configuredKey, true, out KeyCode key))
        {
            if (!string.Equals(_reportedInvalidKey, configuredKey, StringComparison.Ordinal))
            {
                _reportedInvalidKey = configuredKey;
                VersusPlugin.Log.LogWarning($"Invalid OpenVersusMenuKey: {configuredKey}");
            }
            return;
        }

        _reportedInvalidKey = null;
        if (!Input.GetKeyDown(key))
            return;

        var match = _getMatch?.Invoke();
        if (match is not null && match.State is not (VersusMatchState.Idle or VersusMatchState.LobbyBound))
        {
            _hud?.ToggleCollapsed();
            _isOpen = false;
            return;
        }

        _isOpen = !_isOpen;
    }

    private void OnGUI()
    {
        if (!_isOpen)
            return;

        var match = _getMatch?.Invoke();
        if (match is not null && match.State is not (VersusMatchState.Idle or VersusMatchState.LobbyBound))
            return;

        var rect = new Rect(
            PanelMargin,
            PanelMargin,
            PanelWidth,
            Math.Min(PanelHeight, Screen.height - PanelMargin * 2f));
        GUILayout.BeginArea(rect, "Versus", GUI.skin.window);
        DrawLobbyPanel(_getLobby?.Invoke());
        GUILayout.EndArea();
    }

    private void DrawLobbyPanel(VersusLobby? lobby)
    {
        if (lobby is null)
        {
            GUILayout.Label("Steam is unavailable.");
            return;
        }

        GUI.enabled = !_operationPending;
        if (!lobby.HasLobby)
        {
            if (GUILayout.Button("Host"))
                RunOperation(lobby.HostLobbyAsync);
            GUI.enabled = true;
            DrawError();
            return;
        }

        GUILayout.Label($"Lobby {lobby.LobbyId}");
        foreach (var peerId in lobby.Members)
        {
            var ready = lobby.IsMemberReady(peerId) ? "Ready" : "Not Ready";
            GUILayout.Label($"{RivalCardView.FormatPeerName(peerId)} — {ready}");
        }

        GUILayout.Space(8f);
        if (GUILayout.Button("Invite Friends"))
            RunOperation(() => lobby.OpenInviteOverlay());
        if (GUILayout.Button(lobby.IsLocalReady ? "Unready" : "Ready"))
            RunOperation(() => lobby.SetReady(!lobby.IsLocalReady));

        GUI.enabled = !_operationPending && lobby.IsLocalHost;
        if (GUILayout.Button("Start"))
            RunOperation(lobby.StartMatchAsHost);
        GUI.enabled = true;
        DrawError();
    }

    private async void RunOperation(Func<Task> operation)
    {
        if (_operationPending)
            return;

        _operationPending = true;
        _error = null;
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            _operationPending = false;
        }
    }

    private void RunOperation(Action operation)
    {
        if (_operationPending)
            return;

        _error = null;
        try
        {
            operation();
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    private void ReportError(Exception exception)
    {
        _error = exception.Message;
        VersusPlugin.Log.LogError($"Versus menu operation failed: {exception}");
    }

    private void DrawError()
    {
        if (!string.IsNullOrWhiteSpace(_error))
            GUILayout.Label(_error);
    }
}
