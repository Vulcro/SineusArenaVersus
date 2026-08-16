using System;
using Steamworks;

namespace SineusArenaVersus.Steam;

/// <summary>
/// Attaches to the game's existing Steamworks.NET session (no second SteamAPI_Init).
/// </summary>
public sealed class SteamBootstrap : IDisposable
{
    public const uint AppId = 4227400;

    private readonly Action<Exception> _onError;
    private readonly Action<string>? _onInfo;
    private bool _disposed;
    private string? _lastFailReason;

    public SteamBootstrap(Action<Exception>? onError = null, Action<string>? onInfo = null)
    {
        _onError = onError ?? (_ => { });
        _onInfo = onInfo;
    }

    public bool IsAvailable { get; private set; }

    public bool Initialize()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SteamBootstrap));
        if (IsAvailable)
            return true;

        try
        {
            // Game owns SteamAPI.Init via Heathen/SteamTools — never call Init here.
            // IsSteamRunning() is unreliable here (always false under TMM while coop works).
            if (!SteamSession.TryGetLocalId(out var id))
            {
                var running = false;
                try
                {
                    running = SteamAPI.IsSteamRunning();
                }
                catch
                {
                    // ignored — diagnostic only
                }

                var reason =
                    $"SteamUser.GetSteamID() invalid (IsSteamRunning={running}). Waiting for game SteamTools init.";
                if (!string.Equals(_lastFailReason, reason, StringComparison.Ordinal))
                {
                    _lastFailReason = reason;
                    _onInfo?.Invoke(reason);
                }

                IsAvailable = false;
                return false;
            }

            IsAvailable = true;
            _lastFailReason = null;
            _onInfo?.Invoke($"Attached to Steamworks.NET as {id.m_SteamID}");
            return true;
        }
        catch (Exception exception)
        {
            var reason = exception.GetType().Name + ": " + exception.Message;
            if (!string.Equals(_lastFailReason, reason, StringComparison.Ordinal))
            {
                _lastFailReason = reason;
                _onError(exception);
            }

            IsAvailable = false;
            return false;
        }
    }

    public void RunCallbacks()
    {
        // Game already pumps Steamworks.NET callbacks; extra RunCallbacks is safe.
        if (_disposed || !IsAvailable)
            return;

        try
        {
            SteamAPI.RunCallbacks();
        }
        catch (Exception exception)
        {
            _onError(exception);
        }
    }

    public void Dispose()
    {
        // Do not SteamAPI.Shutdown — the game owns the session.
        IsAvailable = false;
        _disposed = true;
    }

    public static ulong LocalSteamId() => SteamSession.LocalIdOrZero();
}
