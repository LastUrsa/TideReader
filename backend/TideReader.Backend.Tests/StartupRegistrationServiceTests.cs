using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class StartupRegistrationServiceTests
{
    [Fact]
    public void Sync_Disabled_Deletes_RunKeyValue()
    {
        var key = new FakeRunKey();
        var service = new StartupRegistrationService(new FakeRunKeyFactory(key));

        service.Sync(false);

        Assert.Equal("TideReader", key.DeletedName);
        Assert.False(key.SetCalled);
    }

    [Fact]
    public void Sync_NoKey_DoesNothing()
    {
        var service = new StartupRegistrationService(new NullRunKeyFactory());

        service.Sync(true);
    }

    [Fact]
    public void Sync_Enabled_SetsQuotedExecutablePath()
    {
        var key = new FakeRunKey();
        var service = new StartupRegistrationService(new FakeRunKeyFactory(key));

        service.Sync(true);

        Assert.True(key.SetCalled);
        Assert.Equal("TideReader", key.SetName);
        Assert.StartsWith("\"", key.SetValueText, StringComparison.Ordinal);
        Assert.EndsWith("\"", key.SetValueText, StringComparison.Ordinal);
    }

    private sealed class FakeRunKeyFactory(FakeRunKey key) : IRegistryRunKeyFactory
    {
        public IRegistryRunKey? OpenCurrentUserRunKey() => key;
    }

    private sealed class NullRunKeyFactory : IRegistryRunKeyFactory
    {
        public IRegistryRunKey? OpenCurrentUserRunKey() => null;
    }

    private sealed class FakeRunKey : IRegistryRunKey
    {
        public string DeletedName { get; private set; } = "";
        public bool SetCalled { get; private set; }
        public string SetName { get; private set; } = "";
        public string SetValueText { get; private set; } = "";

        public void DeleteValue(string name, bool throwOnMissingValue) => DeletedName = name;
        public void SetValue(string name, string value)
        {
            SetCalled = true;
            SetName = name;
            SetValueText = value;
        }
    }
}
