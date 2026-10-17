using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CyEngineHub.Epic.Acquisition;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

#pragma warning disable CA5350 // SHA-1 is required by the distribution protocol.
public sealed class EpicParallelDownloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyHubParallelQa-" + Guid.NewGuid().ToString("N"));
    public EpicParallelDownloadTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("normal")]
    [InlineData("retry")]
    [InlineData("cache")]
    [InlineData("timeout-once")]
    [InlineData("truncated-once")]
    [InlineData("long-transient")]
    public async Task StreamsParallelChunksWithinLimitAndReconstructsVerifiedFiles(string mode)
    {
        using var handler = new FixtureHandler(mode);
        using var http = new HttpClient(handler);
        var session = new FixtureSession();
        using var direct = new DirectFabApiClient(http, session);
        var backend = new OfficialEpicGamesLauncherEngineBackend(http, direct, session) { ChunkRetryDelayAsync = (_, _) => Task.CompletedTask };
        backend.SetChunkConcurrency(4);
        var target = Path.Combine(_root, "Engine");
        if (mode == "cache")
        {
            var cache = Directory.CreateDirectory(Path.Combine(target + ".cyenginehub-download", "chunks")).FullName;
            await File.WriteAllBytesAsync(Path.Combine(cache, FixtureHandler.GuidFor(0) + ".chunk"), FixtureHandler.Chunk(0));
            await File.WriteAllBytesAsync(Path.Combine(cache, FixtureHandler.GuidFor(1) + ".chunk"), [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(cache, FixtureHandler.GuidFor(2) + ".chunk.partial"), [4]);
        }
        var messages = new ConcurrentQueue<EpicBinaryEngineDownloadProgress>();
        var operation = backend.DownloadAsync(new("qa", "UE_QA", "5.6", "QA"), target, new ProgressSink(messages.Enqueue));
        await handler.FourRequests.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(4, handler.Peak);
        handler.Release.TrySetResult();
        var result = await operation;
        Assert.InRange(handler.Peak, 2, 4);
        Assert.Equal(12, result.FileCount);
        for (var i = 0; i < 12; i++) Assert.Equal(FixtureHandler.Payload(i), await File.ReadAllBytesAsync(Path.Combine(target, "Engine", $"QA{i}.dat")));
        var samples = messages.ToArray();
        Assert.True(samples.Zip(samples.Skip(1)).All(pair => pair.First.DownloadedBytes <= pair.Second.DownloadedBytes));
        Assert.True(samples.Length < 30, "Progress is throttled globally, not emitted for each stream block.");
        Assert.Equal(1, samples[^1].Fraction);
        Assert.False(Directory.Exists(target + ".cyenginehub-download"));
        if (mode == "retry") Assert.Equal(2, handler.Attempts[FixtureHandler.GuidFor(1)]);
        if (mode is "timeout-once" or "truncated-once") Assert.Equal(2, handler.Attempts[FixtureHandler.GuidFor(1)]);
        if (mode == "long-transient") Assert.Equal(5, handler.Attempts[FixtureHandler.GuidFor(1)]);
        if (mode == "cache")
        {
            Assert.False(handler.Attempts.ContainsKey(FixtureHandler.GuidFor(0)));
            Assert.Equal(1, handler.Attempts[FixtureHandler.GuidFor(1)]);
            Assert.Equal(FixtureHandler.Chunk(0).Length, samples[^1].CachedBytes);
        }
        Assert.True(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length) < 1024 * 1024);
    }

    [Fact]
    public async Task ComponentDownloadsUseTheSameParallelPipelineAndPreserveUnrelatedFiles()
    {
        using var handler = new FixtureHandler("normal");
        using var http = new HttpClient(handler);
        var session = new FixtureSession();
        using var direct = new DirectFabApiClient(http, session);
        var backend = new OfficialEpicGamesLauncherEngineBackend(http, direct, session);
        backend.SetChunkConcurrency(4);
        var target = Path.Combine(_root, "Components");
        Directory.CreateDirectory(Path.Combine(target, "Engine"));
        await File.WriteAllTextAsync(Path.Combine(target, "Engine", "keep.txt"), "unchanged");
        var operation = backend.UpdateComponentsAsync(new("qa", "UE_QA", "5.6", "QA"), target, ["linux"]);
        await handler.FourRequests.Task.WaitAsync(TimeSpan.FromSeconds(8));
        handler.Release.TrySetResult();
        var result = await operation;
        Assert.Equal(12, result.InstalledFileCount);
        Assert.InRange(handler.Peak, 2, 4);
        Assert.Equal("unchanged", await File.ReadAllTextAsync(Path.Combine(target, "Engine", "keep.txt")));
        for (var i = 0; i < 12; i++) Assert.Equal(FixtureHandler.Payload(i), await File.ReadAllBytesAsync(Path.Combine(target, "Engine", $"QA{i}.dat")));
    }

    [Fact]
    public async Task CancellationStopsAllParallelRequestsBeforeInstallation()
    {
        using var handler = new FixtureHandler("normal");
        using var http = new HttpClient(handler);
        var session = new FixtureSession();
        using var direct = new DirectFabApiClient(http, session);
        var backend = new OfficialEpicGamesLauncherEngineBackend(http, direct, session);
        backend.SetChunkConcurrency(4);
        using var cancellation = new CancellationTokenSource();
        var target = Path.Combine(_root, "Canceled");
        var operation = backend.DownloadAsync(new("qa", "UE_QA", "5.6", "QA"), target, cancellationToken: cancellation.Token);
        await handler.FourRequests.Task.WaitAsync(TimeSpan.FromSeconds(8));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(0, handler.Active);
        Assert.False(Directory.Exists(target));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CancelingDuringAStreamRemovesPartialFilesAndNeverPublishesIncompleteChunks()
    {
        using var handler = new FixtureHandler("midcancel");
        handler.Release.TrySetResult();
        using var http = new HttpClient(handler);
        var session = new FixtureSession();
        using var direct = new DirectFabApiClient(http, session);
        var backend = new OfficialEpicGamesLauncherEngineBackend(http, direct, session);
        backend.SetChunkConcurrency(4);
        using var cancellation = new CancellationTokenSource();
        var target = Path.Combine(_root, "Interrupted");
        var operation = backend.DownloadAsync(new("qa", "UE_QA", "5.6", "QA"), target, cancellationToken: cancellation.Token);
        await handler.InsideStream.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.NotEmpty(Directory.EnumerateFiles(_root, "*.partial", SearchOption.AllDirectories));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Empty(Directory.EnumerateFiles(_root, "*.partial", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(target));
        var interrupted = Path.Combine(target + ".cyenginehub-download", "chunks", FixtureHandler.GuidFor(1) + ".chunk");
        Assert.False(File.Exists(interrupted));
    }

    [Theory]
    [InlineData("forbidden", 1)]
    [InlineData("short", 6)]
    public async Task RejectsNonRetryableOrRepeatedlyIncompleteChunksWithoutInstalling(string mode, int attempts)
    {
        using var handler = new FixtureHandler(mode);
        handler.Release.TrySetResult();
        using var http = new HttpClient(handler);
        var session = new FixtureSession();
        using var direct = new DirectFabApiClient(http, session);
        var backend = new OfficialEpicGamesLauncherEngineBackend(http, direct, session);
        backend.ChunkRetryDelayAsync = (_, _) => Task.CompletedTask;
        backend.SetChunkConcurrency(4);
        var target = Path.Combine(_root, "Failed");
        var error = await Record.ExceptionAsync(() => backend.DownloadAsync(new("qa", "UE_QA", "5.6", "QA"), target));
        Assert.True(error is HttpRequestException or IOException);
        Assert.Equal(attempts, handler.Attempts[FixtureHandler.GuidFor(1)]);
        Assert.False(Directory.Exists(target));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.partial", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void RejectsInvalidParallelism(int concurrency) => Assert.Throws<ArgumentOutOfRangeException>(() => new OfficialEpicGamesLauncherEngineBackend().SetChunkConcurrency(concurrency));

    [Fact]
    public async Task RestartedBackendDownloadsOnlyMissingChunksAfterExhaustedRetries()
    {
        var target = Path.Combine(_root, "Resume");
        using (var failedHandler = new FixtureHandler("short"))
        using (var http = new HttpClient(failedHandler))
        {
            failedHandler.Release.TrySetResult();
            var session = new FixtureSession();
            using var direct = new DirectFabApiClient(http, session);
            var backend = new OfficialEpicGamesLauncherEngineBackend(http, direct, session) { ChunkRetryDelayAsync = (_, _) => Task.CompletedTask };
            backend.SetChunkConcurrency(1);
            var error = await Assert.ThrowsAsync<IOException>(() => backend.DownloadAsync(new("qa", "UE_QA", "5.6", "QA"), target));
            Assert.Contains("Reprendre", error.Message, StringComparison.Ordinal);
            Assert.Equal(6, failedHandler.Attempts[FixtureHandler.GuidFor(1)]);
        }
        var cached = Path.Combine(target + ".cyenginehub-download", "chunks", FixtureHandler.GuidFor(0) + ".chunk");
        Assert.True(File.Exists(cached));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.partial", SearchOption.AllDirectories));
        using var handler = new FixtureHandler("normal");
        using var resumedHttp = new HttpClient(handler);
        handler.Release.TrySetResult();
        var resumedSession = new FixtureSession();
        using var resumedDirect = new DirectFabApiClient(resumedHttp, resumedSession);
        var resumedBackend = new OfficialEpicGamesLauncherEngineBackend(resumedHttp, resumedDirect, resumedSession);
        var samples = new ConcurrentQueue<EpicBinaryEngineDownloadProgress>();
        var result = await resumedBackend.DownloadAsync(new("qa", "UE_QA", "5.6", "QA"), target, new ProgressSink(samples.Enqueue));
        Assert.Equal(12, result.FileCount);
        Assert.False(handler.Attempts.ContainsKey(FixtureHandler.GuidFor(0)));
        Assert.Equal(FixtureHandler.Chunk(0).Length, samples.Last().CachedBytes);
        for (var i = 0; i < 12; i++) Assert.Equal(FixtureHandler.Payload(i), await File.ReadAllBytesAsync(Path.Combine(target, "Engine", $"QA{i}.dat")));
    }

    [Fact]
    public async Task CancelingBackoffStopsRetriesImmediatelyAndPreservesCompleteChunks()
    {
        using var handler = new FixtureHandler("short");
        using var http = new HttpClient(handler);
        handler.Release.TrySetResult();
        var session = new FixtureSession();
        using var direct = new DirectFabApiClient(http, session);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new OfficialEpicGamesLauncherEngineBackend(http, direct, session)
        {
            ChunkRetryDelayAsync = (_, token) => { waiting.TrySetResult(); return Task.Delay(Timeout.Infinite, token); }
        };
        backend.SetChunkConcurrency(1);
        using var cancellation = new CancellationTokenSource();
        var target = Path.Combine(_root, "Backoff");
        var operation = backend.DownloadAsync(new("qa", "UE_QA", "5.6", "QA"), target, cancellationToken: cancellation.Token);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(8));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(1, handler.Attempts[FixtureHandler.GuidFor(1)]);
        Assert.True(File.Exists(Path.Combine(target + ".cyenginehub-download", "chunks", FixtureHandler.GuidFor(0) + ".chunk")));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.partial", SearchOption.AllDirectories));
    }

    private sealed class ProgressSink(Action<EpicBinaryEngineDownloadProgress> sink) : IProgress<EpicBinaryEngineDownloadProgress>
    { public void Report(EpicBinaryEngineDownloadProgress value) => sink(value); }
    private sealed class FixtureSession : IDirectFabSessionStore
    {
        public Task<DirectFabSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<DirectFabSession?>(new("qa", "QA", "fixture-token", DateTimeOffset.UtcNow.AddHours(1), "fixture-refresh", DateTimeOffset.UtcNow.AddDays(1)));
        public Task SaveAsync(DirectFabSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class FixtureHandler(string mode) : HttpMessageHandler
    {
        private static readonly string[] LinuxTags = ["linux"];
        public TaskCompletionSource FourRequests { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource InsideStream { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentDictionary<string, int> Attempts { get; } = new(StringComparer.Ordinal);
        private int _active, _peak;
        public int Active => Volatile.Read(ref _active);
        public int Peak => Volatile.Read(ref _peak);
        public static string GuidFor(int index) => $"00112233445566778899aabb{index:X8}".ToLowerInvariant();
        public static byte[] Payload(int index) => Enumerable.Repeat((byte)(index + 1), 1024).ToArray();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "launcher-public-service-prod06.ol.epicgames.com") return Json("{\"elements\":[{\"manifests\":[{\"uri\":\"https://qa.invalid/test.manifest\"}]}]}");
            Assert.Equal("qa.invalid", uri.Host);
            if (uri.AbsolutePath == "/test.manifest") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Manifest()) };
            var guid = Path.GetFileNameWithoutExtension(uri.AbsolutePath).Split('_').Last().ToLowerInvariant();
            var index = int.Parse(guid.AsSpan(24), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var attempt = Attempts.AddOrUpdate(guid, 1, (_, previous) => previous + 1);
            var active = Interlocked.Increment(ref _active);
            int previous;
            do { previous = _peak; if (active <= previous) break; } while (Interlocked.CompareExchange(ref _peak, active, previous) != previous);
            if (active >= 4) FourRequests.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                if (index == 1 && mode == "forbidden") return new(HttpStatusCode.Forbidden);
                if (index == 1 && mode == "retry" && attempt == 1) return new(HttpStatusCode.ServiceUnavailable);
                if (index == 1 && mode == "timeout-once" && attempt == 1) throw new TaskCanceledException("Synthetic HTTP timeout");
                var bytes = Chunk(index);
                if (index == 1 && mode == "midcancel") return new(HttpStatusCode.OK) { Content = new SlowContent(InsideStream) };
                if (index == 1 && mode == "short") bytes = bytes[..^1];
                if (index == 1 && (mode == "truncated-once" && attempt == 1 || mode == "long-transient" && attempt <= 4)) bytes = bytes[..^1];
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
        private static byte[] Manifest()
        {
            var indices = Enumerable.Range(0, 12).ToArray();
            return JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["ManifestFileVersion"] = Blob(BitConverter.GetBytes(13U)), ["bIsFileData"] = false,
                ["AppNameString"] = "UE_QA", ["BuildVersionString"] = "5.6.1",
                ["FileManifestList"] = indices.Select(index => new Dictionary<string, object>
                {
                    ["Filename"] = $"Engine/QA{index}.dat", ["FileHash"] = Blob(SHA1.HashData(Payload(index))),
                    ["FileChunkParts"] = new[] { new Dictionary<string, string> { ["Guid"] = GuidFor(index), ["Offset"] = Blob(BitConverter.GetBytes(0U)), ["Size"] = Blob(BitConverter.GetBytes(1024U)) } },
                    ["InstallTags"] = LinuxTags
                }),
                ["ChunkHashList"] = indices.ToDictionary(GuidFor, _ => Blob(BitConverter.GetBytes(1UL))),
                ["DataGroupList"] = indices.ToDictionary(GuidFor, _ => Blob([7])),
                ["ChunkFilesizeList"] = indices.ToDictionary(GuidFor, index => Blob(BitConverter.GetBytes((long)Chunk(index).Length)))
            });
        }
        public static byte[] Chunk(int index)
        {
            using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
            writer.Write(2986228386U); writer.Write(1U); writer.Write(41U); writer.Write(1024U);
            var guid = GuidFor(index);
            for (var part = 0; part < 32; part += 8) writer.Write(uint.Parse(guid.AsSpan(part, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            writer.Write(1UL); writer.Write((byte)0); writer.Write(Payload(index)); return output.ToArray();
        }
        private static string Blob(IEnumerable<byte> bytes) => string.Concat(bytes.Select(value => value.ToString("D3", CultureInfo.InvariantCulture)));
    }
    private sealed class SlowContent(TaskCompletionSource inside) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 1065; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new InterruptedStream(inside));
    }
    private sealed class InterruptedStream(TaskCompletionSource inside) : Stream
    {
        private bool _started;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_started) { _started = true; buffer.Span[..16].Clear(); return 16; }
            inside.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
