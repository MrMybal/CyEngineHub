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
    bool ValidateOutput,
    string Mode = "native",
    string? ProfileFile = null,
    bool AllowLossyConversion = false,
    bool AnalyzeOnly = false,
    bool EnableAgentFallback = false,
    bool AllowAgentCodeExecution = false,
    string? AgentWorkspace = null,
    string? AgentPython = null);

internal sealed record UnrealVersionBridgeResult(
    string VariantDirectory,
    string ManifestPath,
    string ReportPath,
    int ImportedAssetCount,
    int UnsupportedAssetCount,
    bool AnalysisOnly = false,
    string? AgentWorkspace = null,
    bool ContainsPlugins = false);

internal sealed class UnrealVersionBridgeService(IProcessRunner processRunner, string? nativePluginRoot = null)
{
    private const int MaximumFiles = 100_000;
    private const long MaximumBytes = 200L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> PortableExtensions = new(
        [".fbx", ".obj", ".mtl", ".gltf", ".glb", ".bin", ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".hdr", ".wav", ".ogg"],
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
        if (request.AnalyzeOnly && !string.IsNullOrWhiteSpace(request.AgentWorkspace))
            throw new InvalidOperationException("L’analyse seule ne peut pas publier ou installer une réparation IA. Désactivez-la pour une reprise explicite.");
        if (!string.IsNullOrWhiteSpace(request.AgentWorkspace))
            return await new UnrealVersionBridgeAgentWorkspace(processRunner).ResumeAsync(request, progress, cancellationToken).ConfigureAwait(false);
        var plugins = UnrealVersionBridgeAgentWorkspace.FindPlugins(sourceRoot);
        if (plugins.Length > 0)
        {
            if (!request.EnableAgentFallback)
                throw new InvalidOperationException("La conversion des plugins exige l’assistance IA optionnelle et les sources pour les modules C++.");
            if (request.AnalyzeOnly)
                throw new InvalidOperationException("Analyse de plugin : activez la préparation IA pour obtenir un diagnostic MCP sans installation.");
            var workspace = await UnrealVersionBridgeAgentWorkspace.CreateAsync(request, null,
                "Plugin : adaptez les API et les assets dans la copie cible, puis compilez les modules pour le moteur cible. Aucun changement des originaux.", cancellationToken).ConfigureAwait(false);
            return new(string.Empty, string.Empty, Path.Combine(workspace, "agent-diagnostic.txt"), 0, 0, AgentWorkspace: workspace, ContainsPlugins: true);
        }
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
            var planPath = Path.Combine(staging, "asset-conversion-plan.json");
            var hasNative = inventory.Any(file => file.Extension.Equals(".uasset", StringComparison.OrdinalIgnoreCase) ||
                file.Extension.Equals(".umap", StringComparison.OrdinalIgnoreCase));
            if (hasNative)
            {
                var profileFile = string.IsNullOrWhiteSpace(request.ProfileFile)
                    ? Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(ResolveScript("source-inspect.py")))!, "profiles",
                        request.SourceEngine.Version.Minor == 7 && request.TargetEngine.Version.Minor is 3 or 5 ? "ue-5.7-native.json" : "ue-5.6-to-5.5.json")
                    : Path.GetFullPath(request.ProfileFile);
                var profiles = UnrealAssetConversionProfiles.Load(profileFile);
                await WriteJsonAsync(Path.Combine(staging, "profiles-snapshot.json"), profiles, cancellationToken).ConfigureAwait(false);
                var inventoryReport = Path.Combine(staging, "asset-inventory.json");
                var inspectConfig = Path.Combine(staging, "inspect-config.json");
                await WriteJsonAsync(inspectConfig, new { reportPath = inventoryReport }, cancellationToken).ConfigureAwait(false);
                progress?.Report(new(0.1, "Analyse des types, fonctionnalités et dépendances avant conversion…"));
                await RunEditorAsync(request.SourceEngine, sourceProjectFile, ResolveScript("source-inspect.py"),
                    inspectConfig, Path.Combine(staging, "inspect-editor.log"), progress, cancellationToken).ConfigureAwait(false);
                ValidateReportErrors(inventoryReport);
                using var inspected = JsonDocument.Parse(await File.ReadAllTextAsync(inventoryReport, cancellationToken).ConfigureAwait(false));
                var assets = inspected.RootElement.GetProperty("assets").Deserialize<UnrealAssetConversionInventoryItem[]>(JsonOptions)
                    ?? throw new InvalidOperationException("Inventaire natif invalide.");
                var plan = UnrealAssetConversionProfiles.Analyze(profiles, assets,
                    $"{request.SourceEngine.Version.Major}.{request.SourceEngine.Version.Minor}",
                    $"{request.TargetEngine.Version.Major}.{request.TargetEngine.Version.Minor}", request.Mode, request.AllowLossyConversion);
                await WriteJsonAsync(planPath, plan, cancellationToken).ConfigureAwait(false);
                foreach (var decision in plan.Assets.Take(20))
                    progress?.Report(new(0.15, $"{decision.AssetPath} · {decision.Status} · {decision.Reason}"));
                if (request.AnalyzeOnly)
                    return new(string.Empty, string.Empty, planPath, 0, plan.Assets.Count(a => !UnrealAssetConversionProfiles.IsExecutable(a)), true);
                if (!plan.CanConvert)
                    throw new InvalidOperationException($"Conversion bloquée par les profils : {string.Join(" ; ", plan.Assets.Where(a => !UnrealAssetConversionProfiles.IsExecutable(a)).Take(3).Select(a => a.AssetPath + " : " + a.Reason))} Rapport : {planPath}");
                if (request.Mode == "native")
                {
                    await PrepareNativePluginAsync(sourceProjectFile, request.SourceEngine, cancellationToken).ConfigureAwait(false);
                    await PrepareNativePluginAsync(targetProjectFile, request.TargetEngine, cancellationToken).ConfigureAwait(false);
                }
                var sourceConfig = Path.Combine(staging, "source-config.json");
                await WriteJsonAsync(sourceConfig, new
                {
                    exchangeRoot,
                    reportPath = sourceReport,
                    profile = request.Profile,
                    planPath,
                    operation = "export",
                }, cancellationToken).ConfigureAwait(false);
                progress?.Report(new(
                    0.2,
                    $"Export avec Unreal {request.SourceEngine.Version.Major}.{request.SourceEngine.Version.Minor}…"));
                await RunEditorAsync(
                        request.SourceEngine,
                        sourceProjectFile,
                        ResolveScript(request.Mode == "native" ? "native-textures.py" : "source-export.py"),
                        sourceConfig,
                        Path.Combine(staging, "source-editor.log"),
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                if (request.Mode != "portable" || !request.AllowLossyConversion || request.AnalyzeOnly)
                    throw new InvalidOperationException("Cette source ne contient pas d'assets Unreal natifs à analyser. L'import de fichiers sources exige le mode portable et l'accord explicite sur les pertes.");
                await WriteJsonAsync(sourceReport, new { exported = Array.Empty<string>(), unsupported = Array.Empty<string>(), errors = Array.Empty<string>() }, cancellationToken)
                    .ConfigureAwait(false);
            }
            ValidateReportErrors(sourceReport);
            if (ReadStringArray(sourceReport, "unsupported").Length > 0)
                throw new InvalidOperationException($"Le pack contient des assets non convertibles : aucune variante partielle publiée. Rapport : {sourceReport}");
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
                operation = "import",
            }, cancellationToken).ConfigureAwait(false);
            progress?.Report(new(
                0.55,
                $"Reconstruction avec Unreal {request.TargetEngine.Version.Major}.{request.TargetEngine.Version.Minor}…"));
            await RunEditorAsync(
                    request.TargetEngine,
                    targetProjectFile,
                    ResolveScript(request.Mode == "native" ? "native-textures.py" : "target-import.py"),
                    targetConfig,
                    Path.Combine(staging, "target-editor.log"),
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateReportErrors(targetReport);
            var importedCount = ReadArrayCount(targetReport, "imported");
            if (importedCount == 0)
            {
                throw new InvalidOperationException($"Le moteur cible n’a produit aucun asset. Diagnostic conservé dans {staging}.");
            }

            if (request.Mode == "native")
            {
                // Always reopen in a fresh target process, even when legacy validation is disabled.
                var verifyReport = Path.Combine(staging, "native-reopen-report.json");
                var verifyConfig = Path.Combine(staging, "native-reopen-config.json");
                await WriteJsonAsync(verifyConfig, new { exchangeRoot, reportPath = verifyReport, operation = "verify" }, cancellationToken).ConfigureAwait(false);
                await RunEditorAsync(request.TargetEngine, targetProjectFile, ResolveScript("native-textures.py"), verifyConfig,
                    Path.Combine(staging, "native-reopen-editor.log"), progress, cancellationToken).ConfigureAwait(false);
                ValidateReportErrors(verifyReport);
                if (ReadArrayCount(verifyReport, "imported") != importedCount || ReadArrayCount(sourceReport, "exported") != importedCount)
                    throw new InvalidOperationException("Le nombre d’assets natifs ne correspond pas après réouverture.");
            }

            var unsupported = ReadStringArray(sourceReport, "unsupported");
            var variantDirectory = BuildVariantDirectory(request);
            var publishing = variantDirectory + ".partial-" + jobId;
            Directory.CreateDirectory(publishing);
            await CopyTreeAsync(Path.Combine(targetProject, "Content"), Path.Combine(publishing, "Content"), cancellationToken)
                .ConfigureAwait(false);
            var reportPath = Path.Combine(publishing, "conversion-report.json");
            await WriteCombinedReportAsync(sourceReport, targetReport, reportPath, cancellationToken).ConfigureAwait(false);
            if (File.Exists(planPath))
            {
                await CopyFileAsync(planPath, Path.Combine(publishing, "asset-conversion-plan.json"), cancellationToken).ConfigureAwait(false);
                await CopyFileAsync(Path.Combine(staging, "profiles-snapshot.json"), Path.Combine(publishing, "profiles-snapshot.json"), cancellationToken).ConfigureAwait(false);
            }
            if (request.Mode == "native")
                await CopyFileAsync(Path.Combine(staging, "native-reopen-report.json"), Path.Combine(publishing, "native-reopen-report.json"), cancellationToken).ConfigureAwait(false);
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
                DateTimeOffset.UtcNow,
                request.Mode);
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
        catch (Exception exception) when (request.EnableAgentFallback && !request.AnalyzeOnly &&
            exception is InvalidOperationException or InvalidDataException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workspace = await UnrealVersionBridgeAgentWorkspace.CreateAsync(request, staging, exception.Message, cancellationToken).ConfigureAwait(false);
            progress?.Report(new(null, "Conversion native bloquée ; tâche de réparation IA préparée. Aucune installation effectuée."));
            return new(string.Empty, string.Empty, Path.Combine(workspace, "agent-diagnostic.txt"), 0, 0, AgentWorkspace: workspace);
        }
        finally
        {
            if (completed && !request.KeepStaging && Directory.Exists(staging) && IsUnderRoot(storageRoot, staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private async Task PrepareNativePluginAsync(string projectFile, UnrealEngineInstallation engine, CancellationToken cancellationToken)
    {
        var source = Path.Combine(nativePluginRoot ?? Path.Combine(AppContext.BaseDirectory, "Extensions", "cyenginehub.unreal-version-bridge", "native-binaries"),
            $"UE_{engine.Version.Major}.{engine.Version.Minor}");
        var descriptor = Path.Combine(source, "CyHubNativeBridge.uplugin");
        var binaryRoot = Path.Combine(source, "Binaries", "Win64");
        if (!OperatingSystem.IsWindows() || !File.Exists(descriptor) || !File.Exists(Path.Combine(binaryRoot, "UnrealEditor-CyHubNativeBridge.dll")) || !File.Exists(Path.Combine(binaryRoot, "UnrealEditor.modules")))
            throw new InvalidOperationException($"Adaptateur natif compilé absent pour UE {engine.Version.Major}.{engine.Version.Minor}. Attendu : {descriptor}");
        var destination = Path.Combine(Path.GetDirectoryName(projectFile)!, "Plugins", "CyHubNativeBridge");
        Directory.CreateDirectory(destination);
        await CopyFileAsync(descriptor, Path.Combine(destination, "CyHubNativeBridge.uplugin"), cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.Combine(destination, "Binaries", "Win64"));
        foreach (var name in new[] { "UnrealEditor-CyHubNativeBridge.dll", "UnrealEditor.modules" })
            await CopyFileAsync(Path.Combine(binaryRoot, name), Path.Combine(destination, "Binaries", "Win64", name), cancellationToken).ConfigureAwait(false);
        var project = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(projectFile, cancellationToken).ConfigureAwait(false))!;
        project["Plugins"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["Name"] = "CyHubNativeBridge", ["Enabled"] = true });
        await File.WriteAllTextAsync(projectFile, project.ToJsonString(JsonOptions), cancellationToken).ConfigureAwait(false);
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
                        // SoundWave import needs the audio decoder factories, even without playback.
                        "-AllowCommandletAudio",
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
            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(configFile, cancellationToken).ConfigureAwait(false));
            if (config.RootElement.TryGetProperty("reportPath", out var report) && report.ValueKind == JsonValueKind.String && File.Exists(report.GetString()))
                ValidateReportErrors(report.GetString()!);
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
        // Conversion must retain Nanite derived data even if the destination project's rendering profile disables it.
        // This isolated conversion project is not the user's project configuration.
        var configRoot = Path.Combine(root, "Config");
        Directory.CreateDirectory(configRoot);
        await File.WriteAllTextAsync(Path.Combine(configRoot, "DefaultEngine.ini"),
            "[/Script/Engine.RendererSettings]\nr.Nanite.ProjectEnabled=True\n\n[/Script/WindowsTargetPlatform.WindowsTargetSettings]\n+D3D12TargetedShaderFormats=PCD3D_SM6\n", cancellationToken).ConfigureAwait(false);
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

    private static void ValidateReportErrors(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException($"Rapport de conversion absent : {path}");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Rapport de conversion invalide : {path}");
        if (errors.GetArrayLength() > 0)
            throw new InvalidOperationException($"Conversion arrêtée avant installation : {string.Join(" · ", errors.EnumerateArray().Take(5).Select(item => item.ToString()))}. Rapport : {path}");
    }

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

    internal static string BuildVariantDirectory(UnrealVersionBridgeRequest request) => CombineContained(
        request.StorageRoot,
        Path.Combine(
            SanitizeSegment(request.AssetId),
            SanitizeSegment(request.ArtifactId),
            $"UE_{request.TargetEngine.Version.Major}.{request.TargetEngine.Version.Minor}"));

    internal static void PublishVariant(string publishing, string target)
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
