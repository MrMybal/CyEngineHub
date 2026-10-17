using System.Net;
using System.Net.Http.Headers;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

public sealed class SignedFileTransferTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "CyHubSignedQa-" + Guid.NewGuid().ToString("N"))).FullName;
    [Theory]
    [InlineData("ranges", 4)]
    [InlineData("ignored", 1)]
    [InlineData("changed", 1)]
    [InlineData("bad-range", 1)]
    [InlineData("single", 0)]
    [InlineData("weak", 0)]
    [InlineData("unknown", 0)]
    [InlineData("retry", 4)]
    public async Task DownloadsVerifiedRangesOrFallsBackWithoutMixingFiles(string mode, int minimumRanges)
    {
        using var handler = new Handler(mode);
        using var http = new HttpClient(handler);
        var transfer = new SignedFileTransfer(http) { RetryDelayAsync = (_, _) => Task.CompletedTask };
        var path = Path.Combine(_root, "asset.bin");
        var samples = new List<long>();
        var operation = transfer.DownloadAsync(new("https://qa.invalid/asset"), path, 4, (bytes, _) => { lock (samples) samples.Add(bytes); }, CancellationToken.None);
        if (mode is "ranges" or "retry")
        {
            await handler.Parallel.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal(4, handler.Peak);
        }
        handler.Release.TrySetResult();
        Assert.Equal(Handler.Payload.Length, await operation);
        Assert.Equal(Handler.Payload, await File.ReadAllBytesAsync(path));
        Assert.True(handler.RangeRequests >= minimumRanges);
        Assert.InRange(handler.Peak, 0, 4);
        if (mode is "single" or "weak" or "unknown") Assert.Equal(0, handler.RangeRequests);
        if (mode is "ignored" or "changed" or "bad-range") Assert.True(handler.FullRequests >= 2);
        Assert.Equal(Handler.Payload.Length, samples[^1]);
    }

    [Theory]
    [InlineData("short", 6)]
    [InlineData("forbidden", 1)]
    public async Task IncompleteAndForbiddenFilesNeverSucceed(string mode, int attempts)
    {
        using var handler = new Handler(mode);
        using var http = new HttpClient(handler);
        var transfer = new SignedFileTransfer(http) { RetryDelayAsync = (_, _) => Task.CompletedTask };
        await Assert.ThrowsAnyAsync<Exception>(() => transfer.DownloadAsync(new("https://qa.invalid/asset"), Path.Combine(_root, "bad"), 4, (_, _) => { }, CancellationToken.None));
        Assert.Equal(attempts, handler.FullRequests);
    }

    [Fact]
    public async Task CancellationStopsAllSegmentsAndReleasesTheFile()
    {
        using var handler = new Handler("ranges");
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var path = Path.Combine(_root, "canceled");
        var operation = new SignedFileTransfer(http).DownloadAsync(new("https://qa.invalid/asset"), path, 4, (_, _) => { }, cancellation.Token);
        await handler.Parallel.Task.WaitAsync(TimeSpan.FromSeconds(8));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(0, handler.Active);
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private sealed class Handler(string mode) : HttpMessageHandler
    {
        public static readonly byte[] Payload = Enumerable.Range(0, 8 * 1024 * 1024).Select(index => (byte)(index % 251)).ToArray();
        public TaskCompletionSource Parallel { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int FullRequests, RangeRequests, Active, Peak;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Null(request.Headers.Authorization);
            if (request.Headers.Range is null)
            {
                var attempt = Interlocked.Increment(ref FullRequests);
                if (mode == "forbidden") return new(HttpStatusCode.Forbidden);
                if (mode == "retry" && attempt == 1) return new(HttpStatusCode.ServiceUnavailable);
                HttpContent content = mode == "unknown" ? new UnknownContent() : new ByteArrayContent(Payload);
                if (mode == "short") content.Headers.ContentLength = Payload.Length + 1;
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
                if (mode is not ("single" or "short" or "unknown"))
                {
                    response.Headers.AcceptRanges.Add("bytes");
                    response.Headers.ETag = new("\"fixture-v1\"", mode == "weak");
                }
                return response;
            }
            Interlocked.Increment(ref RangeRequests);
            Assert.Equal("\"fixture-v1\"", request.Headers.IfRange?.EntityTag?.ToString());
            var active = Interlocked.Increment(ref Active);
            int previous;
            do { previous = Peak; if (active <= previous) break; } while (Interlocked.CompareExchange(ref Peak, active, previous) != previous);
            if (active == 4) Parallel.TrySetResult();
            try
            {
                if (mode is "ranges" or "retry") await Release.Task.WaitAsync(token);
                if (mode == "ignored") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) };
                var range = request.Headers.Range.Ranges.Single();
                var start = range.From!.Value; var end = range.To!.Value;
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Payload, (int)start, (int)(end - start + 1)) };
                response.Content.Headers.ContentRange = new(start, end, mode == "bad-range" ? Payload.Length + 1 : Payload.Length);
                response.Headers.ETag = new(mode == "changed" ? "\"fixture-v2\"" : "\"fixture-v1\"");
                return response;
            }
            finally { Interlocked.Decrement(ref Active); }
        }
    }
    private sealed class UnknownContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(Handler.Payload).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(Handler.Payload, writable: false));
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
