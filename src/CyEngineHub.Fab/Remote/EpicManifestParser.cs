using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CyEngineHub.Fab.Remote;

// Binary layout adapted from AchetaGames/egs-api-rs (MIT, Copyright 2021 Acheta Games).
[SuppressMessage(
    "Security",
    "CA5350:Do Not Use Weak Cryptographic Algorithms",
    Justification = "Epic manifests and files use SHA-1 as a protocol integrity field, not for password security.")]
public static class EpicManifestParser
{
    private const uint ManifestMagic = 1_153_351_692;
    private const int MaximumManifestBytes = 64 * 1024 * 1024;
    private const int MaximumExpandedManifestBytes = 256 * 1024 * 1024;
    private const int MaximumEntries = 1_000_000;

    public static EpicDownloadManifest Parse(ReadOnlyMemory<byte> manifestBytes, Uri sourceBaseUri)
    {
        ArgumentNullException.ThrowIfNull(sourceBaseUri);
        if (!sourceBaseUri.IsAbsoluteUri || sourceBaseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new DirectFabException("Le point de distribution Fab doit être une adresse HTTPS absolue.");
        }
        if (manifestBytes.Length is < 2 or > MaximumManifestBytes)
        {
            throw new DirectFabException("La taille du manifest Fab est invalide.");
        }

        if (LooksLikeJsonManifest(manifestBytes.Span))
        {
            return ParseJson(manifestBytes, sourceBaseUri);
        }

        if (manifestBytes.Length < 41)
        {
            throw new DirectFabException("La taille du manifest Fab binaire est invalide.");
        }

        var data = ReadPayload(manifestBytes.Span);
        var reader = new EpicBinaryReader(data);
        _ = reader.ReadUInt32();
        var metaVersion = reader.ReadByte();
        var manifestVersion = reader.ReadUInt32();
        _ = reader.ReadByte();
        _ = reader.ReadUInt32();
        var appName = reader.ReadFString();
        var buildVersion = reader.ReadFString();
        _ = reader.ReadFString();
        _ = reader.ReadFString();
        var prereqCount = ReadCount(reader);
        for (var index = 0; index < prereqCount; index++) _ = reader.ReadFString();
        _ = reader.ReadFString();
        _ = reader.ReadFString();
        _ = reader.ReadFString();
        if (metaVersion >= 1) _ = reader.ReadFString(); // Build ID, not the engine build version.
        if (metaVersion >= 2)
        {
            _ = reader.ReadFString();
            _ = reader.ReadFString();
        }

        _ = reader.ReadUInt32();
        _ = reader.ReadByte();
        var chunkCount = ReadCount(reader);
        var chunkGuids = new string[chunkCount];
        var hashes = new ulong[chunkCount];
        var groups = new byte[chunkCount];
        var fileSizes = new long[chunkCount];
        for (var index = 0; index < chunkCount; index++) chunkGuids[index] = reader.ReadGuid();
        for (var index = 0; index < chunkCount; index++) hashes[index] = reader.ReadUInt64();
        for (var index = 0; index < chunkCount; index++) _ = reader.ReadBytes(20);
        for (var index = 0; index < chunkCount; index++) groups[index] = reader.ReadByte();
        for (var index = 0; index < chunkCount; index++) _ = reader.ReadUInt32();
        for (var index = 0; index < chunkCount; index++) fileSizes[index] = reader.ReadInt64();

        var chunks = new Dictionary<string, EpicManifestChunk>(StringComparer.OrdinalIgnoreCase);
        var chunkDirectory = manifestVersion >= 15
            ? "ChunksV4"
            : manifestVersion >= 6
                ? "ChunksV3"
                : manifestVersion >= 3
                    ? "ChunksV2"
                    : "Chunks";
        for (var index = 0; index < chunkCount; index++)
        {
            var relative = $"{chunkDirectory}/{groups[index]:00}/{hashes[index]:X16}_{chunkGuids[index].ToUpperInvariant()}.chunk";
            chunks.Add(chunkGuids[index], new EpicManifestChunk(
                chunkGuids[index],
                hashes[index],
                groups[index],
                Math.Max(0, fileSizes[index]),
                CombineDistributionUri(sourceBaseUri, relative)));
        }

        _ = reader.ReadUInt32();
        var fileManifestVersion = reader.ReadByte();
        var fileCount = ReadCount(reader);
        var names = new string[fileCount];
        var hashesSha1 = new byte[fileCount][];
        for (var index = 0; index < fileCount; index++) names[index] = reader.ReadFString();
        for (var index = 0; index < fileCount; index++) _ = reader.ReadFString();
        for (var index = 0; index < fileCount; index++) hashesSha1[index] = reader.ReadBytes(20).ToArray();
        for (var index = 0; index < fileCount; index++) _ = reader.ReadByte();
        var installTags = new IReadOnlyList<string>[fileCount];
        for (var index = 0; index < fileCount; index++)
        {
            var tagCount = ReadCount(reader);
            var tags = new string[tagCount];
            for (var tag = 0; tag < tagCount; tag++) tags[tag] = reader.ReadFString();
            installTags[index] = tags;
        }

        var files = new List<EpicManifestFile>(fileCount);
        for (var index = 0; index < fileCount; index++)
        {
            var partCount = ReadCount(reader);
            var parts = new List<EpicManifestFilePart>(partCount);
            long fileOffset = 0;
            for (var partIndex = 0; partIndex < partCount; partIndex++)
            {
                var partStart = reader.Position;
                var partRecordSize = checked((int)reader.ReadUInt32());
                if (partRecordSize < 28)
                {
                    throw new DirectFabException("Un fragment de fichier du manifest Fab est invalide.");
                }
                var guid = reader.ReadGuid();
                var offset = reader.ReadUInt32();
                var size = reader.ReadUInt32();
                if (!chunks.ContainsKey(guid))
                {
                    throw new DirectFabException("Le manifest Fab référence un chunk inconnu.");
                }
                parts.Add(new EpicManifestFilePart(guid, offset, size, fileOffset));
                fileOffset = checked(fileOffset + size);
                var consumed = reader.Position - partStart;
                if (consumed < partRecordSize) reader.Skip(partRecordSize - consumed);
                if (consumed > partRecordSize)
                {
                    throw new DirectFabException("La taille d’un fragment du manifest Fab est incohérente.");
                }
            }
            files.Add(new EpicManifestFile(
                names[index],
                hashesSha1[index],
                parts,
                fileOffset,
                installTags[index]));
        }

        if (fileManifestVersion >= 1)
        {
            for (var index = 0; index < fileCount; index++)
            {
                if (reader.ReadUInt32() != 0) _ = reader.ReadBytes(16);
            }
            for (var index = 0; index < fileCount; index++) _ = reader.ReadFString();
        }
        if (fileManifestVersion >= 2)
        {
            for (var index = 0; index < fileCount; index++) _ = reader.ReadBytes(32);
        }

        return new EpicDownloadManifest(
            appName,
            buildVersion,
            manifestVersion,
            chunks,
            files);
    }

