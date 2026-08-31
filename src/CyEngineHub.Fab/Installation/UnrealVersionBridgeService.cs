using System.Text.Json;
using CyEngineHub.Core.Processes;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Fab.Installation;

internal sealed record UnrealVersionBridgeRequest(
    string AssetId,
    string ArtifactId,
    string SourceDirectory,
    UnrealEngineInstallation SourceEngine,
    UnrealEngineInstallation TargetEngine,
    string StorageRoot,
    string Profile,
    bool KeepStaging,
    bool ValidateOutput);

internal sealed record UnrealVersionBridgeResult(
    string VariantDirectory,
    string ManifestPath,
    string ReportPath,
    int ImportedAssetCount,
    int UnsupportedAssetCount);

internal sealed class UnrealVersionBridgeService(IProcessRunner processRunner)
{
    private const int MaximumFiles = 100_000;
    private const long MaximumBytes = 200L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> PortableExtensions = new(
        [".fbx", ".obj", ".gltf", ".glb", ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".hdr", ".wav", ".ogg"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> NativeExtensions = new(
        [".uasset", ".umap", ".uexp", ".ubulk", ".uptnl"],
        StringComparer.OrdinalIgnoreCase);

    public async Task<UnrealVersionBridgeResult> ConvertAsync(
        UnrealVersionBridgeRequest request,
        IProgress<FabInstallationScriptProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.SourceDirectory));
        var storageRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.StorageRoot));
        ValidateRoots(sourceRoot, storageRoot);
        Directory.CreateDirectory(storageRoot);
        var jobId = Guid.NewGuid().ToString("N");
        var staging = CombineContained(storageRoot, Path.Combine(".staging", jobId));
        var sourceProject = Path.Combine(staging, "SourceProject");
        var targetProject = Path.Combine(staging, "TargetProject");
        var exchangeRoot = Path.Combine(staging, "Exchange");
        Directory.CreateDirectory(sourceProject);
        Directory.CreateDirectory(targetProject);
        Directory.CreateDirectory(exchangeRoot);
        var completed = false;
        try
        {
            progress?.Report(new(0.05, "Copie isolée du pack source…"));
            var inventory = Inventory(sourceRoot);
            await CopySelectedAsync(
                    Path.Combine(sourceProject, "Content"),
                    inventory.Where(file => NativeExtensions.Contains(file.Extension)).ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);
            await CopySelectedAsync(
                    exchangeRoot,
                    inventory.Where(file => PortableExtensions.Contains(file.Extension)).ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);
            var sourceProjectFile = await WriteProjectAsync(sourceProject, "CyVersionBridgeSource", cancellationToken).ConfigureAwait(false);
            var targetProjectFile = await WriteProjectAsync(targetProject, "CyVersionBridgeTarget", cancellationToken).ConfigureAwait(false);
            var sourceReport = Path.Combine(staging, "source-report.json");
            if (inventory.Any(file => file.Extension.Equals(".uasset", StringComparison.OrdinalIgnoreCase)))
            {
                var sourceConfig = Path.Combine(staging, "source-config.json");
                await WriteJsonAsync(sourceConfig, new
                {
                    exchangeRoot,
                    reportPath = sourceReport,
                    profile = request.Profile,
                }, cancellationToken).ConfigureAwait(false);
                progress?.Report(new(
                    0.2,
                    $"Export avec Unreal {request.SourceEngine.Version.Major}.{request.SourceEngine.Version.Minor}…"));
                await RunEditorAsync(
                        request.SourceEngine,
                        sourceProjectFile,
                        ResolveScript("source-export.py"),
                        sourceConfig,
                        Path.Combine(staging, "source-editor.log"),
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await WriteJsonAsync(sourceReport, new { exported = Array.Empty<string>(), unsupported = Array.Empty<string>(), errors = Array.Empty<string>() }, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (!Directory.EnumerateFiles(exchangeRoot, "*", SearchOption.AllDirectories).Any())
            {
                throw new InvalidOperationException(
                    $"Aucun asset portable n’a pu être produit. Diagnostic conservé dans {staging}.");
            }

            var targetReport = Path.Combine(staging, "target-report.json");
            var targetConfig = Path.Combine(staging, "target-config.json");
            await WriteJsonAsync(targetConfig, new
            {
                exchangeRoot,
                reportPath = targetReport,
                validateOutput = request.ValidateOutput,
            }, cancellationToken).ConfigureAwait(false);
            progress?.Report(new(
                0.55,
                $"Reconstruction avec Unreal {request.TargetEngine.Version.Major}.{request.TargetEngine.Version.Minor}…"));
            await RunEditorAsync(
                    request.TargetEngine,
                    targetProjectFile,
                    ResolveScript("target-import.py"),
                    targetConfig,
                    Path.Combine(staging, "target-editor.log"),
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            var importedCount = ReadArrayCount(targetReport, "imported");
            if (importedCount == 0)
            {
                throw new InvalidOperationException($"Le moteur cible n’a produit aucun asset. Diagnostic conservé dans {staging}.");
            }

            var unsupported = ReadStringArray(sourceReport, "unsupported");
            var variantDirectory = BuildVariantDirectory(request);
            var publishing = variantDirectory + ".partial-" + jobId;
            Directory.CreateDirectory(publishing);
            await CopyTreeAsync(Path.Combine(targetProject, "Content"), Path.Combine(publishing, "Content"), cancellationToken)
                .ConfigureAwait(false);
            var reportPath = Path.Combine(publishing, "conversion-report.json");
            await WriteCombinedReportAsync(sourceReport, targetReport, reportPath, cancellationToken).ConfigureAwait(false);
            var manifest = new UnrealVersionBridgeVariantManifest(
                1,
                request.AssetId,
                request.ArtifactId,
                request.SourceEngine.Version.ToString(),
                request.TargetEngine.Version.ToString(),
                request.Profile,
                importedCount,
                unsupported.Length,
                unsupported,
                "Content",
                DateTimeOffset.UtcNow);
            await WriteJsonAsync(Path.Combine(publishing, "cyenginehub-derived-variant.json"), manifest, cancellationToken)
                .ConfigureAwait(false);
            PublishVariant(publishing, variantDirectory);
            progress?.Report(new(
                0.85,
                $"Variante publiée · {importedCount:N0} asset(s), {unsupported.Length:N0} non convertible(s)."));
            completed = true;
            return new(
                variantDirectory,
                Path.Combine(variantDirectory, "cyenginehub-derived-variant.json"),
                Path.Combine(variantDirectory, "conversion-report.json"),
                importedCount,
                unsupported.Length);
        }
        finally
        {
            if (completed && !request.KeepStaging && Directory.Exists(staging) && IsUnderRoot(storageRoot, staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private async Task RunEditorAsync(
        UnrealEngineInstallation engine,
        string projectFile,
        string scriptFile,
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
                        $"-script={scriptFile}",
                        "-unattended",
                        "-nop4",
                        "-nosplash",
                        "-NullRHI",
                        "-NoSound",
                        $"-abslog={logFile}",
                    ],
                    EnvironmentVariables = new Dictionary<string, string?>
                    {
                        ["CYENGINEHUB_VERSION_BRIDGE_CONFIG"] = configFile,
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
                $"Unreal {engine.Version.Major}.{engine.Version.Minor} a interrompu la conversion (code {result.ExitCode}). Consultez {logFile}.");
        }
    }

    private static VersionBridgeFile[] Inventory(string sourceRoot)
    {
        var contentRoot = Directory.Exists(Path.Combine(sourceRoot, "Content")) ? Path.Combine(sourceRoot, "Content") : sourceRoot;
        var files = new List<VersionBridgeFile>();
        long total = 0;
        foreach (var path in Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            if (!NativeExtensions.Contains(info.Extension) && !PortableExtensions.Contains(info.Extension)) continue;
            total = checked(total + info.Length);
            if (files.Count >= MaximumFiles || total > MaximumBytes)
            {
                throw new InvalidOperationException("Le pack dépasse 100 000 fichiers ou 200 Gio.");
            }
            files.Add(new(info.FullName, Path.GetRelativePath(contentRoot, info.FullName), info.Extension));
        }
        return files.ToArray();
    }

    private static async Task<string> WriteProjectAsync(string root, string name, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(root, "Content"));
        var path = Path.Combine(root, name + ".uproject");
        await File.WriteAllTextAsync(
                path,
                """
                {
                  "FileVersion": 3,
                  "EngineAssociation": "",
                  "Category": "CyEngineHub",
                  "Description": "Projet temporaire isolé pour Unreal Version Bridge",
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

    private static string ResolveScript(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Extensions", "cyenginehub.unreal-version-bridge", "scripts", name);
        return File.Exists(path)
            ? path
            : throw new InvalidOperationException($"Le composant du plugin est absent : {path}");
    }

    private static async Task CopySelectedAsync(string destinationRoot, IReadOnlyList<VersionBridgeFile> files, CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = CombineContained(destinationRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await CopyFileAsync(file.SourcePath, destination, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CopyTreeAsync(string sourceRoot, string destinationRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceRoot)) throw new InvalidOperationException("Le moteur cible n’a produit aucun dossier Content.");
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var destination = CombineContained(destinationRoot, Path.GetRelativePath(sourceRoot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await CopyFileAsync(source, destination, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), cancellationToken).ConfigureAwait(false);

    private static string[] ReadStringArray(string path, string property)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).Take(10_000).ToArray()
            : [];
    }

    private static int ReadArrayCount(string path, string property)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array ? value.GetArrayLength() : 0;
    }

    private static async Task WriteCombinedReportAsync(string sourcePath, string targetPath, string destination, CancellationToken cancellationToken)
    {
        using var source = JsonDocument.Parse(await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false));
        using var target = JsonDocument.Parse(await File.ReadAllTextAsync(targetPath, cancellationToken).ConfigureAwait(false));
        await WriteJsonAsync(destination, new { source = source.RootElement.Clone(), target = target.RootElement.Clone() }, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildVariantDirectory(UnrealVersionBridgeRequest request) => CombineContained(
        request.StorageRoot,
        Path.Combine(
            SanitizeSegment(request.AssetId),
            SanitizeSegment(request.ArtifactId),
            $"UE_{request.TargetEngine.Version.Major}.{request.TargetEngine.Version.Minor}"));

    private static void PublishVariant(string publishing, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (Directory.Exists(target))
        {
            var backup = CombineContained(Path.GetDirectoryName(target)!, $".backups/{Path.GetFileName(target)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            Directory.Move(target, backup);
        }
        Directory.Move(publishing, target);
    }

    private static void ValidateRoots(string source, string storage)
    {
        if (!Directory.Exists(source) || IsReparsePoint(source)) throw new InvalidOperationException("La source est absente ou constitue un lien.");
        if (IsVolumeRoot(storage) || IsSameOrChild(storage, source) || IsSameOrChild(source, storage))
            throw new InvalidOperationException("Le stockage des variantes doit être séparé du pack source.");
        if (Directory.Exists(storage) && IsReparsePoint(storage)) throw new InvalidOperationException("Le stockage des variantes ne peut pas être une jonction.");
    }

    private static string CombineContained(string root, string relative)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relative));
        return IsUnderRoot(fullRoot, candidate) ? candidate : throw new InvalidOperationException("Un chemin sort du dossier autorisé.");
    }

    private static bool IsUnderRoot(string root, string candidate) => candidate.StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsSameOrChild(string candidate, string parent)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var fullCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var fullParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return fullCandidate.Equals(fullParent, comparison) || fullCandidate.StartsWith(fullParent + Path.DirectorySeparatorChar, comparison);
    }

    private static bool IsVolumeRoot(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).Equals(
        Path.TrimEndingDirectorySeparator(Path.GetPathRoot(Path.GetFullPath(path))!),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized.Trim();
    }

    private sealed record VersionBridgeFile(string SourcePath, string RelativePath, string Extension);
}
