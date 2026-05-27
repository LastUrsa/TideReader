using System.Net;
using System.Net.Http;
using System.Text;
using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class AppUpdateCheckerTests
{
    [Theory]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData(" 0.2.0-beta+abc ", "0.2.0")]
    [InlineData(null, "")]
    public void NormalizeVersion_StripsPrefixesAndSuffixes(string? input, string expected)
    {
        Assert.Equal(expected, AppUpdateChecker.NormalizeVersion(input));
    }

    [Theory]
    [InlineData("0.2.1", "0.2.0", 1)]
    [InlineData("0.2.0", "0.2.1", -1)]
    [InlineData("0.2", "0.2.0", 0)]
    [InlineData("0.2.alpha", "0.2.0", 0)]
    public void CompareVersions_HandlesCommonVersionShapes(string left, string right, int expected)
    {
        Assert.Equal(expected, AppUpdateChecker.CompareVersions(left, right));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsUpdateAvailable_WhenLatestTagIsNewer()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"tagName\":\"v9.9.9\"}", Encoding.UTF8, "application/json")
        });
        var checker = new AppUpdateChecker(new HttpClient(handler));

        var result = await checker.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal(checker.CurrentVersion, result.CurrentVersion);
        Assert.Equal("9.9.9", result.LatestVersion);
        Assert.True(result.UpdateAvailable);
        Assert.Equal("https://github.com/LastUrsa/TideReader/releases", result.ReleaseUrl);
        Assert.Equal("Version 9.9.9 is available.", result.Message);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_FallsBackToCurrentVersion_WhenTagIsBlank()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"tagName\":\"\"}", Encoding.UTF8, "application/json")
        });
        var checker = new AppUpdateChecker(new HttpClient(handler));

        var result = await checker.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal(checker.CurrentVersion, result.LatestVersion);
        Assert.False(result.UpdateAvailable);
        Assert.Equal("You're running the latest version.", result.Message);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ThrowsForNonSuccessResponses()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            ReasonPhrase = "Bad Gateway"
        });
        var checker = new AppUpdateChecker(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() => checker.CheckForUpdatesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ThrowsForEmptyResponses()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        });
        var checker = new AppUpdateChecker(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() => checker.CheckForUpdatesAsync(CancellationToken.None));
    }

    [Fact]
    public void ExternalUrlLauncher_RejectsNonHttpUrls()
    {
        var launcher = new ExternalUrlLauncher();

        Assert.Throws<ArgumentException>(() => launcher.OpenUrl("file:///test"));
        Assert.Throws<ArgumentException>(() => launcher.OpenUrl("/relative"));
    }

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("application/vnd.github+json", request.Headers.Accept.Select(header => header.MediaType));
            Assert.Contains("TideReader/", request.Headers.UserAgent.ToString());
            return Task.FromResult(response);
        }
    }
}
