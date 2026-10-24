using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyEngineHub.Epic.Acquisition;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

#pragma warning disable CA5350 // Epic manifest file integrity.

public sealed class EpicPatchStreamingTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("corrupt-local")]
    [InlineData("source-changes")]
    [InlineData("cancel-prepare")]
    [InlineData("fail-commit")]
    [InlineData("corrupt-network")]
    [InlineData("no-space")]
    public async Task PatchIsIncrementalBoundedAndRecoverable(string scenario)
    {
        var root = Path.Combine(Path.GetTempPath(), "CyHubPatchStream-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "UE"); Directory.CreateDirectory(target);
        try
        {
            using var fixture = new PatchFixture(target, scenario);
            using var http = new HttpClient(fixture);
            var session = new PatchSession();
            using var direct = new DirectFabApiClient(http, session);
            var backend = new OfficialEpicGamesLauncherEngineBackend(http, direct, session)
            {
                PatchChunkCacheBytes = 512, PatchFreeSpace = _ => scenario == "no-space" ? 1 : 20L << 20,
                ChunkRetryDelayAsync = (_, _) => Task.CompletedTask
            };
            using var cancellation = new CancellationTokenSource();
            EpicEnginePatchPlan? plan = null; long peakChunks = 0; var changedSource = false;
            var progress = new PatchProgress(value =>
            {
                if (value.PatchPlan is { } next) plan = next;
                var cache = target + ".cyenginehub-update/chunks";
                if (Directory.Exists(cache)) peakChunks = Math.Max(peakChunks, CacheBytes(cache));
                if (scenario == "source-changes" && !changedSource && value.PatchPlan is not null)
                { changedSource = true; System.IO.File.WriteAllBytes(Path.Combine(target, "Engine/Binaries/A.dll"), new byte[32]); }
                if (scenario == "cancel-prepare" && value.Message.StartsWith("Préparation du patch", StringComparison.Ordinal)) cancellation.Cancel();
                if (scenario == "fail-commit" && value.Message.StartsWith("Installation ·", StringComparison.Ordinal)) throw new IOException("Fixture: commit interrupted");
            });
            var task = backend.UpdateAsync(new("qa", "UE_QA", "5.8", "QA engine"), target, progress, cancellation.Token);
            if (scenario is "success" or "corrupt-local" or "source-changes")
            {
                var result = await task;
                Assert.Equal(43, result.FileCount);
                fixture.AssertNewFiles();
                Assert.True(System.IO.File.Exists(Path.Combine(target, ".cyenginehub-install.manifest")));
                Assert.False(Directory.Exists(target + ".cyenginehub-update"));
                Assert.Equal(scenario != "success", fixture.RequestedChunks.Contains(PatchFixture.Id(1), StringComparer.OrdinalIgnoreCase));
                Assert.True(peakChunks <= 512, $"Chunk cache exceeded its bounded prefetch window: {peakChunks}");
                var requests = fixture.RequestedChunks.Count;
                var current = await backend.UpdateAsync(new("qa", "UE_QA", "5.8", "QA"), target, progress);
                Assert.Equal(0, current.FileCount); Assert.Equal(requests, fixture.RequestedChunks.Count);
                Assert.Equal(0, plan!.AdditionalSpaceBytes);
            }
            else
            {
                await Assert.ThrowsAnyAsync<Exception>(() => task);
                fixture.AssertOriginalFiles();
                Assert.False(System.IO.File.Exists(Path.Combine(target, ".cyenginehub-install.manifest")));
                if (scenario == "no-space")
                {
                    Assert.Empty(fixture.RequestedChunks);
                    Assert.Contains("téléchargement estimé", task.Exception!.GetBaseException().Message, StringComparison.Ordinal);
                }
                if (scenario == "cancel-prepare")
                {
                    var resumed = await backend.UpdateAsync(new("qa", "UE_QA", "5.8", "QA"), target);
                    fixture.AssertNewFiles(); Assert.Equal(43, resumed.FileCount);
                    Assert.Equal(1, fixture.RequestedChunks.Count(id => id.Equals(PatchFixture.Id(2), StringComparison.OrdinalIgnoreCase)));
                }
                Assert.NotNull(plan);
            }
            Assert.False(System.IO.File.Exists(Path.Combine(target, "Engine/Platforms/Linux/SDK.bin")));
            Assert.Equal("keep user plugin", await System.IO.File.ReadAllTextAsync(Path.Combine(target, "Engine/Plugins/Personal/keep.txt")));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class PatchProgress(Action<EpicBinaryEngineDownloadProgress> report) : IProgress<EpicBinaryEngineDownloadProgress>
    { public void Report(EpicBinaryEngineDownloadProgress value) => report(value); }
    private static long CacheBytes(string directory)
    {
        long bytes = 0;
        foreach (var path in Directory.GetFiles(directory))
        {
            // Parallel transfers can rename a .partial file between enumeration and stat.
            try { bytes += new FileInfo(path).Length; }
            catch (FileNotFoundException) { }
        }
        return bytes;
    }
    private sealed class PatchSession : IDirectFabSessionStore
    {
        public Task<DirectFabSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<DirectFabSession?>(new("qa", "QA", "fixture-not-a-token", DateTimeOffset.UtcNow.AddHours(1), "fixture-refresh", DateTimeOffset.UtcNow.AddDays(1)));
        public Task SaveAsync(DirectFabSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class PatchFixture : HttpMessageHandler
    {
        private readonly string _target;
        private readonly string _scenario;
        private readonly Dictionary<string, byte[]> _payloads = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _original = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _next = new(StringComparer.OrdinalIgnoreCase);
        private readonly byte[] _manifest;
        public System.Collections.Concurrent.ConcurrentQueue<string> RequestedChunks { get; } = new();
        public static string Id(int number) => (number + 100).ToString("X32", CultureInfo.InvariantCulture);

        public PatchFixture(string target, string scenario)
        {
            _target = target; _scenario = scenario;
            _payloads[Id(0)] = Encoding.UTF8.GetBytes("{\"MajorVersion\":5,\"MinorVersion\":8,\"PatchVersion\":4}");
            _payloads[Id(1)] = Enumerable.Repeat((byte)65, 16).ToArray();
            _payloads[Id(2)] = Enumerable.Repeat((byte)66, 32).ToArray();
            _payloads[Id(3)] = Enumerable.Repeat((byte)67, 16).ToArray();
            _payloads[Id(4)] = Encoding.UTF8.GetBytes("{\"MajorVersion\":5,\"MinorVersion\":8,\"PatchVersion\":3}");
            _payloads[Id(5)] = Encoding.UTF8.GetBytes("core Linux tool, NOT the Linux platform");
            _payloads[Id(6)] = [7];
            var oldFiles = new[] { Entry("Engine/Build/Build.version", [Id(4)]), Entry("Engine/Binaries/A.dll", [Id(1), Id(3)]),
                Entry("Engine/Binaries/DotNET/Linux.Automation.dll", [Id(5)]) };
            var newFiles = new List<Dictionary<string, object>> { Entry("Engine/Build/Build.version", [Id(0)]), Entry("Engine/Binaries/A.dll", [Id(1), Id(2)]),
                Entry("Engine/Binaries/DotNET/Linux.Automation.dll", [Id(5)]), Entry("Engine/Content/New.bin", [Id(2)]) };
            for (var index = 10; index < 50; index++)
            {
                _payloads[Id(index)] = Enumerable.Repeat((byte)index, 96).ToArray();
                newFiles.Add(Entry($"Engine/Content/{index}.bin", [Id(index)]));
            }
            var optional = Entry("Engine/Platforms/Linux/SDK.bin", [Id(6)], "platform_Linux");
            // A giant absent optional SDK must never enter a patch merely because a core Linux helper exists.
            ((Dictionary<string, object>[])optional["FileChunkParts"])[0]["Size"] = Blob(BitConverter.GetBytes(2_000_000_000U));
            newFiles.Add(optional);
            foreach (var entry in oldFiles) _original[(string)entry["Filename"]] = Contents(entry);
            foreach (var entry in newFiles.Where(entry => (string)entry["Filename"] != "Engine/Platforms/Linux/SDK.bin")) _next[(string)entry["Filename"]] = Contents(entry);
            foreach (var (path, data) in _original) Write(path, data);
            if (scenario == "corrupt-local") Write("Engine/Binaries/A.dll", new byte[32]);
            Write("Engine/Plugins/Personal/keep.txt", Encoding.UTF8.GetBytes("keep user plugin"));
            Write(".egstore/old.manifest", Manifest(oldFiles, "5.8.3"));
            _manifest = Manifest(newFiles, "5.8.4");
        }
        public void AssertOriginalFiles()
        {
            foreach (var (path, bytes) in _original) Assert.Equal(bytes, System.IO.File.ReadAllBytes(Path.Combine(_target, path)));
            Assert.False(System.IO.File.Exists(Path.Combine(_target, "Engine/Content/New.bin")));
        }
        public void AssertNewFiles() { foreach (var (path, bytes) in _next) Assert.Equal(bytes, System.IO.File.ReadAllBytes(Path.Combine(_target, path))); }
        private void Write(string path, byte[] data) { var full = Path.Combine(_target, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); System.IO.File.WriteAllBytes(full, data); }
        private Dictionary<string, object> Entry(string path, string[] ids, params string[] tags) => new()
        {
            ["Filename"] = path, ["FileHash"] = Blob(SHA1.HashData(ids.SelectMany(id => _payloads[id]).ToArray())), ["InstallTags"] = tags,
            ["FileChunkParts"] = ids.Select(id => new Dictionary<string, object> { ["Guid"] = id, ["Offset"] = Blob(BitConverter.GetBytes(0U)), ["Size"] = Blob(BitConverter.GetBytes((uint)_payloads[id].Length)) }).ToArray()
        };
        private byte[] Contents(Dictionary<string, object> entry) => ((Dictionary<string, object>[])entry["FileChunkParts"]).SelectMany(part => _payloads[(string)part["Guid"]]).ToArray();
        private byte[] Manifest(IEnumerable<Dictionary<string, object>> files, string version) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["ManifestFileVersion"] = Blob(BitConverter.GetBytes(13U)), ["bIsFileData"] = false, ["AppNameString"] = "UE_QA", ["BuildVersionString"] = version, ["FileManifestList"] = files,
            ["ChunkHashList"] = _payloads.ToDictionary(pair => pair.Key, _ => Blob(BitConverter.GetBytes(1UL))),
            ["DataGroupList"] = _payloads.ToDictionary(pair => pair.Key, _ => Blob([0])),
            ["ChunkFilesizeList"] = _payloads.ToDictionary(pair => pair.Key, pair => Blob(BitConverter.GetBytes((long)Chunk(pair.Key).Length)))
        });
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); var uri = request.RequestUri!;
            if (uri.Host == "launcher-public-service-prod06.ol.epicgames.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"elements\":[{\"manifests\":[{\"uri\":\"https://patch-fixture.invalid/build.manifest\"}]}]}") });
            Assert.Equal("patch-fixture.invalid", uri.Host);
            if (uri.AbsolutePath == "/build.manifest") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_manifest) });
            Assert.EndsWith(".chunk", uri.AbsolutePath, StringComparison.Ordinal);
            var id = Path.GetFileNameWithoutExtension(uri.AbsolutePath).Split('_')[1];
            RequestedChunks.Enqueue(id);
            Assert.Equal(_original["Engine/Build/Build.version"], System.IO.File.ReadAllBytes(Path.Combine(_target, "Engine/Build/Build.version")));
            var data = Chunk(id); if (_scenario == "corrupt-network") data[^1] ^= 255;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        }
        private byte[] Chunk(string id)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write(2986228386U); writer.Write(1U); writer.Write(41U); writer.Write((uint)_payloads[id].Length);
            for (var index = 0; index < 32; index += 8) writer.Write(uint.Parse(id.AsSpan(index, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            writer.Write(1UL); writer.Write((byte)0); writer.Write(_payloads[id]); return stream.ToArray();
        }
        private static string Blob(IEnumerable<byte> data) => string.Concat(data.Select(value => value.ToString("D3", CultureInfo.InvariantCulture)));
    }
}
