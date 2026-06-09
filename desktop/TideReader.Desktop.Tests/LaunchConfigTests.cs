using TideReader.Desktop;
using Xunit;

namespace TideReader.Desktop.Tests;

public sealed class LaunchConfigTests
{
    [Fact]
    public void Parse_DefaultsToStandaloneAndActivatesExistingInstance()
    {
        var config = LaunchConfig.Parse([]);

        Assert.False(config.ServiceMode);
        Assert.True(config.ShouldActivateExisting);
    }

    [Fact]
    public void Parse_ServiceFlagStartsServiceWithoutActivatingExistingInstance()
    {
        var config = LaunchConfig.Parse(["--service"]);

        Assert.True(config.ServiceMode);
        Assert.False(config.ShouldActivateExisting);
    }

    [Fact]
    public void Parse_ShowFlagOverridesServiceMode()
    {
        var config = LaunchConfig.Parse(["--service", "--show"]);

        Assert.False(config.ServiceMode);
        Assert.True(config.ShouldActivateExisting);
    }
}
