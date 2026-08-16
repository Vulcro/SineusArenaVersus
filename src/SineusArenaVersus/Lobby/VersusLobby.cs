using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SineusArenaVersus.Net;
using SineusArenaVersus.Steam;
using Steamworks;

namespace SineusArenaVersus.Lobby;

public sealed class VersusLobby : IDisposable
{
    public const string VersusDataKey = "versus";
    public const string ReadyDataKey = "ready";

    private readonly Func<int> _maxPlayers;
    private readonly Func<float> _waveInterval;
    private readonly Func<VersusNet?> _net;
    private readonly Callback<GameLobbyJoinRequested_t> _joinRequested;
    private readonly Callback<LobbyChatUpdate_t> _lobbyChatUpdate;
    private readonly CallResult<LobbyCreated_t> _lobbyCreated;
    private readonly CallResult<LobbyEnter_t> _lobbyEnter;
    private CSteamID? _lobbyId;
    private TaskCompletionSource<CSteamID>? _createTcs;
    private TaskCompletionSource<bool>? _joinTcs;
    private bool _disposed;

    public VersusLobby(
        Func<int> maxPlayers,
        Func<float> waveInterval,
        Func<VersusNet?> net)
    {
        _maxPlayers = maxPlayers ?? throw new ArgumentNullException(nameof(maxPlayers));
        _waveInterval = waveInterval ?? throw new ArgumentNullException(nameof(waveInterval));
        _net = net ?? throw new ArgumentNullException(nameof(net));
        _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
        _lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
        _lobbyCreated = CallResult<LobbyCreated_t>.Create();
        _lobbyEnter = CallResult<LobbyEnter_t>.Create();
    }

    public event Action? SessionChanged;
    public event Action<ulong>? MemberLeft;
    public event Action<Exception>? LobbyError;

    public bool HasLobby => _lobbyId.HasValue && _lobbyId.Value.IsValid();
    public ulong LobbyId => _lobbyId?.m_SteamID ?? 0UL;
    public ulong HostPeerId =>
        HasLobby ? SteamMatchmaking.GetLobbyOwner(_lobbyId!.Value).m_SteamID : 0UL;
    public bool IsLocalHost =>
        HasLobby && SteamMatchmaking.GetLobbyOwner(_lobbyId!.Value) == SteamUser.GetSteamID();
    public bool IsLocalReady =>
        HasLobby &&
        SteamMatchmaking.GetLobbyMemberData(_lobbyId!.Value, SteamUser.GetSteamID(), ReadyDataKey) == "1";

    public IReadOnlyList<ulong> Members
    {
        get
        {
            if (!HasLobby)
                return Array.Empty<ulong>();

            var count = SteamMatchmaking.GetNumLobbyMembers(_lobbyId!.Value);
            var members = new ulong[count];
            for (var i = 0; i < count; i++)
                members[i] = SteamMatchmaking.GetLobbyMemberByIndex(_lobbyId.Value, i).m_SteamID;
            return members;
        }
    }

