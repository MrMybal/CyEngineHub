using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Fab.Storage;

namespace CyEngineHub.Fab.Library;

public interface IFabLocalContentLibraryService
{
    Task<IReadOnlyList<FabLocalContentPack>> DiscoverAsync(
        FabLocalLibraryRequest request,
        IProgress<FabLocalLibraryProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class FabLocalContentLibraryService : IFabLocalContentLibraryService
{
    private static readonly HashSet<string> UnrealSupportedExtensions = new(
        [
            ".uasset", ".umap", ".uexp", ".ubulk", ".uptnl",
            ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".exr", ".hdr", ".dds",
            ".tif", ".tiff", ".fbx", ".obj", ".gltf", ".glb", ".usd", ".usda",
            ".usdc", ".abc", ".wav", ".ogg", ".mp3", ".flac", ".json", ".csv",
            ".txt", ".md",
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> PortableSupportedExtensions = new(
        [
            ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".exr", ".hdr", ".dds",
            ".tif", ".tiff", ".fbx", ".obj", ".gltf", ".glb", ".usd", ".usda",
            ".usdc", ".abc", ".wav", ".ogg", ".mp3", ".flac", ".json", ".csv",
            ".txt", ".md",
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ExcludedPackageDirectories = new(
        [
            ".git", ".vs", "Binaries", "Intermediate", "Saved", "DerivedDataCache",
            "Library", "Packages", "ProjectSettings", ".godot", "Content", "Assets",
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> CryEngineExcludedInventoryDirectories = new(
        [".git", ".vs", ".cyenginehub", "Solutions", "USER"],
        StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<FabLocalContentPack>> DiscoverAsync(
        FabLocalLibraryRequest request,
        IProgress<FabLocalLibraryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        return Task.Run<IReadOnlyList<FabLocalContentPack>>(
            () => Discover(request, progress, cancellationToken),
            cancellationToken);
    }

    private static FabLocalContentPack[] Discover(
        FabLocalLibraryRequest request,
        IProgress<FabLocalLibraryProgress>? progress,
        CancellationToken cancellationToken)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var roots = request.Roots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .Distinct(comparer)
            .ToArray();
        var packs = new Dictionary<string, FabLocalContentPack>(comparer);
        for (var rootIndex = 0; rootIndex < roots.Length && packs.Count < request.MaximumPacks; rootIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = roots[rootIndex];
            progress?.Report(new FabLocalLibraryProgress(
                rootIndex,
                roots.Length,
                $"Bibliothèque locale · {Path.GetFileName(root)}"));
            if (!Directory.Exists(root) || IsReparsePoint(root))
            {
                continue;
            }

            var directParent = Directory.GetParent(root)?.FullName ?? root;
            var directKind = Path.GetFileName(root) switch
            {
                var name when string.Equals(name, "Content", StringComparison.OrdinalIgnoreCase) => FabLocalContentKind.Unreal,
                var name when string.Equals(name, "Assets", StringComparison.OrdinalIgnoreCase) &&
                              ReadCryEnginePackage(directParent) is not null => FabLocalContentKind.CryEngine,
                var name when string.Equals(name, "Assets", StringComparison.OrdinalIgnoreCase) => FabLocalContentKind.Unity,
                _ => (FabLocalContentKind?)null,
            };
            if (directKind is { } kind)
            {
                var directPack = InspectPackage(
                    directParent,
                    root,
                    kind,
                    request,
                    cancellationToken);
                if (directPack is not null)
                {
                    packs.TryAdd(directPack.ContentDirectory, directPack);
                }

                continue;
            }

            var packCountBeforeRoot = packs.Count;
            foreach (var candidate in EnumeratePackageRoots(
                         root,
                         request.MaximumDepth,
                         request.MaximumDirectoriesPerRoot,
                         cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (packs.Count >= request.MaximumPacks)
                {
                    break;
                }

                var pack = InspectPackage(
                    candidate.PackageRoot,
                    candidate.ContentDirectory,
                    candidate.Kind,
                    request,
                    cancellationToken);
                if (pack is not null)
                {
                    packs.TryAdd(pack.ContentDirectory, pack);
                }
            }

            // Un dossier configuré peut être une simple collection de FBX, textures ou audio,
            // sans descripteur propre à un moteur. Dans ce cas il devient un pack portable.
            if (packs.Count == packCountBeforeRoot && packs.Count < request.MaximumPacks)
            {
                var genericPack = InspectPackage(
                    root,
                    root,
                    FabLocalContentKind.Generic,
                    request,
                    cancellationToken);
                if (genericPack is not null)
                {
                    packs.TryAdd(genericPack.ContentDirectory, genericPack);
                }
            }
        }

        progress?.Report(new FabLocalLibraryProgress(
            roots.Length,
            roots.Length,
            $"{packs.Count:N0} pack(s) moteur ou portable(s)"));
        return packs.Values
            .OrderBy(pack => pack.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pack => pack.PackageRoot, comparer)
            .ToArray();
    }

    private static IEnumerable<PackageCandidate> EnumeratePackageRoots(
        string root,
        int maximumDepth,
        int maximumDirectories,
        CancellationToken cancellationToken)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;
        while (queue.Count > 0 && visited < maximumDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = queue.Dequeue();
            visited++;
            if (IsReparsePoint(current.Path))
            {
                continue;
            }

            var cryEngine = ReadCryEnginePackage(current.Path);
            var godotMarker = Path.Combine(current.Path, "project.godot");
            var epicVaultManifest = FindEpicVaultManifest(current.Path);
            if (epicVaultManifest is not null)
            {
                yield return new PackageCandidate(
                    current.Path,
                    epicVaultManifest,
                    FabLocalContentKind.EpicVault);
                continue;
            }
            else if (cryEngine is not null)
            {
                yield return new PackageCandidate(
                    current.Path,
                    cryEngine.ContentDirectory,
                    FabLocalContentKind.CryEngine);
            }
            else if (File.Exists(godotMarker) && !IsReparsePoint(godotMarker))
            {
                yield return new PackageCandidate(current.Path, current.Path, FabLocalContentKind.Godot);
            }
            else
            {
                var content = Path.Combine(current.Path, "Content");
                if (Directory.Exists(content) && !IsReparsePoint(content))
                {
                    yield return new PackageCandidate(current.Path, content, FabLocalContentKind.Unreal);
                }

                var assets = Path.Combine(current.Path, "Assets");
                if (Directory.Exists(assets) && !IsReparsePoint(assets))
                {
                    yield return new PackageCandidate(current.Path, assets, FabLocalContentKind.Unity);
                }
            }

            if (current.Depth >= maximumDepth)
            {
                continue;
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(current.Path);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (ExcludedPackageDirectories.Contains(Path.GetFileName(child)) ||
                    IsReparsePoint(child))
                {
                    continue;
                }

                queue.Enqueue((child, current.Depth + 1));
            }
        }
    }

    private static FabLocalContentPack? InspectPackage(
        string packageRoot,
        string contentDirectory,
        FabLocalContentKind kind,
        FabLocalLibraryRequest request,
        CancellationToken cancellationToken)
    {
        if (kind == FabLocalContentKind.EpicVault)
        {
            return InspectEpicVaultPackage(
                packageRoot,
                contentDirectory,
                request,
                cancellationToken);
        }

        try
        {
            if (kind == FabLocalContentKind.Unreal &&
                Directory.EnumerateFiles(packageRoot, "*.uplugin", SearchOption.TopDirectoryOnly).Any())
            {
                return null;
            }
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException)
        {
            return null;
        }

        ContentInventory inventory;
        try
        {
            inventory = InventoryContent(
                kind == FabLocalContentKind.CryEngine ? packageRoot : contentDirectory,
                kind,
                request,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException)
        {
            return null;
        }

        if (inventory.AssetCount + inventory.MapCount == 0)
        {
            return null;
        }

        var issues = new List<string>(inventory.Issues);
        string? projectPath = null;
        string? engineAssociation = null;
        if (kind == FabLocalContentKind.Unreal)
        {
            string[] projectFiles;
            try
            {
                projectFiles = Directory.GetFiles(packageRoot, "*.uproject", SearchOption.TopDirectoryOnly)
                    .Take(2)
                    .ToArray();
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                projectFiles = [];
                issues.Add("Descripteur .uproject inaccessible.");
            }

            if (projectFiles.Length == 1)
            {
                projectPath = Path.GetFullPath(projectFiles[0]);
                engineAssociation = ReadEngineAssociation(projectPath);
                if (string.IsNullOrWhiteSpace(engineAssociation)) issues.Add("Version Unreal source non déclarée.");
            }
            else if (projectFiles.Length == 0)
            {
                issues.Add("Aucun .uproject; compatibilité moteur à vérifier pendant l’import.");
            }
            else
            {
                issues.Add("Plusieurs .uproject détectés; source ambiguë.");
            }
        }
        else if (kind == FabLocalContentKind.Unity)
        {
            var versionMarker = Path.Combine(packageRoot, "ProjectSettings", "ProjectVersion.txt");
            if (File.Exists(versionMarker) && !IsReparsePoint(versionMarker))
            {
                projectPath = Path.GetFullPath(versionMarker);
                engineAssociation = ReadUnityVersion(versionMarker);
            }
            if (string.IsNullOrWhiteSpace(engineAssociation))
            {
                issues.Add("Version Unity source non déclarée; pack Assets autonome.");
            }
            if (inventory.BlockedFileCount > 0)
            {
                issues.Add($"{inventory.BlockedFileCount:N0} fichier(s) de code, shader ou binaire bloqué(s); import refusé tant qu’ils sont présents.");
            }
        }
        else if (kind == FabLocalContentKind.Godot)
        {
            projectPath = Path.Combine(packageRoot, "project.godot");
            engineAssociation = ReadGodotVersion(projectPath);
            if (string.IsNullOrWhiteSpace(engineAssociation))
            {
                issues.Add("Version Godot source non déclarée dans config/features.");
            }
            if (inventory.BlockedFileCount > 0)
            {
                issues.Add($"{inventory.BlockedFileCount:N0} script(s), shader(s) ou binaire(s) Godot bloqué(s); import refusé tant qu’ils sont présents.");
            }
        }
        else if (kind == FabLocalContentKind.CryEngine)
        {
            var descriptor = ReadCryEnginePackage(packageRoot);
            if (descriptor is null)
            {
                return null;
            }

            projectPath = descriptor.MarkerPath;
            engineAssociation = descriptor.EngineRequirement;
            if (string.IsNullOrWhiteSpace(engineAssociation))
            {
                issues.Add("Engine CRYENGINE source non déclaré dans require/engine.");
            }
            if (inventory.BlockedFileCount > 0)
            {
                issues.Add($"{inventory.BlockedFileCount:N0} fichier(s) de code, script, shader ou binaire CRYENGINE bloqué(s); import refusé tant qu’ils sont présents.");
            }
        }
        else
        {
            issues.Add("Pack portable sans moteur source; choisissez le moteur cible au moment de l’import.");
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
        var normalizedContent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentDirectory));
        var cryEngineName = kind == FabLocalContentKind.CryEngine
            ? ReadCryEnginePackage(packageRoot)?.Name
            : null;
        var displayName = (kind == FabLocalContentKind.Unreal || kind == FabLocalContentKind.CryEngine) && projectPath is not null
            ? string.IsNullOrWhiteSpace(cryEngineName)
                ? Path.GetFileNameWithoutExtension(projectPath)
                : cryEngineName
            : Path.GetFileName(normalizedRoot);
        var health = inventory.IsPartial
            ? FabLocalContentPackHealth.Partial
            : issues.Count == 0
                ? FabLocalContentPackHealth.Ready
                : FabLocalContentPackHealth.Warning;
        return new FabLocalContentPack(
            CreateStableId(normalizedContent),
            displayName,
            normalizedRoot,
            normalizedContent,
            projectPath,
            engineAssociation,
            inventory.AssetCount,
            inventory.MapCount,
            inventory.SupportedFileCount,
            inventory.SupportedBytes,
            inventory.LastModifiedAt,
            health,
            issues,
            DateTimeOffset.UtcNow,
            kind,
            inventory.BlockedFileCount);
    }

    private static FabLocalContentPack? InspectEpicVaultPackage(
        string packageRoot,
        string manifestPath,
        FabLocalLibraryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var inspection = EpicVaultNativeStoreService.Inspect(
                packageRoot,
                manifestPath,
                request.MaximumFilesPerPack,
                request.MaximumBytesPerPack,
                cancellationToken);
            if (inspection.FileCount == 0)
            {
                return null;
            }
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
            var issues = inspection.Issues.ToList();
            if (inspection.AssetCount + inspection.MapCount == 0)
            {
                issues.Add("Package Epic sans asset Unreal natif détecté; le type sera confirmé après reconstruction.");
            }
            return new FabLocalContentPack(
                CreateStableId(Path.GetFullPath(manifestPath)),
                inspection.DisplayName,
                normalizedRoot,
                normalizedRoot,
                Path.GetFullPath(manifestPath),
                inspection.BuildVersion,
                inspection.AssetCount,
                inspection.MapCount,
                inspection.FileCount,
                inspection.UncompressedBytes,
                inspection.LastModifiedAt,
                inspection.IsPartial
                    ? FabLocalContentPackHealth.Partial
                    : issues.Count == 0
                        ? FabLocalContentPackHealth.Ready
                        : FabLocalContentPackHealth.Warning,
                issues,
                DateTimeOffset.UtcNow,
                FabLocalContentKind.EpicVault);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectFabException)
        {
            return null;
        }
    }

    private static string? FindEpicVaultManifest(string packageRoot)
    {
        try
        {
            var direct = FindManifestInDirectory(packageRoot);
            if (direct is not null)
            {
                return direct;
            }
            string[][] knownRelativeDirectories =
            [
                ["manifests"],
                ["manifest"],
                ["data"],
                ["metadata"],
                ["Data", "Manifests"],
                ["Data", "Manifest"],
                [".egstore", "Manifests"],
                [".egstore", "Manifest"],
                ["CloudDir", "Manifests"],
            ];
            foreach (var segments in knownRelativeDirectories)
            {
                var child = segments.Aggregate(packageRoot, Path.Combine);
                if (!Directory.Exists(child) || IsReparsePoint(child))
                {
                    continue;
                }
                var manifest = FindManifestInDirectory(child);
                if (manifest is not null)
                {
                    return manifest;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return null;
    }

    private static string? FindManifestInDirectory(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path =>
                Path.GetExtension(path).Equals(".manifest", StringComparison.OrdinalIgnoreCase) &&
                !IsReparsePoint(path));

    private static ContentInventory InventoryContent(
        string contentDirectory,
        FabLocalContentKind kind,
        FabLocalLibraryRequest request,
        CancellationToken cancellationToken)
    {
        var directories = new Stack<string>();
        directories.Push(contentDirectory);
        var visitedDirectories = 0;
        var assetCount = 0;
        var mapCount = 0;
        var fileCount = 0;
        var inspectedFileCount = 0;
        var blockedFileCount = 0;
        long bytes = 0;
        var lastModified = DateTimeOffset.MinValue;
        var issues = new List<string>();
        var partial = false;
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = directories.Pop();
            if (IsReparsePoint(current))
            {
                partial = true;
                issues.Add("Lien ou point de réanalyse ignoré dans le contenu.");
                continue;
            }

            visitedDirectories++;
            if (visitedDirectories > request.MaximumDirectoriesPerRoot)
            {
                partial = true;
                issues.Add("Inventaire tronqué par la limite de dossiers.");
                break;
            }

            string[] children;
            string[] files;
            try
            {
                children = Directory.GetDirectories(current);
                files = Directory.GetFiles(current);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException)
            {
                partial = true;
                issues.Add("Un sous-dossier de contenu est inaccessible.");
                continue;
            }

            foreach (var child in children)
            {
                if ((kind == FabLocalContentKind.Godot || kind == FabLocalContentKind.CryEngine) &&
                    (kind == FabLocalContentKind.Godot
                        ? ExcludedPackageDirectories.Contains(Path.GetFileName(child))
                        : CryEngineExcludedInventoryDirectories.Contains(Path.GetFileName(child))))
                {
                    continue;
                }
                if (!IsReparsePoint(child))
                {
                    directories.Push(child);
                }
                else
                {
                    partial = true;
                    issues.Add("Lien ou point de réanalyse ignoré dans le contenu.");
                }
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (inspectedFileCount >= request.MaximumFilesPerPack)
                {
                    partial = true;
                    issues.Add("Inventaire tronqué par la limite de fichiers inspectés.");
                    directories.Clear();
                    break;
                }
                inspectedFileCount++;
                var extension = Path.GetExtension(file);
                var blocked = kind switch
                {
                    FabLocalContentKind.Unity => UnityFabAssetPolicy.IsBlocked(extension),
                    FabLocalContentKind.Godot => GodotFabAssetPolicy.IsBlocked(extension),
                    FabLocalContentKind.CryEngine => CryEngineFabAssetPolicy.IsBlocked(extension),
                    FabLocalContentKind.Generic =>
                        UnityFabAssetPolicy.IsBlocked(extension) ||
                        GodotFabAssetPolicy.IsBlocked(extension) ||
                        CryEngineFabAssetPolicy.IsBlocked(extension),
                    _ => false,
                };
                if (blocked)
                {
                    blockedFileCount++;
                    continue;
                }
                var supported = kind switch
                {
                    FabLocalContentKind.Unreal => UnrealSupportedExtensions.Contains(extension),
                    FabLocalContentKind.Unity => UnityFabAssetPolicy.IsSupported(extension),
                    FabLocalContentKind.Godot => GodotFabAssetPolicy.IsSupported(extension),
                    FabLocalContentKind.CryEngine => CryEngineFabAssetPolicy.IsSupported(extension),
                    _ => PortableSupportedExtensions.Contains(extension),
                };
                if (!supported)
                {
                    continue;
                }

                var info = new FileInfo(file);
                fileCount++;
                bytes = checked(bytes + info.Length);
                if (bytes > request.MaximumBytesPerPack)
                {
                    partial = true;
                    issues.Add("Inventaire tronqué par la limite de taille.");
                    directories.Clear();
                    break;
                }

                if (kind == FabLocalContentKind.Unreal && string.Equals(info.Extension, ".uasset", StringComparison.OrdinalIgnoreCase))
                {
                    assetCount++;
                }
                else if (kind == FabLocalContentKind.Unreal && string.Equals(info.Extension, ".umap", StringComparison.OrdinalIgnoreCase))
                {
                    mapCount++;
                }
                else if (kind == FabLocalContentKind.Unity && string.Equals(info.Extension, ".unity", StringComparison.OrdinalIgnoreCase))
                {
                    mapCount++;
                }
                else if (kind == FabLocalContentKind.Unity && !string.Equals(info.Extension, ".meta", StringComparison.OrdinalIgnoreCase))
                {
                    assetCount++;
                }
                else if (kind is FabLocalContentKind.Godot or FabLocalContentKind.CryEngine or FabLocalContentKind.Generic)
                {
                    assetCount++;
                }

                if (info.LastWriteTimeUtc > lastModified)
                {
                    lastModified = info.LastWriteTimeUtc;
                }
            }
        }

        return new ContentInventory(
            assetCount,
            mapCount,
            fileCount,
            bytes,
            lastModified == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : lastModified,
            partial,
            issues.Distinct(StringComparer.Ordinal).ToArray(),
            blockedFileCount);
    }

    private static string? ReadEngineAssociation(string projectPath)
    {
        try
        {
            var info = new FileInfo(projectPath);
            if (!info.Exists || info.Length > 2 * 1024 * 1024)
            {
                return null;
            }

            using var stream = new FileStream(
                projectPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("EngineAssociation", out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string? ReadUnityVersion(string markerPath)
    {
        try
        {
            var info = new FileInfo(markerPath);
            if (!info.Exists || info.Length > 128 * 1024)
            {
                return null;
            }
            foreach (var line in File.ReadLines(markerPath).Take(32))
            {
                const string prefix = "m_EditorVersion:";
                if (line.TrimStart().StartsWith(prefix, StringComparison.Ordinal))
                {
                    var value = line[(line.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length)..].Trim();
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadGodotVersion(string markerPath)
    {
        try
        {
            var info = new FileInfo(markerPath);
            if (!info.Exists || info.Length > 2 * 1024 * 1024)
            {
                return null;
            }
            foreach (var line in File.ReadLines(markerPath).Take(4_096))
            {
                if (!line.Contains("config/features", StringComparison.Ordinal))
                {
                    continue;
                }
                var match = Regex.Match(
                    line,
                    "\"(?<version>[0-9]+(?:\\.[0-9]+)?)\"",
                    RegexOptions.CultureInvariant);
                if (match.Success)
                {
                    return match.Groups["version"].Value;
                }
            }
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static CryEnginePackageDescriptor? ReadCryEnginePackage(string packageRoot)
    {
        try
        {
            var markers = Directory.EnumerateFiles(
                    packageRoot,
                    "*.cryproject",
                    SearchOption.TopDirectoryOnly)
                .Take(2)
                .ToArray();
            if (markers.Length != 1 || IsReparsePoint(markers[0]))
            {
                return null;
            }

            var markerInfo = new FileInfo(markers[0]);
            if (markerInfo.Length > 1024 * 1024)
            {
                return null;
            }

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(markers[0]),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                    MaxDepth = 64,
                });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(root, "content", out var content) ||
                content.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(content, "assets", out var assets) ||
                assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var relativeAssets = assets.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()?.Trim())
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            if (string.IsNullOrWhiteSpace(relativeAssets) || Path.IsPathRooted(relativeAssets))
            {
                return null;
            }

            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
            var contentDirectory = Path.GetFullPath(Path.Combine(normalizedRoot, relativeAssets));
            var relative = Path.GetRelativePath(normalizedRoot, contentDirectory);
            if (Path.IsPathRooted(relative) ||
                relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                !Directory.Exists(contentDirectory) ||
                IsReparsePoint(contentDirectory))
            {
                return null;
            }

            var name = TryGetProperty(root, "info", out var info) &&
                       info.ValueKind == JsonValueKind.Object &&
                       TryGetProperty(info, "name", out var nameValue) &&
                       nameValue.ValueKind == JsonValueKind.String
                ? nameValue.GetString()?.Trim()
                : null;
            var engine = TryGetProperty(root, "require", out var requirement) &&
                         requirement.ValueKind == JsonValueKind.Object &&
                         TryGetProperty(requirement, "engine", out var engineValue) &&
                         engineValue.ValueKind == JsonValueKind.String
                ? engineValue.GetString()?.Trim()
                : null;
            return new CryEnginePackageDescriptor(
                Path.GetFullPath(markers[0]),
                contentDirectory,
                name,
                engine);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string CreateStableId(string path)
    {
        var normalized = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24];
    }

    private static void ValidateRequest(FabLocalLibraryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Roots);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaximumDepth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaximumDirectoriesPerRoot, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaximumPacks, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaximumFilesPerPack, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaximumBytesPerPack, 1);
    }

    private sealed record ContentInventory(
        int AssetCount,
        int MapCount,
        int SupportedFileCount,
        long SupportedBytes,
        DateTimeOffset LastModifiedAt,
        bool IsPartial,
        IReadOnlyList<string> Issues,
        int BlockedFileCount);

    private sealed record PackageCandidate(
        string PackageRoot,
        string ContentDirectory,
        FabLocalContentKind Kind);

    private sealed record CryEnginePackageDescriptor(
        string MarkerPath,
        string ContentDirectory,
        string? Name,
        string? EngineRequirement);
}
