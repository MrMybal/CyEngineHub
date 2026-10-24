using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyEngineHub.Epic.Acquisition;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

#pragma warning disable CA5350 // SHA-1 is required by the Epic manifest format, not used for credentials.

// Real downloader, parser, reconstruction and integrity checks; no socket or real engine payload.
public sealed class EpicEngineDownloadPipelineTests
{
    [Theory]
    [InlineData("update-success")]
    [InlineData("update-current")]
    [InlineData("update-cancel")]
    [InlineData("update-locked")]
    [InlineData("update-corrupt")]
    [InlineData("update-branch")]
    [InlineData("update-git")]
    [InlineData("update-downgrade")]
    [InlineData("update-fail-commit")]
    [InlineData("update-traversal")]
    [InlineData("update-recover")]
    public async Task UpdateInPlaceDownloadsOnlyChangedContentAndRollsBackFailures(string scenario)
    {
        var root = Path.Combine(Path.GetTempPath(), "CyHubEngineUpdateQa-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "Engine");
        var buildPath = Path.Combine(target, "Engine", "Build", "Build.version");
        var dllPath = Path.Combine(target, "Engine", "Binaries", "Win64", "Test.dll");
        var unchangedPath = Path.Combine(target, "Engine", "Content", "Unchanged.txt");
        var customPath = Path.Combine(target, "Engine", "Plugins", "Custom", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(buildPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dllPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(unchangedPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(customPath)!);
        var originalBuild = scenario == "update-current" ? FixtureHandler.Payload : Encoding.UTF8.GetBytes(
            scenario == "update-branch" ? "{\"MajorVersion\":5,\"MinorVersion\":5,\"PatchVersion\":0}" :
            scenario == "update-downgrade" ? "{\"MajorVersion\":5,\"MinorVersion\":6,\"PatchVersion\":2}" : "{\"MajorVersion\":5,\"MinorVersion\":6,\"PatchVersion\":0}");
        var originalDll = scenario == "update-current" ? FixtureHandler.Payload : Encoding.UTF8.GetBytes("old DLL");
        await File.WriteAllBytesAsync(buildPath, originalBuild);
        await File.WriteAllBytesAsync(dllPath, originalDll);
        await File.WriteAllBytesAsync(unchangedPath, scenario == "update-corrupt" ? Encoding.UTF8.GetBytes("no reusable local bytes") : FixtureHandler.Payload);
        await File.WriteAllTextAsync(customPath, "keep custom plugins");
        var oldTime = DateTime.UtcNow.AddDays(-5);
        File.SetLastWriteTimeUtc(unchangedPath, oldTime);
        if (scenario == "update-git") Directory.CreateDirectory(Path.Combine(target, ".git"));
        if (scenario == "update-recover")
        {
            var stage = target + ".cyenginehub-update";
            var backup = Path.Combine(stage, "backup", "Engine", "Binaries", "Win64", "Test.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Move(dllPath, backup);
            await File.WriteAllTextAsync(dllPath, "interrupted replacement");
            await File.WriteAllTextAsync(Path.Combine(target, "new-partial.txt"), "partial");
            await File.WriteAllTextAsync(Path.Combine(stage, "journal.json"), JsonSerializer.Serialize(new[]
            {
                new { Path = "Engine/Binaries/Win64/Test.dll", Existed = true },
                new { Path = "new-partial.txt", Existed = false }
            }));
        }
        try
        {
            using var cancellation = new CancellationTokenSource();
            using var handler = new FixtureHandler(scenario, cancellation);
            if (scenario == "update-recover") handler.BeforeFirstRequest = () =>
            {
                Assert.Equal(originalDll, File.ReadAllBytes(dllPath));
                Assert.False(File.Exists(Path.Combine(target, "new-partial.txt")));
            };
            using var http = new HttpClient(handler);
            var store = new FixtureSession();
            using var direct = new DirectFabApiClient(http, store);
            var backend = new OfficialEpicGamesLauncherEngineBackend(http, direct, store) { ChunkRetryDelayAsync = (_, _) => Task.CompletedTask };
            using var locked = scenario == "update-locked" ? new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
            var progress = new InlineProgress(value =>
            {
                if (scenario == "update-cancel" && value.Message.StartsWith("Installation ·", StringComparison.Ordinal)) cancellation.Cancel();
                if (scenario == "update-fail-commit" && value.Message.StartsWith("Installation ·", StringComparison.Ordinal)) throw new IOException("Fixture commit interrupted");
            });
            var update = backend.UpdateAsync(new("qa", "UE_QA", "5.6", "QA engine"), target, progress, cancellation.Token);
            if (scenario is "update-success" or "update-current" or "update-recover")
            {
                var result = await update;
                Assert.Equal(scenario == "update-current" ? 0 : 2, result.FileCount);
                Assert.Equal(2, handler.RequestCount); // Unchanged.txt supplies the verified chunk; no network payload is needed.
                Assert.Equal(FixtureHandler.Payload, await File.ReadAllBytesAsync(buildPath));
                Assert.Equal(FixtureHandler.Payload, await File.ReadAllBytesAsync(dllPath));
                Assert.False(Directory.Exists(target + ".cyenginehub-update"));
            }
            else
            {
                await Assert.ThrowsAnyAsync<Exception>(() => update);
                Assert.Equal(originalBuild, await File.ReadAllBytesAsync(buildPath));
                Assert.Equal(originalDll, await File.ReadAllBytesAsync(dllPath));
                if (scenario == "update-cancel")
                {
                    Assert.False(File.Exists(Path.Combine(target + ".cyenginehub-update", "journal.json")));
                    var resumed = await backend.UpdateAsync(new("qa", "UE_QA", "5.6", "QA engine"), target);
                    Assert.Equal(2, resumed.FileCount);
                }
            }
            Assert.False(File.Exists(Path.Combine(target, "Engine", "Source", "Optional.cpp")));
            Assert.Equal(oldTime, File.GetLastWriteTimeUtc(unchangedPath));
            Assert.Equal("keep custom plugins", await File.ReadAllTextAsync(customPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task FabUpdateVerificationFetchesOnlyMetadataAndManifestNeverAssetChunks()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new FixtureHandler("fab-check", cancellation);
        using var http = new HttpClient(handler);
        using var client = new DirectFabApiClient(http, new FixtureSession());
        var version = new RemoteFabProjectVersion("artifact", [], ["5.6"], ["Windows"]);
        var asset = new RemoteFabAsset("fixture", "qa", "Fixture", "", "Fab", "AssetPack", null, null, [], [version]);
        var manifest = await client.GetSourceManifestAsync(asset, version);
        Assert.NotNull(manifest);
        Assert.NotEmpty(manifest.ContentHash!);
        Assert.Equal("5.6.1", manifest.BuildVersion);
        Assert.Equal(2, handler.RequestCount);
    }
    [Theory]
    [InlineData("metadata", 1)]
    [InlineData("success", 2)]
    [InlineData("binary", 2)]
    public async Task UpdateChecksReadMetadataOrManifestButNeverEngineChunks(string scenario, int requests)
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new FixtureHandler(scenario, cancellation);
        using var http = new HttpClient(handler);
        var store = new FixtureSession();
        using var direct = new DirectFabApiClient(http, store);
        var backend = new OfficialEpicGamesLauncherEngineBackend(http, direct, store);
        var build = await backend.GetLatestBuildAsync(new("qa", "UE_QA", "5.6", "QA"));
        Assert.NotNull(build);
        Assert.Equal(requests, handler.RequestCount);
        Assert.Equal(new Version(5, 6, 1), build.Version);
    }
    [Theory]
    [InlineData("success")]
    [InlineData("binary")]
    [InlineData("corrupt")]
    [InlineData("cancel")]
    [InlineData("occupied")]
    [InlineData("unauthorized")]
    [InlineData("traversal")]
    public async Task SimulatedEngineDownloadIsVerifiedAndNeverOverwritesExistingData(string scenario)
    {
        var root = Path.Combine(Path.GetTempPath(), "CyHubEngineDownloadQa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "FakeEngine");
            using var cancellation = new CancellationTokenSource();
            using var handler = new FixtureHandler(scenario, cancellation);
            using var http = new HttpClient(handler);
            var store = new FixtureSession();
            using var direct = new DirectFabApiClient(http, store);
            var backend = new OfficialEpicGamesLauncherEngineBackend(http, direct, store);
            var messages = new List<EpicBinaryEngineDownloadProgress>();
            if (scenario == "occupied")
            {
                Directory.CreateDirectory(target);
                await File.WriteAllTextAsync(Path.Combine(target, "keep.txt"), "untouched");
            }
            var operation = backend.DownloadAsync(new("qa", "UE_QA", "5.6", "QA engine"), target,
                new InlineProgress(messages.Add), cancellation.Token);
            if (scenario is "success" or "binary")
            {
                var result = await operation;
                Assert.Equal(1, result.FileCount);
                Assert.Equal(FixtureHandler.Payload, await File.ReadAllBytesAsync(Path.Combine(target, "Engine", "Build", "Build.version")));
                Assert.Equal(1d, messages[^1].Fraction);
                Assert.Contains(messages, value => value.DownloadedBytes > 0 && value.TotalBytes > 0);
                Assert.Equal(3, handler.RequestCount);
                Assert.False(Directory.Exists(target + ".cyenginehub-download"));
            }
            else
            {
                if (scenario == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
                else
                {
                    var error = await Assert.ThrowsAnyAsync<Exception>(() => operation);
                    Assert.True(error is InvalidOperationException or DirectFabException or IOException or ArgumentException,
                        "Expected a controlled download rejection, not an unrelated test exception: " + error);
                }
                Assert.DoesNotContain(messages, value => value.Fraction == 1);
                if (scenario == "occupied")
                {
                    Assert.Equal("untouched", await File.ReadAllTextAsync(Path.Combine(target, "keep.txt")));
                    Assert.Equal(0, handler.RequestCount);
                }
                else Assert.False(Directory.Exists(target));
                Assert.False(File.Exists(Path.Combine(root, "escaped.txt")));
            }
            Assert.True(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length) < 1024 * 1024);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class InlineProgress(Action<EpicBinaryEngineDownloadProgress> report) : IProgress<EpicBinaryEngineDownloadProgress>
    { public void Report(EpicBinaryEngineDownloadProgress value) => report(value); }

    private sealed class FixtureSession : IDirectFabSessionStore
    {
        public Task<DirectFabSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<DirectFabSession?>(
            new("qa", "QA", "fake-token-not-a-credential", DateTimeOffset.UtcNow.AddHours(1), "fake-refresh", DateTimeOffset.UtcNow.AddDays(1)));
        public Task SaveAsync(DirectFabSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixtureHandler(string scenario, CancellationTokenSource cancellation) : HttpMessageHandler
    {
        private static readonly string[] UpdateFiles = ["Engine/Build/Build.version", "Engine/Binaries/Win64/Test.dll", "Engine/Content/Unchanged.txt", "Engine/Source/Optional.cpp"];
        internal static readonly byte[] Payload = Encoding.UTF8.GetBytes("{\"MajorVersion\":5,\"MinorVersion\":6,\"PatchVersion\":1}");
        private const string ChunkId = "00112233445566778899aabbccddeeff";
        public int RequestCount { get; private set; }
        public Action? BeforeFirstRequest { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (RequestCount == 0) BeforeFirstRequest?.Invoke();
            RequestCount++;
            var uri = request.RequestUri!;
            if (uri.Host == "www.fab.com")
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    "{\"downloadInfo\":[{\"distributionPointBaseUrls\":[\"https://qa.invalid/\"],\"distributionPoints\":[{\"manifestUrl\":\"https://qa.invalid/build.manifest\"}]}]}") });
            }
            if (uri.Host == "launcher-public-service-prod06.ol.epicgames.com")
            {
                Assert.Equal("fake-token-not-a-credential", request.Headers.Authorization?.Parameter);
                return Task.FromResult(new HttpResponseMessage(scenario == "unauthorized" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
                { Content = new StringContent(scenario == "metadata"
                    ? "{\"elements\":[{\"buildVersion\":\"5.6.1-42+++UE5+Release-5.6\",\"manifests\":[{\"uri\":\"https://qa.invalid/build.manifest\"}]}]}"
                    : "{\"elements\":[{\"manifests\":[{\"uri\":\"https://qa.invalid/build.manifest\"}]}]}") });
            }
            Assert.Equal("qa.invalid", uri.Host); // Any unexpected request fails, never delegates to a network handler.
            var chunk = Chunk();
            byte[] bytes;
            if (uri.AbsolutePath == "/build.manifest") bytes = scenario == "binary" ? EpicBinaryManifestTests.Create(Payload) : Manifest(chunk.Length);
            else
            {
                Assert.EndsWith(".chunk", uri.AbsolutePath, StringComparison.Ordinal);
                if (scenario == "cancel") { cancellation.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
                bytes = chunk;
                if (scenario is "corrupt" or "update-corrupt") bytes[^1] ^= 0xff;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
        private byte[] Manifest(int size) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["ManifestFileVersion"] = Blob(BitConverter.GetBytes(13U)), ["bIsFileData"] = false,
            ["AppNameString"] = "UE_QA", ["BuildVersionString"] = "5.6.1",
            ["FileManifestList"] = (scenario.StartsWith("update-", StringComparison.Ordinal)
                ? UpdateFiles
                : new[] { scenario == "traversal" ? "../../escaped.txt" : "Engine/Build/Build.version" })
                .Select(path => new Dictionary<string, object>
            {
                ["Filename"] = scenario == "update-traversal" ? "../../escaped.txt" : path,
                ["FileHash"] = Blob(SHA1.HashData(Payload)),
                ["FileChunkParts"] = new object[] { new Dictionary<string, string>
                { ["Guid"] = ChunkId, ["Offset"] = Blob(BitConverter.GetBytes(0U)), ["Size"] = Blob(BitConverter.GetBytes((uint)Payload.Length)) } },
                ["InstallTags"] = Array.Empty<string>()
            }).ToArray(),
            ["ChunkHashList"] = new Dictionary<string, string> { [ChunkId] = Blob(BitConverter.GetBytes(1UL)) },
            ["DataGroupList"] = new Dictionary<string, string> { [ChunkId] = Blob([7]) },
            ["ChunkFilesizeList"] = new Dictionary<string, string> { [ChunkId] = Blob(BitConverter.GetBytes((long)size)) }
        });
        private static byte[] Chunk()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(2986228386U); writer.Write(1U); writer.Write(41U); writer.Write((uint)Payload.Length);
            for (var i = 0; i < ChunkId.Length; i += 8) writer.Write(uint.Parse(ChunkId.AsSpan(i, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            writer.Write(1UL); writer.Write((byte)0); writer.Write(Payload);
            return stream.ToArray();
        }
        private static string Blob(IEnumerable<byte> bytes) => string.Concat(bytes.Select(value => value.ToString("D3", CultureInfo.InvariantCulture)));
    }
}
