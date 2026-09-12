using System.Text.Json;
using CyEngineHub.Core.Processes;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Fab.Installation;

internal sealed record UnrealRelocatedContentRequest(
    string AssetId,
    string ArtifactId,
    string SourceDirectory,
    UnrealEngineInstallation Engine,
    string StorageRoot,
    string DestinationPath,
    bool PreserveRootFolders,
    bool KeepStaging);

internal sealed record UnrealRelocatedContentResult(
    string VariantDirectory,
    string ManifestPath,
    string ReportPath,
    string EditorLogPath,
    int MovedAssetCount);

internal sealed class UnrealRelocatedContentService(IProcessRunner processRunner)
{
    private const int MaximumFiles = 100_000;
    private const long MaximumBytes = 200L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> NativeExtensions = new(
        [".uasset", ".umap", ".uexp", ".ubulk", ".uptnl"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<UnrealRelocatedContentResult> RelocateAsync(
        UnrealRelocatedContentRequest request,
        IProgress<FabInstallationScriptProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.SourceDirectory));
        var storageRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.StorageRoot));
        ValidateRoots(sourceRoot, storageRoot);
        var destinationPath = NormalizeDestinationPath(request.DestinationPath);
        Directory.CreateDirectory(storageRoot);
        var files = Inventory(sourceRoot);
        if (!files.Any(file => file.Extension.Equals(".uasset", StringComparison.OrdinalIgnoreCase) ||
                               file.Extension.Equals(".umap", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Ce script est réservé aux packs Fab Unreal natifs contenant des .uasset ou .umap.");
        }

        var jobId = Guid.NewGuid().ToString("N");
        var staging = CombineContained(storageRoot, Path.Combine(".staging", jobId));
        var projectRoot = Path.Combine(staging, "Project");
        var reportPath = Path.Combine(staging, "relocation-report.json");
        var configPath = Path.Combine(staging, "relocation-config.json");
        var editorLogPath = Path.Combine(staging, "relocation-editor.log");
        Directory.CreateDirectory(projectRoot);
        var completed = false;
        try
        {
            progress?.Report(new(0.05, "Copie du pack avec son arborescence Content d’origine…"));
            await CopySelectedAsync(Path.Combine(projectRoot, "Content"), files, cancellationToken)
                .ConfigureAwait(false);
            var projectFile = await WriteProjectAsync(projectRoot, cancellationToken).ConfigureAwait(false);
            await WriteJsonAsync(
                    configPath,
                    new
                    {
                        sourceRoot = "/Game",
                        destinationPath,
                        preserveRootFolders = request.PreserveRootFolders,
                        reportPath,
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new(
                0.25,
                $"Chargement au chemin d’origine puis déplacement avec Unreal {request.Engine.Version.Major}.{request.Engine.Version.Minor}…"));
            await RunEditorAsync(
                    request.Engine,
                    projectFile,
                    configPath,
                    editorLogPath,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            var report = await ReadReportAsync(reportPath, cancellationToken).ConfigureAwait(false);
            var logErrors = await ReadEditorErrorsAsync(editorLogPath, cancellationToken).ConfigureAwait(false);
            var errors = report.Errors.Concat(logErrors)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .ToArray();
            if (report.MovedAssetCount == 0 || errors.Length > 0)
            {
                var detail = errors.Length == 0
                    ? "aucun asset n’a été déplacé"
                    : string.Join(" · ", errors.Take(5));
                throw new InvalidOperationException(
                    $"La vérification après déplacement a échoué : {detail}. Diagnostic conservé dans {staging}.");
            }

            progress?.Report(new(0.8, $"Références validées · {report.MovedAssetCount:N0} asset(s)."));
            var sourceContent = ResolveDestinationDirectory(projectRoot, destinationPath);
            if (!Directory.Exists(sourceContent))
            {
                throw new InvalidOperationException(
                    $"Unreal n’a produit aucun dossier à la destination demandée. Diagnostic conservé dans {staging}.");
            }
            var variantDirectory = BuildVariantDirectory(request, destinationPath);
            var publishing = variantDirectory + ".partial-" + jobId;
            var relativeDestination = destinationPath["/Game/".Length..]
                .Replace('/', Path.DirectorySeparatorChar);
            await CopyTreeAsync(
                    sourceContent,
                    Path.Combine(publishing, "Content", relativeDestination),
                    cancellationToken)
                .ConfigureAwait(false);
            Directory.CreateDirectory(publishing);
            File.Copy(reportPath, Path.Combine(publishing, "relocation-report.json"), overwrite: false);
            File.Copy(editorLogPath, Path.Combine(publishing, "relocation-editor.log"), overwrite: false);
            var manifestPath = Path.Combine(publishing, "cyenginehub-relocated-variant.json");
            await WriteJsonAsync(
                    manifestPath,
                    new
                    {
                        schemaVersion = 1,
                        request.AssetId,
                        request.ArtifactId,
                        engineVersion = request.Engine.Version.ToString(),
                        destinationPath,
                        request.PreserveRootFolders,
                        report.OriginalAssetCount,
                        report.MovedAssetCount,
                        contentDirectory = "Content/" + relativeDestination.Replace(Path.DirectorySeparatorChar, '/'),
                        generatedAt = DateTimeOffset.UtcNow,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            PublishVariant(publishing, variantDirectory);
            completed = true;
            return new(
                variantDirectory,
                Path.Combine(variantDirectory, "cyenginehub-relocated-variant.json"),
                Path.Combine(variantDirectory, "relocation-report.json"),
                Path.Combine(variantDirectory, "relocation-editor.log"),
                report.MovedAssetCount);
        }
        finally
        {
            if (completed && !request.KeepStaging && Directory.Exists(staging) &&
                IsUnderRoot(storageRoot, staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private async Task RunEditorAsync(
        UnrealEngineInstallation engine,
        string projectFile,
        string configFile,
        string logFile,
        IProgress<FabInstallationScriptProgress>? progress,
        CancellationToken cancellationToken)
    {
        var output = new Progress<ProcessOutputLine>(line => progress?.Report(new(null, line.Text)));
        var result = await processRunner.RunAsync(
                new ProcessRequest
                {
                    FileName = UnrealVersionBridgeInstallationScript.ResolveCommandEditor(engine),
                    Arguments =
                    [
                        projectFile,
                        "-run=pythonscript",
                        $"-script={ResolveScript()}",
                        "-unattended",
                        "-nop4",
                        "-nosplash",
                        "-NullRHI",
                        "-NoSound",
                        $"-abslog={logFile}",
                    ],
                    EnvironmentVariables = new Dictionary<string, string?>
                    {
                        ["CYENGINEHUB_RELOCATED_IMPORT_CONFIG"] = configFile,
                    },
                    WorkingDirectory = Path.GetDirectoryName(projectFile),
                    MaxCapturedLines = 20_000,
                },
                output,
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Unreal a interrompu le déplacement (code {result.ExitCode}). Consultez {logFile}.");
        }
    }

    private static RelocatedFile[] Inventory(string sourceRoot)
    {
        var contentRoot = Directory.Exists(Path.Combine(sourceRoot, "Content"))
            ? Path.Combine(sourceRoot, "Content")
            : sourceRoot;
        var files = new List<RelocatedFile>();
        long total = 0;
        foreach (var path in Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                !NativeExtensions.Contains(info.Extension)) continue;
            total = checked(total + info.Length);
            if (files.Count >= MaximumFiles || total > MaximumBytes)
            {
                throw new InvalidOperationException("Le pack dépasse 100 000 fichiers ou 200 Gio.");
            }
            files.Add(new(path, Path.GetRelativePath(contentRoot, path), info.Extension));
        }
        return files.ToArray();
    }

    private static string NormalizeDestinationPath(string value)
    {
        var normalized = value.Trim().Replace('\\', '/');
        if (normalized.StartsWith('/') && !normalized.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Le chemin absolu de destination doit être sous /Game.");
        var path = normalized.Trim('/');
        if (path.StartsWith("Game/", StringComparison.OrdinalIgnoreCase)) path = path[5..];
        if (path.Length == 0) throw new InvalidOperationException("Configurez un dossier sous /Game.");
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(segment => segment is "." or ".." || segment.Any(character =>
                !(char.IsLetterOrDigit(character) || character is '_' or '-'))))
        {
            throw new InvalidOperationException(
                "La destination accepte uniquement lettres, chiffres, tirets, underscores et sous-dossiers.");
        }
        return "/Game/" + string.Join('/', segments);
    }

    private static async Task<string> WriteProjectAsync(string root, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(root, "Content"));
        var path = Path.Combine(root, "CyRelocatedImport.uproject");
        await File.WriteAllTextAsync(
                path,
                """
                {
                  "FileVersion": 3,
                  "EngineAssociation": "",
                  "Category": "CyEngineHub",
                  "Description": "Projet temporaire conservant les chemins natifs avant déplacement",
                  "Plugins": [
                    { "Name": "PythonScriptPlugin", "Enabled": true },
                    { "Name": "EditorScriptingUtilities", "Enabled": true }
                  ]
                }
                """,
                cancellationToken)
            .ConfigureAwait(false);
        return path;
    }

    private static string ResolveScript()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Extensions",
            "cyenginehub.unreal-relocated-install",
            "scripts",
            "relocate-pack.py");
        return File.Exists(path)
            ? path
            : throw new InvalidOperationException($"Le composant du plugin est absent : {path}");
    }

    private static async Task<RelocationReport> ReadReportAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("Unreal n’a produit aucun rapport.");
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<RelocationReport>(stream, JsonOptions, cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException("Le rapport Unreal est vide.");
    }

    private static async Task<string[]> ReadEditorErrorsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return [];
        var errors = new List<string>();
        using var reader = new StreamReader(path);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            var dependencyError = line.Contains("Can't find file", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("Failed to load", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("LogLinker: Error", StringComparison.OrdinalIgnoreCase);
            if (dependencyError && line.Contains("/Game/", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(line.Trim());
                if (errors.Count >= 100) break;
            }
        }
        return errors.ToArray();
    }

    private static async Task CopySelectedAsync(
        string destinationRoot,
        IReadOnlyList<RelocatedFile> files,
        CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = CombineContained(destinationRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await CopyFileAsync(file.SourcePath, destination, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CopyTreeAsync(
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = CombineContained(destinationRoot, Path.GetRelativePath(sourceRoot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await CopyFileAsync(source, destination, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveDestinationDirectory(string projectRoot, string destinationPath) =>
        CombineContained(
            Path.Combine(projectRoot, "Content"),
            destinationPath["/Game/".Length..].Replace('/', Path.DirectorySeparatorChar));

    private static string BuildVariantDirectory(
        UnrealRelocatedContentRequest request,
        string destinationPath) => CombineContained(
        request.StorageRoot,
        Path.Combine(
            "Variants",
            SanitizeSegment(request.AssetId),
            SanitizeSegment(request.ArtifactId),
            $"UE_{request.Engine.Version.Major}.{request.Engine.Version.Minor}",
            SanitizeSegment(destinationPath["/Game/".Length..].Replace('/', '_'))));

    private static void PublishVariant(string publishing, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (Directory.Exists(target))
        {
            var backup = CombineContained(
                Path.GetDirectoryName(target)!,
                Path.Combine(".backups", $"{Path.GetFileName(target)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}"));
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            Directory.Move(target, backup);
        }
        Directory.Move(publishing, target);
    }

    private static void ValidateRoots(string source, string storage)
    {
        if (!Directory.Exists(source)) throw new InvalidOperationException("La source du pack est absente.");
        if (IsSameOrChild(storage, source) || IsSameOrChild(source, storage))
        {
            throw new InvalidOperationException("Le stockage temporaire doit être séparé de la source.");
        }
    }

    private static string CombineContained(string root, string relative)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relative));
        return IsUnderRoot(fullRoot, candidate)
            ? candidate
            : throw new InvalidOperationException("Un chemin sort du dossier autorisé.");
    }

    private static bool IsUnderRoot(string root, string candidate) => candidate.StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsSameOrChild(string candidate, string parent)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var fullCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var fullParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return fullCandidate.Equals(fullParent, comparison) ||
               fullCandidate.StartsWith(fullParent + Path.DirectorySeparatorChar, comparison);
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized.Trim();
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record RelocatedFile(string SourcePath, string RelativePath, string Extension);
    private sealed record RelocationReport(
        int OriginalAssetCount,
        int MovedAssetCount,
        IReadOnlyList<string> MovedAssets,
        IReadOnlyList<string> Errors);
}
