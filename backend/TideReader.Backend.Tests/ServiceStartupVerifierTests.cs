using System.Net;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class ServiceStartupVerifierTests
{
    [Fact]
    public async Task WaitForReadyAsync_ReturnsReady_WhenBackendAndSipRespond()
    {
        using var client = new HttpClient(new StubHttpHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (path is "/api/health" or "/api/v1/app")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var verifier = new ServiceStartupVerifier(client);

        var readiness = await verifier.WaitForReadyAsync(
            new Uri("http://127.0.0.1:17656/api/health"),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.True(readiness.Ready);
        Assert.True(readiness.BackendReady);
        Assert.Equal(47030, readiness.SipPort);
    }

    [Fact]
    public async Task WaitForReadyAsync_ReturnsNotReady_WhenSipNeverResponds()
    {
        using var client = new HttpClient(new StubHttpHandler(request =>
            request.RequestUri?.AbsolutePath == "/api/health"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.NotFound)));
        var verifier = new ServiceStartupVerifier(client);

        var readiness = await verifier.WaitForReadyAsync(
            new Uri("http://127.0.0.1:17656/api/health"),
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.False(readiness.Ready);
        Assert.True(readiness.BackendReady);
        Assert.Null(readiness.SipPort);
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handle(request));
    }
}
