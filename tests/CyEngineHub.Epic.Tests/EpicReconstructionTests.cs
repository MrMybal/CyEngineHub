using System.Diagnostics;
using System.Security.Cryptography;
using CyEngineHub.Epic.Acquisition;
using CyEngineHub.Fab.Remote;
using Xunit.Abstractions;

namespace CyEngineHub.Epic.Tests;

#pragma warning disable CA5350 // Distribution integrity protocol.
public sealed class EpicReconstructionTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "CyHubReconstructQa-" + Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public async Task CacheUsesByteBoundedCaseInsensitiveLruAndBypassesOversizedChunks()
    {
        using var cache = new DecodedChunkCache(8, (id, _) => Task.FromResult(new byte[id == "large" ? 9 : 4]));
        var first = await cache.GetAsync("a", default);
        await cache.GetAsync("b", default);
        Assert.Same(first, await cache.GetAsync("A", default));
        await cache.GetAsync("c", default); // b was least recently used.
        Assert.Same(first, await cache.GetAsync("a", default));
        await cache.GetAsync("b", default);
        await cache.GetAsync("large", default);
        await cache.GetAsync("large", default);
        Assert.Equal(6, cache.Loads);
        Assert.Equal(2, cache.Hits);
        Assert.Equal(8, cache.ResidentBytes);
        Assert.Equal(8, cache.PeakBytes);
        cache.Dispose();
        Assert.Equal(0, cache.ResidentBytes);
    }

    [Fact]
    public async Task RepeatedSharedChunkBenchmarkProducesIdenticalFilesWithFewerDecodes()
    {
        const int fileCount = 48;
        var payload = Enumerable.Range(0, 1024 * 1024).Select(index => (byte)(index % 251)).ToArray();
        var compressed = EpicCompressedChunkTests.Create(payload);
        var baseline = await RunAsync(0, "baseline");
        var cached = await RunAsync(payload.Length, "cached");
        Assert.Equal(fileCount * 2, baseline.Loads);
        Assert.Equal(1, cached.Loads);
        Assert.Equal(fileCount * 2 - 1, cached.Hits);
        Assert.Equal(payload.Length, cached.Peak);
        output.WriteLine($"Synthetic reconstruction: {fileCount} files, 96 shared parts. Decodes {baseline.Loads} -> {cached.Loads}; cache peak {cached.Peak} bytes; elapsed {baseline.Milliseconds:F1} -> {cached.Milliseconds:F1} ms (local fixture, not network throughput).");

        async Task<(long Loads, long Hits, long Peak, double Milliseconds)> RunAsync(long budget, string folder)
        {
            using var cache = new DecodedChunkCache(budget, (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(EpicManifestParser.ParseChunk(compressed, EpicCompressedChunkTests.Expected));
            });
            var stopwatch = Stopwatch.StartNew();
            for (var index = 0; index < fileCount; index++)
            {
                var file = new EpicManifestFile($"{index}.dat", SHA1.HashData(payload),
                    [new("shared", 0, 524288, 0), new("shared", 524288, 524288, 524288)], payload.Length, []);
                var destination = Path.Combine(_root, folder, file.RelativePath);
                await OfficialEpicGamesLauncherEngineBackend.ReconstructFileAsync(file, destination, cache,
                    () => throw new InvalidOperationException("Fixture integrity failed"), default);
                Assert.True(EpicManifestParser.MatchesSha1(destination, file.Sha1));
            }
            return (cache.Loads, cache.Hits, cache.PeakBytes, stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("bounds")]
    [InlineData("cancel")]
    public async Task InvalidOrCanceledReconstructionPreservesDestinationAndCleansPartial(string scenario)
    {
        byte[] payload = [1, 2, 3, 4];
        var target = Path.Combine(_root, "output.dat");
        await File.WriteAllBytesAsync(target, [9, 8, 7]);
        using var cancellation = new CancellationTokenSource();
        using var cache = new DecodedChunkCache(16, (_, _) =>
        {
            if (scenario == "cancel") cancellation.Cancel();
            return Task.FromResult(payload);
        });
        var invalidated = false;
        var file = new EpicManifestFile("output.dat", SHA1.HashData(scenario == "hash" ? [5, 6] : payload),
            [new("shared", 0, scenario == "bounds" ? 5U : 4U, 0)], scenario == "size" ? 5 : 4, []);
        var operation = OfficialEpicGamesLauncherEngineBackend.ReconstructFileAsync(file, target, cache,
            () => invalidated = true, cancellation.Token);
        if (scenario == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
        Assert.Equal(scenario is "hash" or "size", invalidated);
        Assert.Equal(new byte[] { 9, 8, 7 }, await File.ReadAllBytesAsync(target));
        Assert.False(File.Exists(target + ".partial"));
    }

    [Fact]
    public async Task VerifiedResumeSkipsChunkLoadsAndCanceledCacheHitThrows()
    {
        byte[] payload = [1, 2, 3];
        var target = Path.Combine(_root, "resume.dat");
        await File.WriteAllBytesAsync(target, payload);
        using var cache = new DecodedChunkCache(16, (_, _) => Task.FromResult(payload));
        var file = new EpicManifestFile("resume.dat", SHA1.HashData(payload), [new("shared", 0, 3, 0)], 3, []);
        await OfficialEpicGamesLauncherEngineBackend.ReconstructFileAsync(file, target, cache,
            () => throw new InvalidOperationException(), default);
        Assert.Equal(0, cache.Loads);
        await cache.GetAsync("shared", default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cache.GetAsync("shared", new CancellationToken(true)));
        Assert.Equal(0, cache.Hits);
    }

    [Fact]
    public async Task FabReusesDecodedPartsAcrossFilesAndReportsBytesInsideOneLargeFile()
    {
        var data = new byte[2 * 1024 * 1024];
        new Random(42).NextBytes(data);
        var serialized = EpicCompressedChunkTests.Create(data);
        var chunk = EpicCompressedChunkTests.Expected with { DownloadSize = serialized.Length };
        var chunkPath = Path.Combine(_root, chunk.ChunkId + ".chunk");
        await File.WriteAllBytesAsync(chunkPath, serialized);
        var staging = Path.Combine(_root, "files");
        var file = new EpicManifestFile("first.dat", SHA1.HashData(data), [new(chunk.ChunkId, 0, (uint)data.Length, 0)], data.Length, []);
        var manifest = new EpicDownloadManifest("qa", "fixture", 15, new Dictionary<string, EpicManifestChunk> { [chunk.ChunkId] = chunk },
            [file, file with { RelativePath = "second.dat" }]);
        var samples = new List<DirectFabDownloadProgress>();
        void Observe(DirectFabDownloadProgress sample)
        {
            samples.Add(sample);
            if (sample.ProcessedBytes > 0 && File.Exists(chunkPath))
                File.Move(chunkPath, chunkPath + ".held"); // Subsequent parts can only come from the decoded cache.
        }
        await DirectFabApiClient.ReconstructFilesAsync(manifest, staging, _root, serialized.Length, 456, new ReportSink(Observe), default);

        Assert.Contains(samples, sample => sample.ProcessedBytes > 0 && sample.ProcessedBytes < data.Length &&
            sample.TotalProcessingBytes == 2L * data.Length && sample.Phase == DirectFabDownloadPhase.Reconstruction);
        Assert.All(samples, sample => { Assert.Equal(serialized.Length, sample.DownloadedBytes); Assert.Equal(456, sample.CachedBytes); });
        Assert.Equal(2L * data.Length, samples[^1].ProcessedBytes);
        samples.Clear();
        // Both outputs validate on resume even though the raw chunk is unavailable.
        await DirectFabApiClient.ReconstructFilesAsync(manifest, staging, _root, serialized.Length, 456, new ReportSink(samples.Add), default);
        Assert.Contains(samples, sample => sample.Phase == DirectFabDownloadPhase.Verification && sample.ProcessedBytes > 0 && sample.ProcessedBytes < data.Length);
        Assert.DoesNotContain(samples, sample => sample.Phase == DirectFabDownloadPhase.Reconstruction);
        Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(staging, "second.dat")));
    }

    private sealed class ReportSink(Action<DirectFabDownloadProgress> report) : IProgress<DirectFabDownloadProgress>
    {
        public void Report(DirectFabDownloadProgress value) => report(value);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
