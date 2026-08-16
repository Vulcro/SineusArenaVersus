using SineusArenaVersus.Steam;
using Xunit;

namespace SineusArenaVersus.Tests;

public sealed class SteamBootstrapTests
{
    [Fact]
    public void Attached_client_does_not_pump_callbacks_or_shutdown()
    {
        var runtime = new FakeSteamRuntime { IsValid = true };
        using (var bootstrap = new SteamBootstrap(runtime))
        {
            Assert.True(bootstrap.Initialize());
            bootstrap.RunCallbacks();
        }

        Assert.Equal(0, runtime.InitCalls);
        Assert.Equal(0, runtime.CallbackCalls);
        Assert.Equal(0, runtime.ShutdownCalls);
    }

    [Fact]
    public void Owned_client_pumps_callbacks_and_shuts_down()
    {
        var runtime = new FakeSteamRuntime();
        using (var bootstrap = new SteamBootstrap(runtime))
        {
            Assert.True(bootstrap.Initialize());
            bootstrap.RunCallbacks();
        }

        Assert.Equal(1, runtime.InitCalls);
        Assert.Equal(1, runtime.CallbackCalls);
        Assert.Equal(1, runtime.ShutdownCalls);
    }

    private sealed class FakeSteamRuntime : ISteamRuntime
    {
        public bool IsValid { get; set; }
        public int InitCalls { get; private set; }
        public int CallbackCalls { get; private set; }
        public int ShutdownCalls { get; private set; }

        public void Init(uint appId, bool asyncCallbacks)
        {
            InitCalls++;
            IsValid = true;
        }

        public void RunCallbacks() => CallbackCalls++;

        public void Shutdown()
        {
            ShutdownCalls++;
            IsValid = false;
        }
    }
}
