using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

public sealed class QuixelParallelDownloadTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "CyHubQuixelQa-" + Guid.NewGuid().ToString("N"))).FullName;
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleSignedFilesRunWithinTheAssetBudgetAndPublishOnlyWhenComplete(bool cancel)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var store = new SessionStore();
        var client = new QuixelMegascansApiClient(http, store, store, "");
        client.SetDownloadConnections(4);
        var version = new RemoteFabProjectVersion("quixel-tier:fixture:2", [], [], []);
        var asset = new RemoteFabAsset("fixture", "", "QA Asset", "", "Megascans", "", null, null, [], [version]);
        using var cancellation = new CancellationTokenSource();
        var progress = new ConcurrentQueue<DirectFabDownloadProgress>();
        var operation = client.DownloadAsync(asset, version, _root, new Sink(progress.Enqueue), cancellation.Token);
        await handler.Parallel.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(4, handler.Peak);
        Assert.Empty(Directory.EnumerateDirectories(_root, "Tier-2", SearchOption.AllDirectories));
        client.SetDownloadConnections(1); // An active transfer keeps its captured connection budget.
        if (cancel)
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            Assert.Empty(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories));
            Assert.Empty(Directory.EnumerateDirectories(_root, "*.partial-*", SearchOption.AllDirectories));
        }
        else
        {
            handler.Release.TrySetResult();
            var result = await operation;
            Assert.Equal(8, result.FileCount);
            Assert.Equal(8 * 1024, result.DownloadedBytes);
            foreach (var path in Directory.EnumerateFiles(result.DestinationDirectory)) Assert.Equal(new byte[1024], await File.ReadAllBytesAsync(path));
            var samples = progress.ToArray();
            Assert.Equal(1, samples[^1].Fraction);
            Assert.True(samples.Zip(samples.Skip(1)).All(pair => pair.First.DownloadedBytes <= pair.Second.DownloadedBytes));
        }
        Assert.InRange(handler.Peak, 2, 4);
        Assert.Equal(0, handler.Active);
    }

    private sealed class Sink(Action<DirectFabDownloadProgress> report) : IProgress<DirectFabDownloadProgress>
    { public void Report(DirectFabDownloadProgress value) => report(value); }
    private sealed class Handler : HttpMessageHandler
    {
        public TaskCompletionSource Parallel { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Active, Peak;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host == "quixel.com")
            {
                Assert.NotNull(request.Headers.Authorization);
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { files = Enumerable.Range(0, 8).Select(index => $"https://qa.invalid/file{index}.bin") })) };
            }
            Assert.Equal("qa.invalid", request.RequestUri.Host);
            Assert.Null(request.Headers.Authorization);
            var active = Interlocked.Increment(ref Active);
            int previous;
            do { previous = Peak; if (active <= previous) break; } while (Interlocked.CompareExchange(ref Peak, active, previous) != previous);
            if (active >= 4) Parallel.TrySetResult();
            try { await Release.Task.WaitAsync(token); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1024]) }; }
            finally { Interlocked.Decrement(ref Active); }
        }
    }
    private sealed class SessionStore : IQuixelSessionStore, IDirectFabSessionStore
    {
        public Task<QuixelSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<QuixelSession?>(new("fixture-token", "fixture-refresh", DateTimeOffset.UtcNow.AddHours(8), "fixture", "QA", ""));
        public Task SaveAsync(QuixelSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        Task<DirectFabSession?> IDirectFabSessionStore.LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(DirectFabSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