    public Task HostLobbyAsync()
    {
        ThrowIfDisposed();
        RequireSteam();
        if (HasLobby)
            throw new InvalidOperationException("Already in a Steam lobby.");

        var maxPlayers = _maxPlayers();
        if (maxPlayers < 2 || maxPlayers > 4)
            throw new InvalidOperationException("Versus lobby size must be between 2 and 4.");

        _createTcs = new TaskCompletionSource<CSteamID>();
        var call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxPlayers);
        _lobbyCreated.Set(call, OnLobbyCreated);
        return WaitCreateAndConfigureAsync();
    }

    public void OpenInviteOverlay()
    {
        ThrowIfDisposed();
        SteamFriends.ActivateGameOverlayInviteDialog(RequireLobby());
    }

    public bool IsMemberReady(ulong peerId)
    {
        var lobby = RequireLobby();
        return SteamMatchmaking.GetLobbyMemberData(lobby, new CSteamID(peerId), ReadyDataKey) == "1";
    }

    public void SetReady(bool ready)
    {
        ThrowIfDisposed();
        SteamMatchmaking.SetLobbyMemberData(RequireLobby(), ReadyDataKey, ready ? "1" : "0");
    }

    public void StartMatchAsHost()
    {
        ThrowIfDisposed();
        var lobby = RequireLobby();
        if (SteamMatchmaking.GetLobbyOwner(lobby) != SteamUser.GetSteamID())
            throw new InvalidOperationException("Only the lobby owner can start Versus.");

        var members = Members;
        if (members.Count < 2 || members.Count > _maxPlayers())
            throw new InvalidOperationException("Versus requires 2-4 lobby members.");
        foreach (var peerId in members)
        {
            if (SteamMatchmaking.GetLobbyMemberData(lobby, new CSteamID(peerId), ReadyDataKey) != "1")
                throw new InvalidOperationException("Every lobby member must be ready.");
        }

        var net = _net() ?? throw new InvalidOperationException("Versus networking is not attached.");
        if (!net.StartMatchAsHost(lobby.m_SteamID, members, _waveInterval()))
            throw new InvalidOperationException(
                "Versus start aborted because the local solo run could not be launched.");
    }

    public bool ContainsPeer(ulong peerId)
    {
        foreach (var member in Members)
        {
            if (member == peerId)
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (HasLobby)
            SteamMatchmaking.LeaveLobby(_lobbyId!.Value);
        _lobbyId = null;
        _disposed = true;
    }

    private async Task WaitCreateAndConfigureAsync()
    {
        var lobby = await _createTcs!.Task.ConfigureAwait(true);
        _lobbyId = lobby;
        SteamMatchmaking.SetLobbyJoinable(lobby, true);
        if (!SteamMatchmaking.SetLobbyData(lobby, VersusDataKey, "1"))
            throw new InvalidOperationException("Steam rejected Versus lobby metadata.");
        SteamMatchmaking.SetLobbyMemberData(lobby, ReadyDataKey, "0");
        SessionChanged?.Invoke();
    }

    private void OnLobbyCreated(LobbyCreated_t data, bool ioFailure)
    {
        if (_createTcs is null)
            return;
        if (ioFailure || data.m_eResult != EResult.k_EResultOK)
        {
            _createTcs.TrySetException(
                new InvalidOperationException($"Steam lobby create failed: {data.m_eResult}"));
            return;
        }

        _createTcs.TrySetResult(new CSteamID(data.m_ulSteamIDLobby));
    }

    private async void OnJoinRequested(GameLobbyJoinRequested_t data)
    {
        try
        {
            if (_disposed)
                return;

            var lobby = data.m_steamIDLobby;
            var ok = await VersusLobbyInviteFilter.RefreshAndCheckVersusAsync(
                () => RefreshLobbyAsync(lobby),
                () => SteamMatchmaking.GetLobbyData(lobby, VersusDataKey));
            if (!ok)
                return;

            _joinTcs = new TaskCompletionSource<bool>();
            var call = SteamMatchmaking.JoinLobby(lobby);
            _lobbyEnter.Set(call, OnLobbyEntered);
            if (!await _joinTcs.Task.ConfigureAwait(true))
                throw new InvalidOperationException("Steam lobby join failed.");

            _lobbyId = lobby;
            SteamMatchmaking.SetLobbyMemberData(lobby, ReadyDataKey, "0");
            SessionChanged?.Invoke();
        }
        catch (Exception exception)
        {
            LobbyError?.Invoke(exception);
        }
    }

    private void OnLobbyEntered(LobbyEnter_t data, bool ioFailure)
    {
        if (_joinTcs is null)
            return;
        if (ioFailure || data.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            _joinTcs.TrySetResult(false);
            return;
        }

        _joinTcs.TrySetResult(true);
    }

    private static Task<bool> RefreshLobbyAsync(CSteamID lobby)
    {
        var tcs = new TaskCompletionSource<bool>();
        Callback<LobbyDataUpdate_t>? callback = null;
        callback = Callback<LobbyDataUpdate_t>.Create(update =>
        {
            if (update.m_ulSteamIDLobby != lobby.m_SteamID)
                return;
            callback?.Dispose();
            tcs.TrySetResult(update.m_bSuccess != 0);
        });

        if (!SteamMatchmaking.RequestLobbyData(lobby))
        {
            callback.Dispose();
            return Task.FromResult(false);
        }

        return tcs.Task;
    }

    private void OnLobbyChatUpdate(LobbyChatUpdate_t data)
    {
        if (_disposed || !HasLobby || data.m_ulSteamIDLobby != _lobbyId!.Value.m_SteamID)
            return;

        const uint leftFlags =
            (uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft |
            (uint)EChatMemberStateChange.k_EChatMemberStateChangeDisconnected |
            (uint)EChatMemberStateChange.k_EChatMemberStateChangeKicked |
            (uint)EChatMemberStateChange.k_EChatMemberStateChangeBanned;
        if ((data.m_rgfChatMemberStateChange & leftFlags) == 0)
            return;

        MemberLeft?.Invoke(data.m_ulSteamIDUserChanged);
        SessionChanged?.Invoke();
    }

    private CSteamID RequireLobby() =>
        HasLobby ? _lobbyId!.Value : throw new InvalidOperationException("No Steam lobby is active.");

    private static void RequireSteam()
    {
        if (!SteamSession.IsAttached())
            throw new InvalidOperationException("Steam is unavailable.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VersusLobby));
    }
}
