using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SineusArenaVersus.Net;
using Steamworks;
using Steamworks.Data;
using SteamLobby = Steamworks.Data.Lobby;

namespace SineusArenaVersus.Lobby;

public sealed class VersusLobby : IDisposable
{
    public const string VersusDataKey = "versus";
    public const string ReadyDataKey = "ready";

    private readonly Func<int> _maxPlayers;
    private readonly Func<float> _waveInterval;
    private readonly Func<VersusNet?> _net;
    private SteamLobby? _lobby;
    private bool _disposed;

    public VersusLobby(
        Func<int> maxPlayers,
        Func<float> waveInterval,
        Func<VersusNet?> net)
    {
        _maxPlayers = maxPlayers ?? throw new ArgumentNullException(nameof(maxPlayers));
        _waveInterval = waveInterval ?? throw new ArgumentNullException(nameof(waveInterval));
        _net = net ?? throw new ArgumentNullException(nameof(net));
        SteamFriends.OnGameLobbyJoinRequested += HandleJoinRequested;
        SteamMatchmaking.OnLobbyMemberLeave += HandleMemberLeave;
    }

    public event Action? SessionChanged;
    public event Action<ulong>? MemberLeft;
    public event Action<Exception>? LobbyError;

    public bool HasLobby => _lobby.HasValue;
    public ulong LobbyId => _lobby?.Id ?? 0UL;
    public ulong HostPeerId => _lobby?.Owner.Id ?? 0UL;
    public bool IsLocalHost => _lobby?.IsOwnedBy(SteamClient.SteamId) == true;
    public bool IsLocalReady => _lobby.HasValue &&
                                _lobby.Value.GetMemberData(
                                    new Friend(SteamClient.SteamId),
                                    ReadyDataKey) == "1";
    public IReadOnlyList<ulong> Members =>
        _lobby?.Members.Select(member => (ulong)member.Id).ToArray() ?? Array.Empty<ulong>();

    public async Task HostLobbyAsync()
    {
        ThrowIfDisposed();
        RequireSteam();
        if (_lobby.HasValue)
            throw new InvalidOperationException("Already in a Steam lobby.");

        var maxPlayers = _maxPlayers();
        if (maxPlayers < 2 || maxPlayers > 4)
            throw new InvalidOperationException("Versus lobby size must be between 2 and 4.");

        var lobby = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);
        if (!lobby.HasValue)
            throw new InvalidOperationException("Steam did not create the Versus lobby.");

        _lobby = lobby.Value;
        _lobby.Value.SetFriendsOnly();
        _lobby.Value.SetJoinable(true);
        if (!_lobby.Value.SetData(VersusDataKey, "1"))
            throw new InvalidOperationException("Steam rejected Versus lobby metadata.");
        _lobby.Value.SetMemberData(ReadyDataKey, "0");
        SessionChanged?.Invoke();
    }

    public void InviteFriend(SteamId id)
    {
        ThrowIfDisposed();
        var lobby = RequireLobby();
        if (!lobby.InviteFriend(id))
            throw new InvalidOperationException($"Steam rejected the lobby invite for {id}.");
    }

    public void OpenInviteOverlay()
    {
        ThrowIfDisposed();
        SteamFriends.OpenGameInviteOverlay(RequireLobby().Id);
    }

    public bool IsMemberReady(ulong peerId)
    {
        var lobby = RequireLobby();
        return lobby.GetMemberData(new Friend(peerId), ReadyDataKey) == "1";
    }

    public void SetReady(bool ready)
    {
        ThrowIfDisposed();
        var lobby = RequireLobby();
        lobby.SetMemberData(ReadyDataKey, ready ? "1" : "0");
    }

    public void StartMatchAsHost()
    {
        ThrowIfDisposed();
        var lobby = RequireLobby();
        if (!lobby.IsOwnedBy(SteamClient.SteamId))
            throw new InvalidOperationException("Only the lobby owner can start Versus.");

        var members = lobby.Members.ToArray();
        if (members.Length < 2 || members.Length > _maxPlayers())
            throw new InvalidOperationException("Versus requires 2-4 lobby members.");
        if (members.Any(member => lobby.GetMemberData(member, ReadyDataKey) != "1"))
            throw new InvalidOperationException("Every lobby member must be ready.");

        var net = _net() ?? throw new InvalidOperationException("Versus networking is not attached.");
        net.StartMatchAsHost(
            lobby.Id,
            members.Select(member => (ulong)member.Id).ToArray(),
            _waveInterval());
    }

    public bool ContainsPeer(ulong peerId) =>
        _lobby?.Members.Any(member => member.Id == peerId) == true;

    public void Dispose()
    {
        if (_disposed)
            return;
        SteamFriends.OnGameLobbyJoinRequested -= HandleJoinRequested;
        SteamMatchmaking.OnLobbyMemberLeave -= HandleMemberLeave;
        if (_lobby.HasValue)
            _lobby.Value.Leave();
        _lobby = null;
        _disposed = true;
    }

    private async void HandleJoinRequested(SteamLobby lobby, SteamId friendId)
    {
        try
        {
            if (_disposed)
                return;
            var result = await lobby.Join();
            if (result != RoomEnter.Success)
                throw new InvalidOperationException($"Steam lobby join failed: {result}.");

            _lobby = lobby;
            _lobby.Value.SetMemberData(ReadyDataKey, "0");
            SessionChanged?.Invoke();
        }
        catch (Exception exception)
        {
            LobbyError?.Invoke(exception);
        }
    }

    private void HandleMemberLeave(SteamLobby lobby, Friend member)
    {
        if (_disposed || !_lobby.HasValue || lobby.Id != _lobby.Value.Id)
            return;

        MemberLeft?.Invoke(member.Id);
    }

    private SteamLobby RequireLobby() =>
        _lobby ?? throw new InvalidOperationException("No Steam lobby is active.");

    private static void RequireSteam()
    {
        if (!SteamClient.IsValid)
            throw new InvalidOperationException("Steam is unavailable.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VersusLobby));
    }
}
