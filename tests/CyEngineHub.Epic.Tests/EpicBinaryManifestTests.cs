using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

#pragma warning disable CA5350 // Protocol integrity, not credential security.
public sealed class EpicBinaryManifestTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParsesBinaryEngineManifestAndPreservesBuildVersion(bool large)
    {
        var bytes = Create(Encoding.UTF8.GetBytes("fixture"), large);
        if (large) Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) > 64 * 1024 * 1024);
        var manifest = EpicManifestParser.Parse(bytes, new Uri("https://qa.invalid/CloudDir/"));
        Assert.Equal("5.6.1-42+++UE5+Release-5.6", manifest.BuildVersion);
        var file = Assert.Single(manifest.Files);
        Assert.Equal("Engine/Build/Build.version", file.RelativePath);
        Assert.Equal(7, file.Size);
        Assert.Equal("core", Assert.Single(file.InstallTags));
        Assert.Contains("ChunksV4/", Assert.Single(manifest.Chunks).Value.DownloadUri.AbsolutePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("expanded-limit")]
    [InlineData("stored-length")]
    [InlineData("expansion-overrun")]
    [InlineData("hash")]
    [InlineData("encryption")]
    public void RejectsInvalidBinaryHeadersOrPayload(string scenario)
    {
        var bytes = Create([1, 2, 3]);
        switch (scenario)
        {
            case "expanded-limit": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 256 * 1024 * 1024 + 1); break;
            case "stored-length": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1); break;
            case "expansion-overrun": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1); break;
            case "hash": bytes[16] ^= 0xff; break;
            case "encryption": bytes[36] = 2; break;
        }
        Assert.Throws<DirectFabException>(() => EpicManifestParser.Parse(bytes, new Uri("https://qa.invalid/")));
    }

    internal static byte[] Create(byte[] fileContents, bool large = false)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true);
        void Text(string text) { var value = Encoding.UTF8.GetBytes(text); writer.Write(value.Length + 1); writer.Write(value); writer.Write((byte)0); }
        void Section(Action write)
        {
            var start = payload.Position; writer.Write(0U); write(); var end = payload.Position;
            payload.Position = start; writer.Write(checked((uint)(end - start))); payload.Position = end;
        }
        void GuidValue() { writer.Write(0x00112233U); writer.Write(0x44556677U); writer.Write(0x8899aabbU); writer.Write(0xccddeeffU); }
        Section(() =>
        {
            writer.Write((byte)1); writer.Write(18U); writer.Write((byte)0); writer.Write(0U);
            Text("UE_QA"); Text("5.6.1-42+++UE5+Release-5.6"); Text(""); Text(""); writer.Write(0U);
            Text(""); Text(""); Text(""); Text("opaque-build-id-not-a-version");
        });
        Section(() =>
        {
            writer.Write((byte)0); writer.Write(1U); GuidValue(); writer.Write(1UL);
            writer.Write(SHA1.HashData(fileContents)); writer.Write((byte)7); writer.Write((uint)fileContents.Length);
            writer.Write((long)fileContents.Length + 41);
        });
        Section(() =>
        {
            writer.Write((byte)0); writer.Write(1U); Text("Engine/Build/Build.version"); Text("");
            writer.Write(SHA1.HashData(fileContents)); writer.Write((byte)0); writer.Write(1U); Text("core");
            writer.Write(1U); writer.Write(28U); GuidValue(); writer.Write(0U); writer.Write((uint)fileContents.Length);
        });
        Section(() =>
        {
            writer.Write((byte)0); writer.Write(large ? 65U : 0U);
            if (!large) return;
            for (var i = 0; i < 65; i++) Text("field" + i);
            var value = new string('x', 1024 * 1024);
            for (var i = 0; i < 65; i++) Text(value);
        });
        var body = payload.ToArray();
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) zlib.Write(body);
        using var output = new MemoryStream();
        using var header = new BinaryWriter(output);
        header.Write(0x44BEC00CU); header.Write(41U); header.Write((uint)body.Length); header.Write((uint)compressed.Length);
        header.Write(SHA1.HashData(body)); header.Write((byte)1); header.Write(18U); header.Write(compressed.ToArray());
        return output.ToArray();
    }
}
