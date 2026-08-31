namespace CyEngineHub.AssetBridge.Tool;

internal static class Program
{
    private const int MaximumFileCount = 10_000;
    private const long MaximumTotalBytes = 20L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(
        [
            ".fbx", ".obj", ".gltf", ".glb",
            ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".hdr",
            ".wav", ".ogg",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            var input = GetRequiredPath(options, "--input");
            var output = GetRequiredPath(options, "--output");
            var sourceEngine = GetRequired(options, "--source-engine");
            var targetEngine = GetRequired(options, "--target-engine");
            ValidateRoots(input, output);
            var files = Inventory(input);
            if (files.Length == 0)
            {
                Console.Error.WriteLine("Aucun format d’échange compatible n’a été trouvé.");
                return 2;
            }

            var collisions = files
                .Select(file => CombineContained(output, file.RelativePath))
                .Where(File.Exists)
                .Take(10)
                .ToArray();
            if (collisions.Length > 0)
            {
                Console.Error.WriteLine("Copie refusée : des fichiers existent déjà dans la sortie.");
                foreach (var collision in collisions)
                {
                    Console.Error.WriteLine(collision);
                }

                return 3;
            }

            Console.WriteLine(
                $"Asset Bridge {sourceEngine} -> {targetEngine}: {files.Length} fichier(s), " +
                $"{files.Sum(file => file.Length)} octet(s).");
            await CopyTransactionallyAsync(input, output, files).ConfigureAwait(false);
            Console.WriteLine("Transfert terminé. Vérifiez puis importez les fichiers dans l’éditeur cible.");
            return 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
                InvalidOperationException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Arguments attendus : --input, --output, --source-engine et --target-engine.");
            }

            if (!options.TryAdd(args[index], args[index + 1]))
            {
                throw new ArgumentException($"Argument dupliqué : {args[index]}.");
            }
        }

        return options;
    }

    private static string GetRequired(
        IReadOnlyDictionary<string, string> options,
        string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Argument requis absent : {key}.");

    private static string GetRequiredPath(
        IReadOnlyDictionary<string, string> options,
        string key) =>
        Path.GetFullPath(GetRequired(options, key).Trim().Trim('"'));

    private static void ValidateRoots(string input, string output)
    {
        if ((!File.Exists(input) && !Directory.Exists(input)) || IsReparsePoint(input))
        {
            throw new ArgumentException("La source est absente ou constitue un lien symbolique.");
        }

        var outputRoot = Path.GetPathRoot(output)!;
        if (SamePath(output, outputRoot) ||
            IsSameOrChild(output, input) ||
            IsSameOrChild(input, output) ||
            (Directory.Exists(output) && IsReparsePoint(output)))
        {
            throw new ArgumentException("La sortie doit être un dossier séparé et non symbolique.");
        }
    }

    private static BridgeFile[] Inventory(string input)
    {
        if (File.Exists(input))
        {
            var file = new FileInfo(input);
            return AllowedExtensions.Contains(file.Extension)
                ? [new BridgeFile(file.FullName, file.Name, file.Length)]
                : [];
        }

        var files = new List<BridgeFile>();
        var directories = new Stack<string>();
        directories.Push(input);
        long totalBytes = 0;
        while (directories.Count > 0)
        {
            var directory = directories.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         directory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Push(entry);
                    continue;
                }

                var info = new FileInfo(entry);
                if (!AllowedExtensions.Contains(info.Extension))
                {
                    continue;
                }

                totalBytes = checked(totalBytes + info.Length);
                if (files.Count >= MaximumFileCount || totalBytes > MaximumTotalBytes)
                {
                    throw new InvalidOperationException(
                        "Le transfert dépasse 10 000 fichiers ou 20 Gio.");
                }

                files.Add(new BridgeFile(
                    info.FullName,
                    Path.GetRelativePath(input, info.FullName),
                    info.Length));
            }
        }

        return files
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task CopyTransactionallyAsync(
        string input,
        string output,
        IReadOnlyList<BridgeFile> files)
    {
        Directory.CreateDirectory(output);
        var staging = CombineContained(output, $".cyenginehub-bridge-{Guid.NewGuid():N}");
        var createdFiles = new List<string>();
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in files)
            {
                var staged = CombineContained(staging, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                await using var sourceStream = new FileStream(
                    file.SourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var targetStream = new FileStream(
                    staged,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await sourceStream.CopyToAsync(targetStream).ConfigureAwait(false);
                await targetStream.FlushAsync().ConfigureAwait(false);
                if (targetStream.Length != file.Length)
                {
                    throw new IOException($"Copie incomplète : {file.RelativePath}.");
                }
            }

            foreach (var file in files)
            {
                var staged = CombineContained(staging, file.RelativePath);
                var destination = CombineContained(output, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(staged, destination);
                createdFiles.Add(destination);
            }
        }
        catch
        {
            foreach (var created in createdFiles.AsEnumerable().Reverse())
            {
                try
                {
                    File.Delete(created);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    // Continuer le rollback des autres fichiers.
                }
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static string CombineContained(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var combined = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!combined.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Un chemin de fichier sort du dossier de destination.");
        }

        return combined;
    }

    private static bool IsSameOrChild(string candidate, string parent)
    {
        var normalizedCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return SamePath(normalizedCandidate, normalizedParent) ||
               normalizedCandidate.StartsWith(
                   normalizedParent + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private sealed record BridgeFile(string SourcePath, string RelativePath, long Length);
}
