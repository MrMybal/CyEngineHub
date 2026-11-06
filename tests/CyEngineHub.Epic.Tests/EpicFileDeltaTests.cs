using System.Security.Cryptography;
using CyEngineHub.Epic.Acquisition;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

#pragma warning disable CA5350 // Epic manifest protocol integrity.

public sealed class EpicFileDeltaTests
{
    [Fact]
    public async Task InPlacePatchRefusesAHardLinkBeforeWritingOrJournaling()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new DeltaFixture();
        await File.WriteAllTextAsync(fixture.Original, "original");
        var otherInstallation = Path.Combine(fixture.Root, "OtherEngine.dll");
        Assert.True(CreateHardLinkW(otherInstallation, fixture.Original, IntPtr.Zero));
        var patch = await fixture.Create("modified"u8.ToArray());
        var journaled = false;
        using (var output = new FileStream(fixture.Original, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => EpicFileDelta.Apply(patch, output, CancellationToken.None, () => journaled = true));
        Assert.False(journaled);
        Assert.Equal("original", await File.ReadAllTextAsync(fixture.Original));
        Assert.Equal("original", await File.ReadAllTextAsync(otherInstallation));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string path, string existing, IntPtr securityAttributes);

    [Theory]
    [InlineData("small-edit")]
    [InlineData("sparse-interior")]
    [InlineData("grow")]
    [InlineData("shrink")]
    [InlineData("insert")]
    [InlineData("empty")]
    [InlineData("new")]
    [InlineData("same")]
    public async Task ByteRangesRoundTripWithoutCopyingUnchangedData(string scenario)
    {
        using var fixture = new DeltaFixture();
        var old = new byte[2 << 20]; new Random(473).NextBytes(old);
        if (scenario != "new") await File.WriteAllBytesAsync(fixture.Original, old);
        if (File.Exists(fixture.Original)) File.SetLastWriteTimeUtc(fixture.Original, DateTime.UtcNow.AddDays(-4));
        var originalTime = File.Exists(fixture.Original) ? File.GetLastWriteTimeUtc(fixture.Original) : DateTime.MinValue;
        var next = scenario switch
        {
            "grow" => old.Concat(new byte[9000]).ToArray(),
            "shrink" => old[..^13000],
            "insert" => old[..3].Concat(new byte[] { 22 }).Concat(old[3..]).ToArray(),
            "empty" => [],
            _ => (byte[])old.Clone()
        };
        if (scenario == "small-edit") { next[19] ^= 1; next[40_000] ^= 3; next[^14] ^= 7; }
        if (scenario == "sparse-interior")
            for (var offset = 0; offset < next.Length; offset += 4096) { next[offset + 11] ^= 1; next[offset + 4080] ^= 2; }
        var patch = await fixture.Create(next);
        Assert.Equal(scenario != "new", patch.Header.Existed);
        if (scenario != "new") Assert.Equal(old, await File.ReadAllBytesAsync(fixture.Original));
        else Assert.False(File.Exists(fixture.Original));
        if (scenario == "small-edit")
        {
            Assert.Equal(3, patch.NewBytes); Assert.Equal(3, patch.OldBytes);
            Assert.True(patch.DiskBytes < 2048, "Three edited bytes must not stage a whole 2 MiB binary.");
        }
        if (scenario == "same") { Assert.Equal(0, patch.Ranges); Assert.True(patch.DiskBytes < 2048); }
        if (scenario == "sparse-interior")
        { Assert.Equal(1024, patch.NewBytes); Assert.Equal(1024, patch.OldBytes); Assert.True(patch.DiskBytes < 24 * 1024); }
        using (var output = new FileStream(fixture.Original, scenario == "new" ? FileMode.CreateNew : FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            EpicFileDelta.Apply(patch, output, CancellationToken.None);
        Assert.Equal(next, await File.ReadAllBytesAsync(fixture.Original));
        EpicFileDelta.Restore(fixture.Patch, fixture.Original, "Engine/Test.dll");
        if (scenario == "new") Assert.False(File.Exists(fixture.Original));
        else
        {
            Assert.Equal(old, await File.ReadAllBytesAsync(fixture.Original));
            Assert.Equal(originalTime, File.GetLastWriteTimeUtc(fixture.Original));
        }
    }

    [Fact]
    public async Task InterruptedRangeWritesAreFullyReversible()
    {
        using var fixture = new DeltaFixture();
        var old = new byte[200_000]; new Random(19).NextBytes(old);
        var next = (byte[])old.Clone(); next[200] ^= 8; next[100_000] ^= 9;
        await File.WriteAllBytesAsync(fixture.Original, old);
        var patch = await fixture.Create(next);
        var ranges = 0; var journaled = false;
        using (var output = new FileStream(fixture.Original, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => EpicFileDelta.Apply(patch, output, CancellationToken.None,
                () => journaled = true, () => { Assert.True(journaled); if (++ranges == 1) throw new IOException("Simulated abrupt commit interruption"); }));
        Assert.NotEqual(old, await File.ReadAllBytesAsync(fixture.Original));
        EpicFileDelta.Restore(fixture.Patch, fixture.Original, "Engine/Test.dll");
        Assert.Equal(old, await File.ReadAllBytesAsync(fixture.Original));
        EpicFileDelta.Restore(fixture.Patch, fixture.Original, "Engine/Test.dll"); // crash recovery is idempotent
        Assert.Equal(old, await File.ReadAllBytesAsync(fixture.Original));
    }

    [Fact]
    public async Task CorruptedPatchCannotWriteAnyInstalledByte()
    {
        using var fixture = new DeltaFixture();
        var old = new byte[10000]; var next = (byte[])old.Clone(); next[15] = 42;
        await File.WriteAllBytesAsync(fixture.Original, old);
        var patch = await fixture.Create(next);
        var corrupted = await File.ReadAllBytesAsync(fixture.Patch); corrupted[^35] ^= 1;
        await File.WriteAllBytesAsync(fixture.Patch, corrupted);
        var journaled = false;
        using (var output = new FileStream(fixture.Original, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<InvalidDataException>(() => EpicFileDelta.Apply(patch, output, CancellationToken.None, () => journaled = true));
        Assert.False(journaled); Assert.Equal(old, await File.ReadAllBytesAsync(fixture.Original));
    }

    [Fact]
    public async Task OriginalChangedAfterPreparationIsNotOverwritten()
    {
        using var fixture = new DeltaFixture();
        await File.WriteAllTextAsync(fixture.Original, "original");
        var patch = await fixture.Create("modified"u8.ToArray());
        await File.WriteAllTextAsync(fixture.Original, "USEREDIT");
        using (var output = new FileStream(fixture.Original, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => EpicFileDelta.Apply(patch, output, CancellationToken.None));
        Assert.Equal("USEREDIT", await File.ReadAllTextAsync(fixture.Original));
    }

    [Fact]
    public async Task LowSpaceDuringPreparationLeavesOriginalAndNoPartialDelta()
    {
        using var fixture = new DeltaFixture();
        var old = new byte[2 << 20]; var next = Enumerable.Repeat((byte)127, old.Length).ToArray();
        await File.WriteAllBytesAsync(fixture.Original, old);
        using var cache = new DecodedChunkCache(0, (_, _) => Task.FromResult(next));
        var file = DeltaFixture.File(next);
        var reservations = 0;
        await Assert.ThrowsAsync<IOException>(() => EpicFileDelta.CreateAsync(fixture.Original, file, fixture.Patch, cache,
            _ => { if (++reservations > 1) throw new IOException("Simulated low space"); }, CancellationToken.None));
        Assert.Equal(old, await File.ReadAllBytesAsync(fixture.Original));
        Assert.False(File.Exists(fixture.Patch)); Assert.False(File.Exists(fixture.Patch + ".partial"));
    }

    [Fact]
    public async Task RecoveryRejectsADifferentJournalTarget()
    {
        using var fixture = new DeltaFixture();
        await File.WriteAllTextAsync(fixture.Original, "original");
        await fixture.Create("modified"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => EpicFileDelta.Restore(fixture.Patch, fixture.Original, "Engine/Other.dll"));
        Assert.Throws<InvalidDataException>(() => EpicFileDelta.Restore(fixture.Patch, fixture.Original, "Engine/Test.dll", false));
        Assert.Equal("original", await File.ReadAllTextAsync(fixture.Original));
    }

    [Fact]
    public async Task BackendRecoversInterruptedRangeJournalBeforeAnotherUpdate()
    {
        using var fixture = new DeltaFixture();
        var old = new byte[1 << 20]; new Random(12).NextBytes(old);
        var next = (byte[])old.Clone(); next[7] ^= 1; next[900_000] ^= 2;
        await File.WriteAllBytesAsync(fixture.Original, old);
        var patch = await fixture.Create(next);
        using (var output = new FileStream(fixture.Original, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => EpicFileDelta.Apply(patch, output, CancellationToken.None,
                afterRange: () => throw new IOException("Simulated process interruption")));
        var stage = Path.Combine(fixture.Root, "stage"); Directory.CreateDirectory(Path.Combine(stage, "deltas"));
        File.Copy(fixture.Patch, Path.Combine(stage, "deltas", "test.delta"));
        var journal = Path.Combine(stage, "journal.json");
        await File.WriteAllTextAsync(journal, "[{\"Path\":\"Engine/Test.dll\",\"Existed\":true,\"Delta\":\"deltas/test.delta\"}]");
        var recover = typeof(OfficialEpicGamesLauncherEngineBackend).GetMethod("RecoverEngineUpdate",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        recover.Invoke(null, [fixture.Root, stage]);
        Assert.Equal(old, await File.ReadAllBytesAsync(fixture.Original));
        Assert.False(File.Exists(journal));
        recover.Invoke(null, [fixture.Root, stage]);
        Assert.Equal(old, await File.ReadAllBytesAsync(fixture.Original));
    }

    private sealed class DeltaFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "CyHubDelta-" + Guid.NewGuid().ToString("N"));
        public string Root => _root;
        public string Original => Path.Combine(_root, "Engine", "Test.dll");
        public string Patch => Path.Combine(_root, "Test.delta");
        public DeltaFixture() => Directory.CreateDirectory(Path.Combine(_root, "Engine"));
        public async Task<EpicFileDelta.Info> Create(byte[] next)
        {
            using var cache = new DecodedChunkCache(0, (_, _) => Task.FromResult(next));
            return await EpicFileDelta.CreateAsync(Original, File(next), Patch, cache, _ => { }, CancellationToken.None);
        }
        public static EpicManifestFile File(byte[] next) => new("Engine/Test.dll", SHA1.HashData(next),
            [new("chunk", 0, checked((uint)next.Length), 0)], next.Length, []);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
