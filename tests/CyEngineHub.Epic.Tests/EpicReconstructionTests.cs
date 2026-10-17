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

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
