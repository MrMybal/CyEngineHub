using System.Buffers.Binary;
using System.IO.Compression;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

public sealed class EpicCompressedChunkTests
{
    internal static readonly EpicManifestChunk Expected = new(new string('0', 32), 0, 0, 0, new Uri("https://qa.invalid/chunk"));

    [Theory]
    [InlineData(4, true)]
    [InlineData(1, false)]
    [InlineData(8, false)]
    public void OnlyThePreciseV2ToV3HeaderUpgradeMayDifferFromTheManifest(int difference, bool valid)
    {
        var payload = new byte[1024];
        var raw = Create(payload);
        var expected = Expected with { DownloadSize = raw.Length - difference };
        if (valid) Assert.Equal(payload, EpicManifestParser.ParseChunk(raw, expected));
        else Assert.Throws<DirectFabException>(() => EpicManifestParser.ParseChunk(raw, expected));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void HeaderUpgradeNeverAcceptsAnIncompleteOrOverlongPayload(int delta)
    {
        var raw = Create(new byte[1024]);
        var expected = Expected with { DownloadSize = raw.Length - 4 };
        var declared = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(12), checked((uint)(declared + delta)));
        Assert.Throws<DirectFabException>(() => EpicManifestParser.ParseChunk(raw, expected));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("unknown-size")]
    [InlineData("overrun")]
    [InlineData("oversized-header")]
    [InlineData("invalid-zlib")]
    public void EnforcesDecodedSizeBeforeUnboundedAllocation(string scenario)
    {
        var payload = new byte[1024 * 1024];
        var raw = Create(payload);
        if (scenario == "unknown-size") BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 1);
        if (scenario == "overrun") BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(62), 1);
        if (scenario == "oversized-header") BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(62), 256 * 1024 * 1024 + 1);
        if (scenario == "invalid-zlib") raw[66] = 0xff;
        if (scenario is "valid" or "unknown-size") Assert.Equal(payload, EpicManifestParser.ParseChunk(raw, Expected));
        else Assert.Throws<DirectFabException>(() => EpicManifestParser.ParseChunk(raw, Expected));
    }

    internal static byte[] Create(byte[] payload)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) zlib.Write(payload);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(2986228386U); writer.Write(3U); writer.Write(66U); writer.Write((uint)compressed.Length);
        writer.Write(new byte[16]); writer.Write(0UL); writer.Write((byte)1);
        writer.Write(new byte[20]); writer.Write((byte)0); writer.Write((uint)payload.Length);
        writer.Write(compressed.ToArray());
        return stream.ToArray();
    }
}