    public static byte[] ParseChunk(ReadOnlyMemory<byte> bytes, EpicManifestChunk expected)
    {
        if (bytes.Length < 41 || bytes.Length > 256 * 1024 * 1024)
        {
            throw new DirectFabException($"Le chunk {expected.ChunkId} a une taille invalide.");
        }
        var reader = new EpicBinaryReader(bytes);
        if (reader.ReadUInt32() != 2_986_228_386)
        {
            throw new DirectFabException($"Le chunk {expected.ChunkId} n’a pas la signature Epic attendue.");
        }
        var headerVersion = reader.ReadUInt32();
        var headerSize = checked((int)reader.ReadUInt32());
        var payloadSize = reader.ReadUInt32();
        var guid = reader.ReadGuid();
        _ = reader.ReadUInt64();
        var compressed = reader.ReadByte() != 0;
        if (!string.Equals(guid, expected.ChunkId, StringComparison.OrdinalIgnoreCase))
        {
            throw new DirectFabException($"Le chunk téléchargé ne correspond pas à {expected.ChunkId}.");
        }
        if (headerVersion >= 2)
        {
            _ = reader.ReadBytes(20);
            _ = reader.ReadByte();
        }
        uint? uncompressedSize = null;
        if (headerVersion >= 3) uncompressedSize = reader.ReadUInt32();
        if (uncompressedSize > 256 * 1024 * 1024)
            throw new DirectFabException($"La taille décompressée du chunk {expected.ChunkId} dépasse la limite autorisée.");
        if (headerSize < reader.Position || headerSize > bytes.Length)
        {
            throw new DirectFabException($"L’en-tête du chunk {expected.ChunkId} est invalide.");
        }
        if (payloadSize != bytes.Length - headerSize)
            throw new DirectFabException($"Le chunk {expected.ChunkId} est incomplet : sa charge utile ne correspond pas à l’en-tête.");
        // CDN chunks may be reserialized from header v2 to v3 (four extra bytes)
        // without changing the manifest's original serialized size. Do not reject
        // that precise upgrade; all other size differences remain invalid.
        if (expected.DownloadSize > 0 && bytes.Length != expected.DownloadSize &&
            !(headerVersion == 3 && headerSize == 66 && bytes.Length == expected.DownloadSize + 4))
            throw new DirectFabException($"La taille du chunk {expected.ChunkId} ne correspond pas au manifest.");
        reader.Skip(headerSize - reader.Position);
        var payload = reader.ReadBytes(reader.Remaining).ToArray();
        if (!compressed)
        {
            ValidateChunkSize(expected.ChunkId, payload, uncompressedSize);
            return payload;
        }

        try
        {
            using var input = new MemoryStream(payload, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var limit = uncompressedSize is > 0 ? uncompressedSize.Value : 256L * 1024 * 1024;
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = zlib.Read(buffer)) != 0)
            {
                if (output.Length + read > limit)
                    throw new DirectFabException($"La décompression du chunk {expected.ChunkId} dépasse la taille autorisée.");
                output.Write(buffer, 0, read);
            }
            var result = output.ToArray();
            ValidateChunkSize(expected.ChunkId, result, uncompressedSize);
            return result;
        }
        catch (InvalidDataException exception)
        {
            throw new DirectFabException($"Le chunk {expected.ChunkId} est compressé de façon invalide.", exception);
        }
    }

    public static bool MatchesSha1(string path, ReadOnlySpan<byte> expected)
    {
        if (!File.Exists(path) || expected.Length != 20) return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return SHA1.HashData(stream).AsSpan().SequenceEqual(expected);
    }

    private static ReadOnlyMemory<byte> ReadPayload(ReadOnlySpan<byte> bytes)
    {
        var header = new EpicBinaryReader(bytes.ToArray());
        if (header.ReadUInt32() != ManifestMagic)
        {
            throw new DirectFabException("Le manifest Fab n’utilise pas le format binaire Epic attendu.");
        }
        var headerSize = checked((int)header.ReadUInt32());
        var uncompressedSize = checked((int)header.ReadUInt32());
        var compressedSize = checked((int)header.ReadUInt32());
        var expectedHash = header.ReadBytes(20).ToArray();
        var storage = header.ReadByte();
        if ((storage & ~1) != 0)
            throw new DirectFabException("Le mode de stockage du manifest Epic n’est pas pris en charge.");
        var compressed = (storage & 1) != 0;
        _ = header.ReadUInt32();
        if (headerSize < header.Position || headerSize > bytes.Length ||
            uncompressedSize is <= 0 or > MaximumExpandedManifestBytes ||
            compressedSize is < 0 or > MaximumManifestBytes ||
            compressedSize != bytes.Length - headerSize)
        {
            throw new DirectFabException("L’en-tête du manifest Fab est invalide.");
        }

        header.Skip(headerSize - header.Position);
        byte[] payload;
        if (compressed)
        {
            try
            {
                using var input = new MemoryStream(header.ReadBytes(header.Remaining).ToArray(), writable: false);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream(uncompressedSize);
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = zlib.Read(buffer)) > 0)
                {
                    if (output.Length + read > uncompressedSize)
                        throw new DirectFabException("La taille décompressée du manifest Epic dépasse la taille annoncée.");
                    output.Write(buffer, 0, read);
                }
                payload = output.ToArray();
            }
            catch (InvalidDataException exception)
            {
                throw new DirectFabException("Le manifest Fab compressé est corrompu.", exception);
            }
        }
        else
        {
            payload = bytes[headerSize..].ToArray();
        }
        if (payload.Length != uncompressedSize || !SHA1.HashData(payload).AsSpan().SequenceEqual(expectedHash))
        {
            throw new DirectFabException("L’intégrité SHA-1 du manifest Fab est invalide.");
        }
        return payload;
    }

    private static EpicDownloadManifest ParseJson(
        ReadOnlyMemory<byte> manifestBytes,
        Uri sourceBaseUri)
    {
        try
        {
            using var document = JsonDocument.Parse(
                manifestBytes,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new DirectFabException("Le manifest JSON Epic doit contenir un objet racine.");
            }

            var featureLevel = root.TryGetProperty("ManifestFileVersion", out var featureElement)
                ? ReadJsonBlobUInt32(featureElement, "ManifestFileVersion")
                : 1U;
            if (featureLevel == 255)
            {
                featureLevel = 8;
            }
            var isFileData = root.TryGetProperty("bIsFileData", out var fileDataElement) &&
                             fileDataElement.ValueKind == JsonValueKind.True;
            if (isFileData)
            {
                throw new DirectFabException(
                    "Le manifest JSON Epic utilise l’ancien stockage par fichiers, non pris en charge pour ce téléchargement.");
            }

            var appName = ReadOptionalJsonString(root, "AppNameString") ?? "Fab";
            var buildVersion = ReadOptionalJsonString(root, "BuildVersionString") ?? string.Empty;
            var hashByGuid = ReadJsonUInt64Map(root, "ChunkHashList", required: true);
            var groupByGuid = ReadJsonByteMap(root, "DataGroupList", required: featureLevel >= 5);
            var sizeByGuid = ReadJsonInt64Map(root, "ChunkFilesizeList", required: false);

            if (!root.TryGetProperty("FileManifestList", out var fileList) ||
                fileList.ValueKind != JsonValueKind.Array ||
                fileList.GetArrayLength() > MaximumEntries)
            {
                throw new DirectFabException("La liste des fichiers du manifest JSON Epic est invalide.");
            }

            var referencedChunks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new List<EpicManifestFile>(fileList.GetArrayLength());
            var totalPartCount = 0;
            foreach (var fileElement in fileList.EnumerateArray())
            {
                if (fileElement.ValueKind != JsonValueKind.Object)
                {
                    throw new DirectFabException("Une entrée fichier du manifest JSON Epic est invalide.");
                }
                var relativePath = ReadRequiredJsonString(fileElement, "Filename");
                var fileHash = ReadJsonBlob(
                    GetRequiredJsonProperty(fileElement, "FileHash"),
                    20,
                    "FileHash");
                if (fileElement.TryGetProperty("SymlinkTarget", out var symlinkElement) &&
                    symlinkElement.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(symlinkElement.GetString()))
                {
                    throw new DirectFabException(
                        "Le manifest JSON Epic contient un lien symbolique non pris en charge.");
                }
                if (!fileElement.TryGetProperty("FileChunkParts", out var partList) ||
                    partList.ValueKind != JsonValueKind.Array)
                {
                    throw new DirectFabException("Les fragments d’un fichier du manifest JSON Epic sont absents.");
                }
                totalPartCount = checked(totalPartCount + partList.GetArrayLength());
                if (totalPartCount > MaximumEntries)
                {
                    throw new DirectFabException("Le manifest JSON Epic contient trop de fragments.");
                }

                var parts = new List<EpicManifestFilePart>(partList.GetArrayLength());
                long fileOffset = 0;
                foreach (var partElement in partList.EnumerateArray())
                {
                    if (partElement.ValueKind != JsonValueKind.Object)
                    {
                        throw new DirectFabException("Un fragment du manifest JSON Epic est invalide.");
                    }
                    var guid = NormalizeJsonGuid(ReadRequiredJsonString(partElement, "Guid"));
                    var offset = ReadJsonBlobUInt32(
                        GetRequiredJsonProperty(partElement, "Offset"),
                        "Offset");
                    var size = ReadJsonBlobUInt32(
                        GetRequiredJsonProperty(partElement, "Size"),
                        "Size");
                    if (size == 0)
                    {
                        throw new DirectFabException("Un fragment du manifest JSON Epic a une taille nulle.");
                    }
                    parts.Add(new EpicManifestFilePart(guid, offset, size, fileOffset));
                    referencedChunks.Add(guid);
                    fileOffset = checked(fileOffset + size);
                }

                IReadOnlyList<string> installTags = [];
                if (fileElement.TryGetProperty("InstallTags", out var tagList) &&
                    tagList.ValueKind == JsonValueKind.Array)
                {
                    installTags = tagList.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Cast<string>()
                        .Take(1_024)
                        .ToArray();
                }
                files.Add(new EpicManifestFile(relativePath, fileHash, parts, fileOffset, installTags));
            }

            var chunkDirectory = featureLevel >= 15
                ? "ChunksV4"
                : featureLevel >= 6
                    ? "ChunksV3"
                    : featureLevel >= 3
                        ? "ChunksV2"
                        : "Chunks";
            var chunks = new Dictionary<string, EpicManifestChunk>(StringComparer.OrdinalIgnoreCase);
            foreach (var guid in referencedChunks)
            {
                if (!hashByGuid.TryGetValue(guid, out var rollingHash))
                {
                    throw new DirectFabException("Le manifest JSON Epic référence un chunk sans hash.");
                }
                if (!groupByGuid.TryGetValue(guid, out var group))
                {
                    throw new DirectFabException("Le manifest JSON Epic référence un chunk sans groupe.");
                }
                var downloadSize = sizeByGuid.TryGetValue(guid, out var storedSize)
                    ? storedSize
                    : 1_048_576;
                if (downloadSize <= 0 || downloadSize > MaximumManifestBytes * 4L)
                {
                    throw new DirectFabException("La taille d’un chunk du manifest JSON Epic est invalide.");
                }
                var relative =
                    $"{chunkDirectory}/{group:00}/{rollingHash:X16}_{guid.ToUpperInvariant()}.chunk";
                chunks.Add(guid, new EpicManifestChunk(
                    guid,
                    rollingHash,
                    group,
                    downloadSize,
                    CombineDistributionUri(sourceBaseUri, relative)));
            }

            return new EpicDownloadManifest(appName, buildVersion, featureLevel, chunks, files);
        }
        catch (DirectFabException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or OverflowException or FormatException)
        {
            throw new DirectFabException("Le manifest JSON Epic est invalide.", exception);
        }
    }

    private static bool LooksLikeJsonManifest(ReadOnlySpan<byte> bytes)
    {
        var limit = Math.Min(bytes.Length, 8);
        for (var index = 0; index < limit; index++)
        {
            var value = bytes[index];
            if (value == (byte)'{') return true;
            if (value is not (0xEF or 0xBB or 0xBF or (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
            {
                return false;
            }
        }
        return false;
    }

    private static Dictionary<string, ulong> ReadJsonUInt64Map(
        JsonElement root,
        string propertyName,
        bool required)
    {
        var values = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetJsonObject(root, propertyName, required, out var element)) return values;
        foreach (var property in element.EnumerateObject())
        {
            if (values.Count >= MaximumEntries)
            {
                throw new DirectFabException($"Le champ {propertyName} du manifest JSON Epic est trop grand.");
            }
            values[NormalizeJsonGuid(property.Name)] = BinaryPrimitives.ReadUInt64LittleEndian(
                ReadJsonBlob(property.Value, sizeof(ulong), propertyName));
        }
        return values;
    }

    private static Dictionary<string, long> ReadJsonInt64Map(
        JsonElement root,
        string propertyName,
        bool required)
    {
        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetJsonObject(root, propertyName, required, out var element)) return values;
        foreach (var property in element.EnumerateObject())
        {
            if (values.Count >= MaximumEntries)
            {
                throw new DirectFabException($"Le champ {propertyName} du manifest JSON Epic est trop grand.");
            }
            values[NormalizeJsonGuid(property.Name)] = BinaryPrimitives.ReadInt64LittleEndian(
                ReadJsonBlob(property.Value, sizeof(long), propertyName));
        }
        return values;
    }

    private static Dictionary<string, byte> ReadJsonByteMap(
        JsonElement root,
        string propertyName,
        bool required)
    {
        var values = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetJsonObject(root, propertyName, required, out var element)) return values;
        foreach (var property in element.EnumerateObject())
        {
            if (values.Count >= MaximumEntries)
            {
                throw new DirectFabException($"Le champ {propertyName} du manifest JSON Epic est trop grand.");
            }
            values[NormalizeJsonGuid(property.Name)] = ReadJsonBlob(property.Value, 1, propertyName)[0];
        }
        return values;
    }

    private static bool TryGetJsonObject(
        JsonElement root,
        string propertyName,
        bool required,
        out JsonElement value)
    {
        if (root.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }
        if (required)
        {
            throw new DirectFabException($"Le champ {propertyName} du manifest JSON Epic est absent.");
        }
        value = default;
        return false;
    }

    private static JsonElement GetRequiredJsonProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            throw new DirectFabException($"Le champ {propertyName} du manifest JSON Epic est absent.");
        }
        return value;
    }

    private static string ReadRequiredJsonString(JsonElement element, string propertyName)
    {
        var value = ReadOptionalJsonString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumManifestBytes)
        {
            throw new DirectFabException($"Le champ {propertyName} du manifest JSON Epic est invalide.");
        }
        return value;
    }

    private static string? ReadOptionalJsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static uint ReadJsonBlobUInt32(JsonElement element, string fieldName) =>
        BinaryPrimitives.ReadUInt32LittleEndian(ReadJsonBlob(element, sizeof(uint), fieldName));

    private static byte[] ReadJsonBlob(JsonElement element, int byteCount, string fieldName)
    {
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } encoded ||
            encoded.Length != byteCount * 3)
        {
            throw new DirectFabException($"Le champ {fieldName} du manifest JSON Epic est invalide.");
        }
        var bytes = new byte[byteCount];
        for (var index = 0; index < byteCount; index++)
        {
            var offset = index * 3;
            var first = encoded[offset] - '0';
            var second = encoded[offset + 1] - '0';
            var third = encoded[offset + 2] - '0';
            if (first is < 0 or > 9 || second is < 0 or > 9 || third is < 0 or > 9)
            {
                throw new DirectFabException($"Le champ {fieldName} du manifest JSON Epic est invalide.");
            }
            var value = first * 100 + second * 10 + third;
            if (value > byte.MaxValue)
            {
                throw new DirectFabException($"Le champ {fieldName} du manifest JSON Epic est invalide.");
            }
            bytes[index] = (byte)value;
        }
        return bytes;
    }

    private static string NormalizeJsonGuid(string value)
    {
        if (!Guid.TryParse(value, out var guid) || guid == Guid.Empty)
        {
            throw new DirectFabException("Un GUID du manifest JSON Epic est invalide.");
        }
        return guid.ToString("N", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int ReadCount(EpicBinaryReader reader)
    {
        var value = reader.ReadUInt32();
        if (value > MaximumEntries)
        {
            throw new DirectFabException("Le manifest Fab dépasse les limites de sécurité.");
        }
        return checked((int)value);
    }

    private static Uri CombineDistributionUri(Uri sourceBaseUri, string relativePath)
    {
        var builder = new UriBuilder(sourceBaseUri);
        var query = builder.Query;
        builder.Query = string.Empty;
        if (!builder.Path.EndsWith('/')) builder.Path += "/";
        var combined = new Uri(builder.Uri, relativePath);
        if (string.IsNullOrEmpty(query))
        {
            return combined;
        }

        var combinedBuilder = new UriBuilder(combined)
        {
            Query = query.TrimStart('?'),
        };
        return combinedBuilder.Uri;
    }

    private static void ValidateChunkSize(string guid, byte[] data, uint? expected)
    {
        if (expected is not null && data.Length != expected.Value)
        {
            throw new DirectFabException($"La taille décompressée du chunk {guid} est invalide.");
        }
    }

    private sealed class EpicBinaryReader
    {
        private readonly ReadOnlyMemory<byte> _data;

        public EpicBinaryReader(ReadOnlyMemory<byte> data) => _data = data;

        public int Position { get; private set; }

        public int Remaining => _data.Length - Position;

        public byte ReadByte() => ReadBytes(1).Span[0];

        public uint ReadUInt32() => BitConverter.ToUInt32(ReadBytes(4).Span);

        public int ReadInt32() => BitConverter.ToInt32(ReadBytes(4).Span);

        public ulong ReadUInt64() => BitConverter.ToUInt64(ReadBytes(8).Span);

        public long ReadInt64() => BitConverter.ToInt64(ReadBytes(8).Span);

        public ReadOnlyMemory<byte> ReadBytes(int length)
        {
            if (length < 0 || length > Remaining)
            {
                throw new DirectFabException("Le manifest Fab est tronqué.");
            }
            var result = _data.Slice(Position, length);
            Position += length;
            return result;
        }

        public void Skip(int length) => _ = ReadBytes(length);

        public string ReadGuid() => string.Concat(
            ReadUInt32().ToString("x8", System.Globalization.CultureInfo.InvariantCulture),
            ReadUInt32().ToString("x8", System.Globalization.CultureInfo.InvariantCulture),
            ReadUInt32().ToString("x8", System.Globalization.CultureInfo.InvariantCulture),
            ReadUInt32().ToString("x8", System.Globalization.CultureInfo.InvariantCulture));

        public string ReadFString()
        {
            var length = ReadInt32();
            if (length == 0) return string.Empty;
            if (length > 0)
            {
                if (length > MaximumManifestBytes) throw new DirectFabException("Une chaîne du manifest Fab est trop longue.");
                var bytes = ReadBytes(length).Span;
                return Encoding.UTF8.GetString(bytes[..^1]);
            }

            var characterCount = checked(-length);
            if (characterCount > MaximumManifestBytes / 2) throw new DirectFabException("Une chaîne du manifest Fab est trop longue.");
            var bytesUtf16 = ReadBytes(checked(characterCount * 2)).Span;
            return Encoding.Unicode.GetString(bytesUtf16[..^2]);
        }
    }
}

public sealed record EpicDownloadManifest(
    string AppName,
    string BuildVersion,
    uint Version,
    IReadOnlyDictionary<string, EpicManifestChunk> Chunks,
    IReadOnlyList<EpicManifestFile> Files)
{
    public long DownloadSize => Chunks.Values.Sum(chunk => chunk.DownloadSize);
}

public sealed record EpicManifestChunk(
    string ChunkId,
    ulong RollingHash,
    byte Group,
    long DownloadSize,
    Uri DownloadUri);

public sealed record EpicManifestFile(
    string RelativePath,
    byte[] Sha1,
    IReadOnlyList<EpicManifestFilePart> Parts,
    long Size,
    IReadOnlyList<string> InstallTags);

public sealed record EpicManifestFilePart(
    string ChunkGuid,
    uint Offset,
    uint Size,
    long FileOffset);
