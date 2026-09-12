using System.Text.Json;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

public sealed class FabWebLibraryClientTests
{
    private const string Item = """
    {"uid":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","source":"acquired","canRequestDownloadUrl":true,
    "entitlement":{"licenses":[]},"createdAt":"2026-09-12T12:00:00Z",
    "listing":{"uid":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb","title":"Acacia","isActiveInLive":true,
    "listingType":"3d-model","publisher":{"sellerName":"Quixel Megascans"},
    "assetFormats":[{"assetFormatType":{"name":"FBX"},"technicalSpecs":{"technicalDetails":"<p>Mesh &amp; textures</p>"}}],
    "thumbnails":[{"images":[{"url":"https://media.fab.com/a.jpg","width":144},{"url":"https://media.fab.com/b.jpg","width":640}]}]}}
    """;

    [Fact]
    public void ReadsOwnedMetadataWithoutInventingUnrealArtifacts()
    {
        using var json = JsonDocument.Parse(Item);
        var asset = FabWebLibraryClient.ParseAcquiredItem(json.RootElement)!;
        Assert.Equal("Acacia", asset.Title);
        Assert.Equal("FabWeb", asset.Source);
        Assert.Contains("Mesh & textures", asset.Description);
        Assert.Equal("https://media.fab.com/b.jpg", asset.ThumbnailUri!.AbsoluteUri);
        Assert.Contains("FBX", asset.Categories);
        Assert.True(asset.WebCanDownload);
        Assert.Empty(asset.ProjectVersions);
        Assert.Equal("https://www.fab.com/listings/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", asset.ListingUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("acquired", "catalog")]
    [InlineData("Quixel Megascans", "Other seller")]
    public void RejectsCatalogResultsOrOtherPublishers(string before, string after)
    {
        using var json = JsonDocument.Parse(Item.Replace(before, after, StringComparison.Ordinal));
        Assert.Null(FabWebLibraryClient.ParseAcquiredItem(json.RootElement));
    }

    [Fact]
    public async Task FollowsCursorAndDeduplicates()
    {
        var paths = new List<string>();
        var client = new FabWebLibraryClient((path, _) =>
        {
            paths.Add(path);
            return Task.FromResult("{\"results\":[" + Item + "],\"cursors\":{\"next\":" + (paths.Count == 1 ? "\"a==\"" : "null") + "}}");
        });
        Assert.Single(await client.SynchronizeAsync());
        Assert.Equal(2, paths.Count);
        Assert.EndsWith("&cursor=a%3D%3D", paths[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepeatedCursorOrLoginPageFailsInsteadOfReplacingCacheWithEmptySuccess()
    {
        var client = new FabWebLibraryClient((_, _) => Task.FromResult("{\"results\":[],\"cursors\":{\"next\":\"same\"}}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.SynchronizeAsync());
        var login = new FabWebLibraryClient((_, _) => Task.FromResult("{}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => login.SynchronizeAsync());
    }

    [Theory]
    [InlineData("/i/users/me")]
    [InlineData("https://evil.example/i/library/search")]
    [InlineData("/i/library/search?sort_by=-createdAt&source=acquired&cursor=x&other=true")]
    public void TransportRejectsAnythingOutsideLibraryPagination(string path) => Assert.False(FabWebLibraryClient.IsAllowedRequest(path));

    [Fact]
    public void AllowsOnlyObservedLibraryRouteAndEncodedCursor()
    {
        Assert.True(FabWebLibraryClient.IsAllowedRequest(FabWebLibraryClient.FirstPage));
        Assert.True(FabWebLibraryClient.IsAllowedRequest(FabWebLibraryClient.FirstPage + "&cursor=a%3D%3D"));
    }
}
