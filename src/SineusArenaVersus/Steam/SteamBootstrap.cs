using System;
using Steamworks;

namespace SineusArenaVersus.Steam;

internal interface ISteamRuntime
{
    bool IsValid { get; }
    void Init(uint appId, bool asyncCallbacks);
    void RunCallbacks();
    void Shutdown();
}

public sealed class SteamBootstrap : IDisposable
{
    public const uint AppId = 4227400;

    private readonly ISteamRuntime _runtime;
    private readonly Action<Exception> _onError;
    private bool _ownsClient;
    private bool _ownsCallbackPump;
    private bool _disposed;

    public SteamBootstrap(Action<Exception>? onError = null)
        : this(new FacepunchSteamRuntime(), onError)
    {
    }

    internal SteamBootstrap(ISteamRuntime runtime, Action<Exception>? onError = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
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
            if (!_runtime.IsValid)
            {
                _runtime.Init(AppId, asyncCallbacks: false);
                _ownsClient = true;
                _ownsCallbackPump = true;
            }

            IsAvailable = _runtime.IsValid;
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
        if (_disposed || !_ownsCallbackPump || !IsAvailable || !_runtime.IsValid)
            return;

        try
        {
            _runtime.RunCallbacks();
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
        if (_ownsClient && _runtime.IsValid)
            _runtime.Shutdown();
        IsAvailable = false;
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SteamBootstrap));
    }

    private sealed class FacepunchSteamRuntime : ISteamRuntime
    {
        public bool IsValid => SteamClient.IsValid;

        public void Init(uint appId, bool asyncCallbacks) =>
            SteamClient.Init(appId, asyncCallbacks);

        public void RunCallbacks() => SteamClient.RunCallbacks();

        public void Shutdown() => SteamClient.Shutdown();
    }
}
