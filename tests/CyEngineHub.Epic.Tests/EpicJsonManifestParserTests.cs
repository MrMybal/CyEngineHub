using System.Text.Json;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

public sealed class EpicJsonManifestParserTests
{
    [Fact]
    public void ParsesLegacyJsonBuildPatchManifest()
    {
        const string chunkGuid = "00112233445566778899aabbccddeeff";
        const ulong rollingHash = 0x1122334455667788;
        var fileHash = Enumerable.Range(1, 20).Select(value => (byte)value).ToArray();
        var json = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["ManifestFileVersion"] = Blob(BitConverter.GetBytes(13U)),
            ["bIsFileData"] = false,
            ["AppNameString"] = "ReliefMapping",
            ["BuildVersionString"] = "5.3.0",
            ["FileManifestList"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["Filename"] = "Plugins/ReliefMapping.uplugin",
                    ["FileHash"] = Blob(fileHash),
                    ["FileChunkParts"] = new[]
                    {
                        new Dictionary<string, string>
                        {
                            ["Guid"] = chunkGuid,
                            ["Offset"] = Blob(BitConverter.GetBytes(0U)),
                            ["Size"] = Blob(BitConverter.GetBytes(3U)),
                        },
                    },
                    ["InstallTags"] = Array.Empty<string>(),
                },
            },
            ["ChunkHashList"] = new Dictionary<string, string>
            {
                [chunkGuid] = Blob(BitConverter.GetBytes(rollingHash)),
            },
            ["DataGroupList"] = new Dictionary<string, string>
            {
                [chunkGuid] = Blob([7]),
            },
            ["ChunkFilesizeList"] = new Dictionary<string, string>
            {
                [chunkGuid] = Blob(BitConverter.GetBytes(64L)),
            },
        });

        var manifest = EpicManifestParser.Parse(
            json,
            new Uri("https://cdn.example/CloudDir/?signature=test"));

        Assert.Equal(13U, manifest.Version);
        Assert.Equal("ReliefMapping", manifest.AppName);
        var file = Assert.Single(manifest.Files);
        Assert.Equal("Plugins/ReliefMapping.uplugin", file.RelativePath);
        Assert.Equal(fileHash, file.Sha1);
        Assert.Equal(3, file.Size);
        Assert.Equal(chunkGuid, Assert.Single(file.Parts).ChunkGuid);
        var chunk = Assert.Single(manifest.Chunks).Value;
        Assert.Equal(rollingHash, chunk.RollingHash);
        Assert.Equal((byte)7, chunk.Group);
        Assert.Equal(64, chunk.DownloadSize);
        Assert.Contains(
            $"/ChunksV3/07/{rollingHash:X16}_{chunkGuid.ToUpperInvariant()}.chunk",
            chunk.DownloadUri.AbsolutePath,
            StringComparison.Ordinal);
        Assert.Equal("?signature=test", chunk.DownloadUri.Query);
    }

    private static string Blob(IEnumerable<byte> bytes) => string.Concat(
        bytes.Select(value => value.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)));
}
