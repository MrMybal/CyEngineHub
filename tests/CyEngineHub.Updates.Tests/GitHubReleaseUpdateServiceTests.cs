using System.Net;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace CyEngineHub.Updates.Tests;

public sealed class GitHubReleaseUpdateServiceTests
{
    [Fact]
    public async Task CheckAndDownloadAcceptsNewerStableReleaseWithMatchingDigest()
    {
        var installerBytes = Encoding.UTF8.GetBytes("verified CyEngineHub installer");
        var sha256 = Convert.ToHexString(SHA256.HashData(installerBytes)).ToLowerInvariant();
        using var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Host == "api.github.com")
            {
                return JsonResponse(BuildReleaseJson("v1.3.0", sha256));
            }

            return ByteResponse(installerBytes);
        });
        using var httpClient = new HttpClient(handler);
        var service = new GitHubReleaseUpdateService(httpClient);
        using var temporary = new TemporaryDirectory();

        var release = await service.CheckForUpdateAsync(
            "https://github.com/CyberAlien/CyEngineHub",
            new Version(1, 2, 0));

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 3, 0, 0), release.Version);
        Assert.True(release.CanInstall);
        var result = await service.DownloadAndVerifyAsync(release, temporary.Path);
        Assert.Equal(sha256, result.Sha256);
        Assert.Equal(installerBytes, await File.ReadAllBytesAsync(result.InstallerPath));
    }

    [Fact]
    public async Task CheckReturnsNullWhenLatestReleaseIsNotNewer()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            JsonResponse(BuildReleaseJson("v1.2.0", new string('a', 64))));
        using var httpClient = new HttpClient(handler);
        var service = new GitHubReleaseUpdateService(httpClient);

        var release = await service.CheckForUpdateAsync("CyberAlien/CyEngineHub", new Version(1, 2, 0));

        Assert.Null(release);
    }

    [Fact]
    public async Task DownloadRejectsInvalidDigestAndRemovesPartialFile()
    {
        var installerBytes = Encoding.UTF8.GetBytes("tampered installer");
        using var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Host == "api.github.com")
            {
                return JsonResponse(BuildReleaseJson("v2.0.0", new string('0', 64)));
            }

            return ByteResponse(installerBytes);
        });
        using var httpClient = new HttpClient(handler);
        var service = new GitHubReleaseUpdateService(httpClient);
        using var temporary = new TemporaryDirectory();
        var release = await service.CheckForUpdateAsync("CyberAlien/CyEngineHub", new Version(1, 0, 0));
        Assert.NotNull(release);

        var exception = await Assert.ThrowsAsync<ApplicationUpdateException>(() =>
            service.DownloadAndVerifyAsync(release, temporary.Path));

        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(temporary.Path));
    }

    [Theory]
    [InlineData("v0.1.0-alpha.2", "0.1.0-alpha.1", true)]
    [InlineData("v0.1.0-alpha.10", "0.1.0-alpha.2", true)]
    [InlineData("v0.1.0-alpha.2", "0.1.0-alpha.10", false)]
    [InlineData("v0.1.0-alpha.1", "0.1.0-alpha.1+commit", false)]
    [InlineData("v0.1.0", "0.1.0-alpha.10", true)]
    [InlineData("v0.1.0-alpha.10", "0.1.0", false)]
    [InlineData("v0.1.0-beta.1", "0.1.0-alpha.10", true)]
    public async Task PrereleaseChannelUsesSemanticVersionOrder(string tag, string current, bool newer)
    {
        using var handler = new StubHttpMessageHandler(request =>
        {
            Assert.EndsWith("/releases?per_page=100", request.RequestUri!.ToString(), StringComparison.Ordinal);
            return JsonResponse("[" + BuildReleaseJson(tag, new string('a', 64), tag.Contains('-')) + "]");
        });
        using var client = new HttpClient(handler);
        var release = await new GitHubReleaseUpdateService(client)
            .CheckForUpdateAsync("MrMybal/CyEngineHub", current, true);
        Assert.Equal(newer, release is not null);
    }

    [Fact]
    public async Task PrereleaseChannelSelectsHighestVersionAndSkipsDraftsAndUnrelatedTags()
    {
        var hash = new string('a', 64);
        var json = "[" + string.Join(',',
            BuildReleaseJson("v0.2.0-alpha.1", hash, true).Replace("\"draft\": false", "\"draft\": true", StringComparison.Ordinal),
            BuildReleaseJson("nightly", hash, true),
            BuildReleaseJson("v0.1.0-alpha.10", hash, true),
            BuildReleaseJson("v0.1.0-alpha.2", hash, true)) + "]";
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(json));
        using var client = new HttpClient(handler);
        var release = await new GitHubReleaseUpdateService(client)
            .CheckForUpdateAsync("MrMybal/CyEngineHub", "0.1.0-alpha.1", true);
        Assert.Equal("v0.1.0-alpha.10", release?.TagName);
    }

    [Fact]
    public async Task StableChannelDoesNotOfferAnAlpha()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            JsonResponse(BuildReleaseJson("v2.0.0-alpha.1", new string('a', 64), true)));
        using var client = new HttpClient(handler);
        Assert.Null(await new GitHubReleaseUpdateService(client)
            .CheckForUpdateAsync("MrMybal/CyEngineHub", "0.1.0-alpha.1", false));
    }

    [Fact]
    public async Task RepositoryWithoutStableReleaseIsNotAnError()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        Assert.Null(await new GitHubReleaseUpdateService(client)
            .CheckForUpdateAsync("MrMybal/CyEngineHub", "0.1.0-alpha.1", false));
    }

    private static string BuildReleaseJson(string tag, string sha256, bool prerelease = false) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "CyEngineHub {{tag}}",
          "body": "Notes de version de test",
          "html_url": "https://github.com/CyberAlien/CyEngineHub/releases/tag/{{tag}}",
          "published_at": "2026-08-29T10:00:00Z",
          "draft": false,
          "prerelease": {{prerelease.ToString().ToLowerInvariant()}},
          "assets": [
            {
              "name": "CyEngineHub-Setup-x64.exe",
              "browser_download_url": "https://github.com/CyberAlien/CyEngineHub/releases/download/{{tag}}/CyEngineHub-Setup-x64.exe",
              "size": 28,
              "digest": "sha256:{{sha256}}"
            }
          ]
        }
        """;

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage ByteResponse(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes),
    };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CyEngineHub-UpdateTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
