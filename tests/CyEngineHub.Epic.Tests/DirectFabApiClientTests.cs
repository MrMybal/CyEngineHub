using System.Net;
using System.Text;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

public sealed class DirectFabApiClientTests
{
    [Fact]
    public async Task ConnectsWithAuthorizationJsonAndReadsLibrary()
    {
        var store = new MemorySessionStore();
        using var handler = new FabHttpHandler();
        using var http = new HttpClient(handler);
        using var client = new DirectFabApiClient(http, store);

        var status = await client.ConnectAsync("{\"authorizationCode\":\"authorization-code-123\"}");
        var assets = await client.GetLibraryAsync();

        Assert.Equal(DirectFabConnectionState.Connected, status.State);
        Assert.Equal("CyTester", status.DisplayName);
        var asset = Assert.Single(assets);
        Assert.Equal("Environment Pack", asset.Title);
        Assert.Equal("Portable environment", asset.Description);
        Assert.Contains("<strong>", asset.RichDescription);
        var details = await client.GetProductDetailsAsync(asset);
        Assert.Equal("Full details & data\n\nDocs (https://example.invalid/docs)", details.Description);
        Assert.Contains("<a href=", details.RichDescription);
        Assert.Equal("artifact-1", asset.PreferredProjectVersion?.ArtifactId);
        Assert.Equal("5.5", Assert.Single(asset.PreferredProjectVersion!.EngineVersions));
        Assert.True(handler.SawAuthorizationCode);
        Assert.True(handler.SawBearerToken);
        Assert.NotNull(store.Session);
    }

    [Fact]
    public async Task ReportsDisconnectedWithoutStoredSession()
    {
        using var http = new HttpClient(new FabHttpHandler());
        using var client = new DirectFabApiClient(http, new MemorySessionStore());

        var status = await client.GetStatusAsync();

        Assert.Equal(DirectFabConnectionState.Disconnected, status.State);
    }

    private sealed class MemorySessionStore : IDirectFabSessionStore
    {
        public DirectFabSession? Session { get; private set; }

        public Task<DirectFabSession?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Session);

        public Task SaveAsync(DirectFabSession session, CancellationToken cancellationToken = default)
        {
            Session = session;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Session = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FabHttpHandler : HttpMessageHandler
    {
        public bool SawAuthorizationCode { get; private set; }

        public bool SawBearerToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host == "account-public-service-prod03.ol.epicgames.com")
            {
                var form = await request.Content!.ReadAsStringAsync(cancellationToken);
                SawAuthorizationCode = form.Contains(
                    "code=authorization-code-123",
                    StringComparison.Ordinal);
                return Json(HttpStatusCode.OK, """
                    {
                      "access_token": "access-token",
                      "expires_in": 28800,
                      "refresh_token": "refresh-token",
                      "refresh_expires": 2592000,
                      "account_id": "account-1",
                      "displayName": "CyTester"
                    }
                    """);
            }

            if (request.RequestUri?.Host == "www.fab.com" &&
                request.RequestUri.AbsolutePath.EndsWith("/ue/library", StringComparison.Ordinal))
            {
                SawBearerToken = string.Equals(
                    request.Headers.Authorization?.Parameter,
                    "access-token",
                    StringComparison.Ordinal);
                return Json(HttpStatusCode.OK, """
                    {
                      "cursors": { "next": null },
                      "results": [
                        {
                          "assetId": "asset-1",
                          "assetNamespace": "ue",
                          "categories": [{ "id": "3d", "name": "3D" }],
                          "description": "<p>Portable <strong>environment</strong></p>",
                          "distributionMethod": "assetPack",
                          "images": [],
                          "projectVersions": [{
                            "artifactId": "artifact-1",
                            "buildVersions": [{ "buildVersion": "1", "platform": "Windows" }],
                            "engineVersions": ["5.5"],
                            "targetPlatforms": ["Windows"]
                          }],
                          "source": "Fab",
                          "title": "Environment Pack",
                          "url": "/listings/asset-1"
                        }
                      ]
                    }
                    """);
            }

            if (request.RequestUri?.Host == "www.fab.com" && request.RequestUri.AbsolutePath == "/i/listings/asset-1")
                return Json(HttpStatusCode.OK, """
                    { "description": "<p>Full details &amp; data</p><p><a href='https://example.invalid/docs'>Docs</a></p>" }
                    """);
            return Json(HttpStatusCode.NotFound, "{}");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string content) =>
            new(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
    }
}
