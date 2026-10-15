using System.Net;
using System.Text;
using System.Text.Json;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

public sealed class QuixelCollectionsTests
{
    [Fact]
    public void QuixelLoginHasExactCallbackCompatibleWithEmbeddedCapture()
    {
        using var http = new HttpClient();
        var client = new QuixelMegascansApiClient(http, new ForbiddenSessionStore(), new ForbiddenSessionStore());
        var reader = new EpicAuthorizationResponse(client.AuthorizationUri);
        var query = client.AuthorizationUri.Query;
        var callback = Uri.UnescapeDataString(query[(query.IndexOf('=') + 1)..]);
        Assert.True(reader.IsExpectedCallback(new Uri(callback)));
        Assert.False(reader.IsExpectedCallback(new Uri("https://www.epicgames.com/id/api/redirect?clientId=other&responseType=code")));
    }
    [Fact]
    public async Task ReadsAllPublicPagesWithoutAccountAndSkipsHiddenCollections()
    {
        using var handler = new FixtureHandler(async request =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("unit-test-catalog-key", Assert.Single(request.Headers.GetValues("X-Algolia-API-Key")));
            Assert.EndsWith("/collections/query", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            return body.RootElement.GetProperty("page").GetInt32() == 0 ? """
                {"nbPages":2,"hits":[
                {"objectID":"forest","name":"Forest","collectionPath":"Environment | Natural","presentation":{"coverImages":["megascans/pack-banners/forest.jpg"]}},
                {"objectID":"hidden","name":"x - Hidden","collectionPath":"Environment"}]}
                """ : """
                {"nbPages":2,"hits":[
                {"objectID":"forest","name":"Forest","collectionPath":"Environment"},
                {"objectID":"archviz","name":"Architecture","collectionPath":"Archviz"}]}
                """;
        });
        using var http = new HttpClient(handler);
        var client = new QuixelMegascansApiClient(http, new ForbiddenSessionStore(), new ForbiddenSessionStore(), algoliaSearchKey: "unit-test-catalog-key");
        var collections = await client.GetCollectionsAsync();
        Assert.Equal(2, handler.Calls);
        Assert.Equal(["Architecture", "Forest"], collections.Select(item => item.Name));
        var forest = collections[1];
        Assert.Equal("https://d3uwib8iif8w1p.cloudfront.net/megascans/pack-banners/forest.jpg", forest.ThumbnailUri!.AbsoluteUri);
        Assert.Null(forest.AssetIds);
        Assert.Contains("category=natural", forest.ListingUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsOfficialMembershipAndThumbnailVariant()
    {
        using var handler = new FixtureHandler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("https://quixel.com/v1/packs/forest", request.RequestUri!.AbsoluteUri);
            return Task.FromResult("""
                {"id":"forest","name":"Forest","collectionPath":"Environment | Natural",
                 "assets":["asset1","ASSET1","asset2","../invalid"],
                 "presentation":{"subtitle":"Trees and rocks","coverImages":["megascans/pack-banners/forest.jpg"],"coverImageRes":["_100","_644"]}}
                """);
        });
        using var http = new HttpClient(handler);
        var collection = await new QuixelMegascansApiClient(http, new ForbiddenSessionStore(), new ForbiddenSessionStore(), algoliaSearchKey: "unit-test-catalog-key").GetCollectionAsync("forest");
        Assert.Equal(["asset1", "asset2"], collection.AssetIds);
        Assert.Equal("Trees and rocks", collection.Description);
        Assert.EndsWith("forest_644.jpg", collection.ThumbnailUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../forest")]
    [InlineData("forest?query=1")]
    [InlineData("https://evil.test/")]
    [InlineData("")]
    public async Task RejectsUnsafeIdentifiersBeforeNetworkAccess(string id)
    {
        using var handler = new FixtureHandler(_ => throw new InvalidOperationException());
        using var http = new HttpClient(handler);
        var client = new QuixelMegascansApiClient(http, new ForbiddenSessionStore(), new ForbiddenSessionStore(), algoliaSearchKey: "unit-test-catalog-key");
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetCollectionAsync(id));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"nbPages\":1}")]
    [InlineData("{\"nbPages\":51,\"hits\":[]}")]
    [InlineData("{\"nbPages\":2,\"hits\":[]}")]
    public async Task RejectsInvalidOrTruncatedCatalog(string json)
    {
        using var handler = new FixtureHandler(_ => Task.FromResult(json));
        using var http = new HttpClient(handler);
        var client = new QuixelMegascansApiClient(http, new ForbiddenSessionStore(), new ForbiddenSessionStore(), algoliaSearchKey: "unit-test-catalog-key");
        await Assert.ThrowsAsync<QuixelException>(() => client.GetCollectionsAsync());
    }

