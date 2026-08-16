using System;
using Steamworks;

namespace SineusArenaVersus.Steam;

public sealed class SteamBootstrap : IDisposable
{
    public const uint AppId = 4227400;

    private readonly Action<Exception> _onError;
    private bool _ownsClient;
    private bool _disposed;

    public SteamBootstrap(Action<Exception>? onError = null)
    {
        _onError = onError ?? (_ => { });
    }

    public bool IsAvailable { get; private set; }

    public bool Initialize()
    {
        ThrowIfDisposed();
        if (IsAvailable)
            return true;

        try
        {
            if (!SteamClient.IsValid)
            {
                SteamClient.Init(AppId, asyncCallbacks: false);
                _ownsClient = true;
            }

            IsAvailable = SteamClient.IsValid;
            return IsAvailable;
        }
        catch (Exception exception)
        {
            _onError(exception);
            IsAvailable = false;
            return false;
        }
    }

    public void RunCallbacks()
    {
        if (_disposed || !IsAvailable || !SteamClient.IsValid)
            return;

        try
        {
            SteamClient.RunCallbacks();
        }
        catch (Exception exception)
        {
            _onError(exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (_ownsClient && SteamClient.IsValid)
            SteamClient.Shutdown();
        IsAvailable = false;
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SteamBootstrap));
    }
}
