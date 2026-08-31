using System.Security.Cryptography;
using System.Text.Json;
using CyEngineHub.Core.Processes;
using CyEngineHub.Engines;

namespace CyEngineHub.Extensions;

public interface IHubExtensionExecutionService
{
    Task<HubExtensionExecutionPlan> PrepareAsync(
        HubExtensionSnapshot extension,
        string inputPath,
        string outputDirectory,
        EngineFamily sourceEngine,
        EngineFamily targetEngine,
        CancellationToken cancellationToken = default);

    Task<HubExtensionExecutionResult> ExecuteAsync(
        HubExtensionExecutionPlan plan,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class HubExtensionExecutionService(IProcessRunner processRunner)
    : IHubExtensionExecutionService
{
    private const string TrustedAssetBridgeId = "cyenginehub.asset-bridge";
    private const int MaximumInspectedEntries = 50_000;
    private const int MaximumTransferableFiles = 10_000;
    private const long MaximumTransferableBytes = 20L * 1024 * 1024 * 1024;
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(10);
    private static readonly HashSet<string> PortableExtensions = new(
        [
            ".fbx", ".obj", ".gltf", ".glb",
            ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".hdr",
            ".wav", ".ogg",
        ],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> EngineNativeExtensions = new(
        [
            ".uasset", ".umap",
            ".asset", ".prefab", ".unity", ".mat", ".controller", ".anim", ".overridecontroller",
            ".meta", ".asmdef", ".asmref", ".shader", ".compute", ".hlsl", ".cginc",
            ".tscn", ".scn", ".tres", ".res", ".gd", ".gdshader", ".godot", ".import", ".uid",
            ".cs",
        ],
        StringComparer.OrdinalIgnoreCase);

    public async Task<HubExtensionExecutionPlan> PrepareAsync(
        HubExtensionSnapshot extension,
        string inputPath,
        string outputDirectory,
        EngineFamily sourceEngine,
        EngineFamily targetEngine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (!IsTrustedAssetBridge(extension, sourceEngine, targetEngine))
        {
            throw new HubExtensionException(
                "Cette extension n’est pas exécutable pour les moteurs sélectionnés.",
                "PrepareExecution",
                extension.RootDirectory);
        }

        var root = Path.GetFullPath(extension.RootDirectory);
        if (!Directory.Exists(root) || HubExtensionManifestParser.IsReparsePoint(root))
        {
            throw new HubExtensionException(
                "Le dossier de l’extension est absent ou non sûr.",
                "PrepareExecution",
                root);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        var input = Path.GetFullPath(inputPath.Trim().Trim('"'));
        if (!File.Exists(input) && !Directory.Exists(input))
        {
            throw new HubExtensionException(
                "La source de l’outil n’existe pas.",
                "PrepareExecution",
                input);
        }

        var inputSummary = InspectInput(input, cancellationToken);
        if (inputSummary.TransferableFileCount == 0)
        {
            throw new HubExtensionException(
                "Aucun format d’échange transférable n’a été trouvé dans la source.",
                "PrepareExecution",
                input);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var planId = Guid.NewGuid();
        var output = ResolveTargetOutput(
            outputDirectory,
            targetEngine,
            input,
            planId);
        if (!extension.Manifest.WritesToOutput)
        {
            output = string.IsNullOrWhiteSpace(outputDirectory) ? input : output;
        }
        else if (IsVolumeRoot(output) ||
                 IsSameOrChild(output, root) ||
                 IsSameOrChild(output, input) ||
                 IsSameOrChild(input, output))
        {
            throw new HubExtensionException(
                "La sortie doit être un dossier séparé de la source et de l’extension.",
                "PrepareExecution",
                output);
        }

        var executable = HubExtensionManifestParser.CombineContained(
            root,
            extension.Manifest.EntryPoint!);
        if (!string.Equals(
                Path.GetFileName(executable),
                "CyEngineHub.AssetBridge.Tool.exe",
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(executable) ||
            HubExtensionManifestParser.IsReparsePoint(executable))
        {
            throw new HubExtensionException(
                "L’exécutable de l’extension est absent ou non sûr.",
                "PrepareExecution",
                executable);
        }

        var arguments = extension.Manifest.Arguments
            .Select(argument => argument
                .Replace("{Input}", input, StringComparison.Ordinal)
                .Replace("{Output}", output, StringComparison.Ordinal)
                .Replace("{SourceEngine}", sourceEngine.ToString(), StringComparison.Ordinal)
                .Replace("{TargetEngine}", targetEngine.ToString(), StringComparison.Ordinal))
            .ToArray();
        var now = DateTimeOffset.UtcNow;
        return new HubExtensionExecutionPlan(
            planId,
            extension,
            executable,
            await ComputeSha256Async(executable, cancellationToken).ConfigureAwait(false),
            arguments,
            input,
            output,
            sourceEngine,
            targetEngine,
            inputSummary,
            now,
            now + PlanLifetime);
    }

    public async Task<HubExtensionExecutionResult> ExecuteAsync(
        HubExtensionExecutionPlan plan,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Id == Guid.Empty || DateTimeOffset.UtcNow > plan.ExpiresAt)
        {
            throw new HubExtensionException(
                "Le plan d’exécution de l’extension a expiré.",
                "ExecuteExtension",
                plan.ExecutablePath);
        }

        if (!IsTrustedAssetBridge(
                plan.Extension,
                plan.SourceEngine,
                plan.TargetEngine))
        {
            throw new HubExtensionException(
                "L’autorisation d’exécution de l’extension n’est plus valide.",
                "ExecuteExtension",
                plan.Extension.RootDirectory);
        }

        var root = Path.GetFullPath(plan.Extension.RootDirectory);
        if (!Directory.Exists(root) || HubExtensionManifestParser.IsReparsePoint(root))
        {
            throw new HubExtensionException(
                "Le dossier de l’extension a changé après confirmation.",
                "ExecuteExtension",
                root);
        }

        var executable = HubExtensionManifestParser.CombineContained(
            root,
            plan.Extension.Manifest.EntryPoint!);
        if (!SamePath(executable, plan.ExecutablePath) ||
            !string.Equals(
                Path.GetFileName(executable),
                "CyEngineHub.AssetBridge.Tool.exe",
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(executable) ||
            HubExtensionManifestParser.IsReparsePoint(executable) ||
            !string.Equals(
                await ComputeSha256Async(executable, cancellationToken).ConfigureAwait(false),
                plan.ExecutableSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new HubExtensionException(
                "L’exécutable de l’extension a changé après confirmation.",
                "ExecuteExtension",
                executable);
        }

        if (!File.Exists(plan.InputPath) && !Directory.Exists(plan.InputPath))
        {
            throw new HubExtensionException(
                "La source a disparu après confirmation.",
                "ExecuteExtension",
                plan.InputPath);
        }

        var currentSummary = InspectInput(plan.InputPath, cancellationToken);
        if (currentSummary != plan.InputSummary)
        {
            throw new HubExtensionException(
                "La sélection de fichiers a changé après confirmation.",
                "ExecuteExtension",
                plan.InputPath);
        }

        var resolvedOutput = ResolveTargetOutput(
            plan.OutputPath,
            plan.TargetEngine,
            plan.InputPath,
            plan.Id);
        if (!SamePath(resolvedOutput, plan.OutputPath))
        {
            throw new HubExtensionException(
                "Le projet cible a changé après confirmation.",
                "ExecuteExtension",
                plan.OutputPath);
        }

        var expectedArguments = plan.Extension.Manifest.Arguments
            .Select(argument => argument
                .Replace("{Input}", plan.InputPath, StringComparison.Ordinal)
                .Replace("{Output}", plan.OutputPath, StringComparison.Ordinal)
                .Replace("{SourceEngine}", plan.SourceEngine.ToString(), StringComparison.Ordinal)
                .Replace("{TargetEngine}", plan.TargetEngine.ToString(), StringComparison.Ordinal))
            .ToArray();
        if (!expectedArguments.SequenceEqual(plan.Arguments, StringComparer.Ordinal))
        {
            throw new HubExtensionException(
                "Les arguments d’exécution ont changé après confirmation.",
                "ExecuteExtension",
                plan.ExecutablePath);
        }

        if (plan.Extension.Manifest.WritesToOutput)
        {
            Directory.CreateDirectory(plan.OutputPath);
        }

        var result = await processRunner.RunAsync(
                new ProcessRequest
                {
                    FileName = executable,
                    Arguments = plan.Arguments,
                    WorkingDirectory = plan.Extension.RootDirectory,
                    MaxCapturedLines = 5_000,
                },
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        return new HubExtensionExecutionResult(plan, result);
    }

    private static bool IsTrustedAssetBridge(
        HubExtensionSnapshot extension,
        EngineFamily sourceEngine,
        EngineFamily targetEngine) =>
        extension.CanRun &&
        extension.Origin == HubExtensionOrigin.BuiltIn &&
        string.Equals(
            extension.Manifest.Id,
            TrustedAssetBridgeId,
            StringComparison.Ordinal) &&
        extension.Manifest.WritesToOutput &&
        !string.IsNullOrWhiteSpace(extension.Manifest.EntryPoint) &&
        extension.Manifest.SupportedEngines.Contains(sourceEngine) &&
        extension.Manifest.SupportedEngines.Contains(targetEngine) &&
        sourceEngine != targetEngine &&
        IsBridgeEngine(sourceEngine) &&
        IsBridgeEngine(targetEngine);

    private static bool IsBridgeEngine(EngineFamily family) =>
        family is EngineFamily.Unreal or EngineFamily.Unity or EngineFamily.Godot or EngineFamily.CryEngine;

    private static HubAssetInspectionSummary InspectInput(
        string input,
        CancellationToken cancellationToken)
    {
        if (HubExtensionManifestParser.IsReparsePoint(input))
        {
            throw new HubExtensionException(
                "La source ne peut pas être un lien ou point de réanalyse.",
                "InspectAssets",
                input);
        }

        var transferableCount = 0;
        long transferableBytes = 0;
        var nativeCount = 0;
        var unsupportedCount = 0;
        var skippedReparsePoints = 0;
        var inspectedEntries = 0;

        void ClassifyFile(string path)
        {
            inspectedEntries++;
            if (inspectedEntries > MaximumInspectedEntries)
            {
                throw new HubExtensionException(
                    "La source dépasse la limite d’inspection de 50 000 entrées.",
                    "InspectAssets",
                    input);
            }

            var info = new FileInfo(path);
            if (PortableExtensions.Contains(info.Extension))
            {
                transferableCount++;
                transferableBytes = checked(transferableBytes + info.Length);
                if (transferableCount > MaximumTransferableFiles ||
                    transferableBytes > MaximumTransferableBytes)
                {
                    throw new HubExtensionException(
                        "Le transfert dépasse 10 000 fichiers ou 20 Gio.",
                        "InspectAssets",
                        input);
                }
            }
            else if (EngineNativeExtensions.Contains(info.Extension))
            {
                nativeCount++;
            }
            else
            {
                unsupportedCount++;
            }
        }

        if (File.Exists(input))
        {
            ClassifyFile(input);
        }
        else
        {
            var directories = new Stack<string>();
            directories.Push(input);
            while (directories.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = directories.Pop();
                foreach (var entry in Directory.EnumerateFileSystemEntries(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    inspectedEntries++;
                    if (inspectedEntries > MaximumInspectedEntries)
                    {
                        throw new HubExtensionException(
                            "La source dépasse la limite d’inspection de 50 000 entrées.",
                            "InspectAssets",
                            input);
                    }

                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        skippedReparsePoints++;
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        directories.Push(entry);
                    }
                    else
                    {
                        inspectedEntries--;
                        ClassifyFile(entry);
                    }
                }
            }
        }

        return new HubAssetInspectionSummary(
            transferableCount,
            transferableBytes,
            nativeCount,
            unsupportedCount,
            skippedReparsePoints);
    }

    private static string ResolveTargetOutput(
        string outputCandidate,
        EngineFamily targetEngine,
        string input,
        Guid planId)
    {
        var candidate = Path.GetFullPath(outputCandidate.Trim().Trim('"'));
        var sourceName = SanitizeDirectoryName(
            File.Exists(input)
                ? Path.GetFileNameWithoutExtension(input)
                : new DirectoryInfo(input).Name);
        var importFolderName = $"{sourceName}-{planId:N}"[..Math.Min(sourceName.Length + 9, 69)];
        return targetEngine switch
        {
            EngineFamily.Unity => ResolveUnityOutput(candidate, importFolderName),
            EngineFamily.Unreal => ResolveUnrealOutput(candidate, importFolderName),
            EngineFamily.Godot => ResolveGodotOutput(candidate, importFolderName),
            EngineFamily.CryEngine => ResolveCryEngineOutput(candidate, importFolderName),
            _ => throw new HubExtensionException(
                "Asset Bridge prend actuellement en charge Unreal, Unity, Godot et CryEngine.",
                "ResolveTarget",
                candidate),
        };
    }

    private static string ResolveUnityOutput(string candidate, string importFolderName)
    {
        if (File.Exists(candidate))
        {
            throw new HubExtensionException(
                "Sélectionnez le dossier racine du projet Unity ou un dossier sous Assets.",
                "ResolveTarget",
                candidate);
        }

        var projectRoot = FindAncestor(candidate, directory =>
        {
            var marker = Path.Combine(directory, "ProjectSettings", "ProjectVersion.txt");
            var assets = Path.Combine(directory, "Assets");
            return File.Exists(marker) &&
                   !HubExtensionManifestParser.IsReparsePoint(marker) &&
                   Directory.Exists(assets) &&
                   !HubExtensionManifestParser.IsReparsePoint(assets);
        });
        if (projectRoot is null)
        {
            throw new HubExtensionException(
                "Aucun projet Unity valide n’a été trouvé pour la destination.",
                "ResolveTarget",
                candidate);
        }

        EnsureExistingPathSegmentsAreSafe(projectRoot, projectRoot);

        if (SamePath(candidate, projectRoot))
        {
            return Path.Combine(projectRoot, "Assets", "CyEngineHubImports", importFolderName);
        }

        var assetsRoot = Path.Combine(projectRoot, "Assets");
        if (!IsSameOrChild(candidate, assetsRoot))
        {
            throw new HubExtensionException(
                "La destination Unity doit se trouver sous le dossier Assets.",
                "ResolveTarget",
                candidate);
        }

        EnsureExistingPathSegmentsAreSafe(candidate, projectRoot);

        return candidate;
    }

    private static string ResolveUnrealOutput(string candidate, string importFolderName)
    {
        string? projectRoot;
        if (File.Exists(candidate) &&
            string.Equals(Path.GetExtension(candidate), ".uproject", StringComparison.OrdinalIgnoreCase) &&
            !HubExtensionManifestParser.IsReparsePoint(candidate))
        {
            projectRoot = Path.GetDirectoryName(candidate);
            candidate = projectRoot!;
        }
        else if (File.Exists(candidate))
        {
            throw new HubExtensionException(
                "Sélectionnez un fichier .uproject ou son dossier de projet.",
                "ResolveTarget",
                candidate);
        }
        else
        {
            projectRoot = FindAncestor(candidate, directory =>
                Directory.EnumerateFiles(directory, "*.uproject", SearchOption.TopDirectoryOnly)
                    .Where(path => !HubExtensionManifestParser.IsReparsePoint(path))
                    .Take(2)
                    .Count() == 1);
        }

        if (projectRoot is null)
        {
            throw new HubExtensionException(
                "Aucun projet Unreal unique n’a été trouvé pour la destination.",
                "ResolveTarget",
                candidate);
        }

        EnsureExistingPathSegmentsAreSafe(projectRoot, projectRoot);

        if (SamePath(candidate, projectRoot))
        {
            return Path.Combine(
                projectRoot,
                "Saved",
                "CyEngineHubAssetBridge",
                importFolderName);
        }

        var stagingRoot = Path.Combine(projectRoot, "Saved", "CyEngineHubAssetBridge");
        if (!IsSameOrChild(candidate, stagingRoot))
        {
            throw new HubExtensionException(
                "Les fichiers d’échange Unreal doivent rester dans le staging Saved\\CyEngineHubAssetBridge.",
                "ResolveTarget",
                candidate);
        }

        EnsureExistingPathSegmentsAreSafe(candidate, projectRoot);

        return candidate;
    }

    private static string ResolveGodotOutput(string candidate, string importFolderName)
    {
        string? projectRoot;
        if (File.Exists(candidate) &&
            string.Equals(Path.GetFileName(candidate), "project.godot", StringComparison.OrdinalIgnoreCase) &&
            !HubExtensionManifestParser.IsReparsePoint(candidate))
        {
            projectRoot = Path.GetDirectoryName(candidate);
            candidate = projectRoot!;
        }
        else if (File.Exists(candidate))
        {
            throw new HubExtensionException(
                "Sélectionnez un fichier project.godot ou son dossier de projet.",
                "ResolveTarget",
                candidate);
        }
        else
        {
            projectRoot = FindAncestor(candidate, directory =>
            {
                var marker = Path.Combine(directory, "project.godot");
                return File.Exists(marker) &&
                       !HubExtensionManifestParser.IsReparsePoint(marker);
            });
        }

        if (projectRoot is null)
        {
            throw new HubExtensionException(
                "Aucun projet Godot valide n’a été trouvé pour la destination.",
                "ResolveTarget",
                candidate);
        }

        EnsureExistingPathSegmentsAreSafe(projectRoot, projectRoot);

        if (SamePath(candidate, projectRoot))
        {
            return Path.Combine(projectRoot, "CyEngineHubImports", importFolderName);
        }

        var importRoot = Path.Combine(projectRoot, "CyEngineHubImports");
        if (!IsSameOrChild(candidate, importRoot))
        {
            throw new HubExtensionException(
                "La destination Godot doit rester sous le dossier CyEngineHubImports du projet.",
                "ResolveTarget",
                candidate);
        }

        EnsureExistingPathSegmentsAreSafe(candidate, projectRoot);

        return candidate;
    }

    private static string ResolveCryEngineOutput(string candidate, string importFolderName)
    {
        string? projectRoot;
        if (File.Exists(candidate) &&
            string.Equals(Path.GetExtension(candidate), ".cryproject", StringComparison.OrdinalIgnoreCase) &&
            !HubExtensionManifestParser.IsReparsePoint(candidate))
        {
            projectRoot = Path.GetDirectoryName(candidate);
            candidate = projectRoot!;
        }
        else if (File.Exists(candidate))
        {
            throw new HubExtensionException(
                "Sélectionnez un fichier .cryproject ou son dossier de projet.",
                "ResolveTarget",
                candidate);
        }
        else
        {
            projectRoot = FindAncestor(candidate, directory => FindCryProjectMarker(directory) is not null);
        }

        if (projectRoot is null)
        {
            throw new HubExtensionException(
                "Aucun projet CryEngine valide n’a été trouvé pour la destination.",
                "ResolveTarget",
                candidate);
        }

        EnsureExistingPathSegmentsAreSafe(projectRoot, projectRoot);
        var assetsRoot = ResolveCryEngineAssetsRoot(projectRoot);
        var importRoot = Path.Combine(assetsRoot, "CyEngineHubImports");
        if (SamePath(candidate, projectRoot))
        {
            return Path.Combine(importRoot, importFolderName);
        }

        if (!IsSameOrChild(candidate, importRoot))
        {
            throw new HubExtensionException(
                "La destination CryEngine doit rester sous CyEngineHubImports dans le dossier d’assets déclaré.",
                "ResolveTarget",
                candidate);
        }

        EnsureExistingPathSegmentsAreSafe(candidate, projectRoot);
        return candidate;
    }

    private static string ResolveCryEngineAssetsRoot(string projectRoot)
    {
        var marker = FindCryProjectMarker(projectRoot)
            ?? throw new HubExtensionException(
                "Le projet CryEngine doit contenir un unique descripteur .cryproject.",
                "ResolveTarget",
                projectRoot);
        try
        {
            var info = new FileInfo(marker);
            if (info.Length > 1024 * 1024)
            {
                throw new HubExtensionException(
                    "Le descripteur .cryproject dépasse 1 Mio.",
                    "ResolveTarget",
                    marker);
            }

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(marker),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                    MaxDepth = 64,
                });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetJsonProperty(root, "content", out var content) ||
                content.ValueKind != JsonValueKind.Object ||
                !TryGetJsonProperty(content, "assets", out var assets) ||
                assets.ValueKind != JsonValueKind.Array)
            {
                throw new HubExtensionException(
                    "Le projet CryEngine ne déclare aucun dossier d’assets.",
                    "ResolveTarget",
                    marker);
            }

            var relativeAssets = assets.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()?.Trim())
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            if (string.IsNullOrWhiteSpace(relativeAssets) || Path.IsPathRooted(relativeAssets))
            {
                throw new HubExtensionException(
                    "Le dossier d’assets CryEngine déclaré est invalide.",
                    "ResolveTarget",
                    marker);
            }

            var assetsRoot = Path.GetFullPath(Path.Combine(projectRoot, relativeAssets));
            if (!IsSameOrChild(assetsRoot, projectRoot) ||
                !Directory.Exists(assetsRoot) ||
                HubExtensionManifestParser.IsReparsePoint(assetsRoot))
            {
                throw new HubExtensionException(
                    "Le dossier d’assets CryEngine déclaré est absent ou sort du projet.",
                    "ResolveTarget",
                    assetsRoot);
            }

            return assetsRoot;
        }
        catch (JsonException exception)
        {
            throw new HubExtensionException(
                "Le descripteur .cryproject est invalide.",
                "ResolveTarget",
                marker,
                exception);
        }
    }

    private static string? FindCryProjectMarker(string projectRoot)
    {
        try
        {
            var markers = Directory.EnumerateFiles(
                    projectRoot,
                    "*.cryproject",
                    SearchOption.TopDirectoryOnly)
                .Where(path => !HubExtensionManifestParser.IsReparsePoint(path))
                .Take(2)
                .ToArray();
            return markers.Length == 1 ? markers[0] : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryGetJsonProperty(JsonElement element, string name, out JsonElement value)
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

    private static void EnsureExistingPathSegmentsAreSafe(string candidate, string projectRoot)
    {
        var current = Directory.Exists(candidate)
            ? candidate
            : Path.GetDirectoryName(candidate);
        while (!string.IsNullOrWhiteSpace(current) && IsSameOrChild(current, projectRoot))
        {
            if (Directory.Exists(current) && HubExtensionManifestParser.IsReparsePoint(current))
            {
                throw new HubExtensionException(
                    "La destination ne peut pas traverser un lien ou point de réanalyse.",
                    "ResolveTarget",
                    current);
            }

            if (SamePath(current, projectRoot))
            {
                return;
            }

            current = Path.GetDirectoryName(current);
        }

        throw new HubExtensionException(
            "La destination ne peut pas être rattachée de façon sûre au projet cible.",
            "ResolveTarget",
            candidate);
    }

    private static string? FindAncestor(string candidate, Func<string, bool> predicate)
    {
        var current = Directory.Exists(candidate)
            ? candidate
            : Path.GetDirectoryName(candidate);
        for (var depth = 0; depth < 12 && !string.IsNullOrWhiteSpace(current); depth++)
        {
            if (Directory.Exists(current) && predicate(current))
            {
                return Path.GetFullPath(current);
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    private static string SanitizeDirectoryName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Where(character => !invalid.Contains(character))
            .Take(60)
            .ToArray())
            .Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "AssetImport" : sanitized;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
            .ToLowerInvariant();
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

    private static bool IsVolumeRoot(string path) =>
        SamePath(path, Path.GetPathRoot(Path.GetFullPath(path))!);

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