    [Fact]
    public async Task RejectsMismatchedDetailsAndUnsafeImage()
    {
        using var handler = new FixtureHandler(_ => Task.FromResult("""
            {"id":"different","name":"Forest","assets":[],"presentation":{"coverImages":[],"splashImage":"https://evil.test/image.jpg"}}
            """));
        using var http = new HttpClient(handler);
        var client = new QuixelMegascansApiClient(http, new ForbiddenSessionStore(), new ForbiddenSessionStore(), algoliaSearchKey: "unit-test-catalog-key");
        await Assert.ThrowsAsync<QuixelException>(() => client.GetCollectionAsync("forest"));
        var empty = await client.GetCollectionAsync("different");
        Assert.Null(empty.ThumbnailUri);
        Assert.Empty(empty.AssetIds!);
    }

    [Fact]
    public async Task CacheRoundTripsVisitedCollectionsAndKeepsOldDataOnCanceledSave()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CyHub-Collections-Test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "collections.json.gz");
        try
        {
            var cache = new QuixelCollectionsCache(path);
            Assert.Null(await cache.LoadAsync());
            var snapshot = new QuixelCollectionsSnapshot(DateTimeOffset.UtcNow,
                [new QuixelCollection("forest", "Forest", "Environment", "Trees", null, new Uri("https://quixel.com/megascans/collections"), ["asset1", "asset2"])]);
            await cache.SaveAsync(snapshot);
            var loaded = await new QuixelCollectionsCache(path).LoadAsync();
            Assert.Equal(snapshot.UpdatedAt, loaded!.UpdatedAt);
            Assert.Equal(["asset1", "asset2"], Assert.Single(loaded.Collections).AssetIds);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.SaveAsync(new QuixelCollectionsSnapshot(DateTimeOffset.UtcNow, []), cancellation.Token));
            Assert.Single((await cache.LoadAsync())!.Collections);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.partial"));
            await File.WriteAllTextAsync(path, "invalid cache");
            Assert.Null(await cache.LoadAsync());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingCatalogKeyDisablesSearchBeforeNetworkOrSessionAccess(string configuredKey)
    {
        using var handler = new FixtureHandler(_ => throw new InvalidOperationException("Network access must remain disabled."));
        using var http = new HttpClient(handler);
        var client = new QuixelMegascansApiClient(http, new ForbiddenSessionStore(), new ForbiddenSessionStore(), configuredKey);
        var collectionsError = await Assert.ThrowsAsync<QuixelException>(() => client.GetCollectionsAsync());
        var libraryError = await Assert.ThrowsAsync<QuixelException>(() => client.GetLibraryAsync());
        Assert.Contains("désactivée", collectionsError.Message, StringComparison.Ordinal);
        Assert.Contains("désactivée", libraryError.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Calls);
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage, Task<string>> reply) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(await reply(request), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ForbiddenSessionStore : IQuixelSessionStore, IDirectFabSessionStore
    {
        Task<QuixelSession?> IQuixelSessionStore.LoadAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Public collections must not read sessions.");
        Task<DirectFabSession?> IDirectFabSessionStore.LoadAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Public collections must not read sessions.");
        public Task SaveAsync(QuixelSession session, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task SaveAsync(DirectFabSession session, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task ClearAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
