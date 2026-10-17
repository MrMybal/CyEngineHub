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
        internal static readonly byte[] Payload = Encoding.UTF8.GetBytes("{\"MajorVersion\":5,\"MinorVersion\":6,\"PatchVersion\":1}");
        private const string ChunkId = "00112233445566778899aabbccddeeff";
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var uri = request.RequestUri!;
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
                if (scenario == "corrupt") bytes[^1] ^= 0xff;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
        private byte[] Manifest(int size) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["ManifestFileVersion"] = Blob(BitConverter.GetBytes(13U)), ["bIsFileData"] = false,
            ["AppNameString"] = "UE_QA", ["BuildVersionString"] = "5.6.1",
            ["FileManifestList"] = new object[] { new Dictionary<string, object>
            {
                ["Filename"] = scenario == "traversal" ? "../../escaped.txt" : "Engine/Build/Build.version",
                ["FileHash"] = Blob(SHA1.HashData(Payload)),
                ["FileChunkParts"] = new object[] { new Dictionary<string, string>
                { ["Guid"] = ChunkId, ["Offset"] = Blob(BitConverter.GetBytes(0U)), ["Size"] = Blob(BitConverter.GetBytes((uint)Payload.Length)) } },
                ["InstallTags"] = Array.Empty<string>()
            } },
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
