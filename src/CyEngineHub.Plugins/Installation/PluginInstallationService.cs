using System.Diagnostics;
using CyEngineHub.Plugins.Compatibility;
using CyEngineHub.Plugins.Models;
using CyEngineHub.Plugins.Parsing;

namespace CyEngineHub.Plugins.Installation;

public interface IPluginInstallationService
{
    Task<PluginInstallPlan> PlanAsync(
        UnrealPluginDescriptor plugin,
        PluginInstallTarget target,
        string targetPlatform = "Win64",
        CancellationToken cancellationToken = default);

    Task<PluginInstallResult> InstallAsync(
        PluginInstallPlan plan,
        IProgress<PluginInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class PluginInstallationService : IPluginInstallationService
{
    private const int MaximumFiles = 250_000;
    private const int CopyBufferSize = 128 * 1024;
    private static readonly HashSet<string> AlwaysExcludedDirectories = new(
        [".git", ".vs", "Intermediate", "Saved", "DerivedDataCache"],
        StringComparer.OrdinalIgnoreCase);

    private readonly IPluginCompatibilityService _compatibilityService;
    private readonly IUpluginDescriptorParser _parser;
    private readonly StringComparison _pathComparison;

    public PluginInstallationService(
        IPluginCompatibilityService compatibilityService,
        IUpluginDescriptorParser parser)
    {
        _compatibilityService = compatibilityService;
        _parser = parser;
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public Task<PluginInstallPlan> PlanAsync(
        UnrealPluginDescriptor plugin,
        PluginInstallTarget target,
        string targetPlatform = "Win64",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInstallable(plugin);

        var sourceRoot = Path.GetFullPath(plugin.RootPath);
        var pluginsDirectory = Path.GetFullPath(target.PluginsDirectory);
        var destination = ResolveDestination(pluginsDirectory, plugin.Name);
        EnsureNoOverlap(sourceRoot, destination, plugin.DescriptorPath);
        var compatibility = _compatibilityService.Evaluate(plugin, target.Engine, targetPlatform);
        if (!compatibility.IsCompatible)
        {
            throw new PluginOperationException(
                string.Join(" ", compatibility.Warnings),
                "PlanInstall",
                plugin.DescriptorPath,
                destination);
        }

        var manifest = CreateManifest(plugin, cancellationToken);
        return Task.FromResult(new PluginInstallPlan(
            plugin,
            target,
            targetPlatform,
            destination,
            Directory.Exists(destination),
            manifest.Files.Count,
            manifest.Files.Sum(file => file.Length),
            manifest.SkippedReparsePoints,
            compatibility));
    }

    public async Task<PluginInstallResult> InstallAsync(
        PluginInstallPlan plan,
        IProgress<PluginInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnsureInstallable(plan.Plugin);
        var stopwatch = Stopwatch.StartNew();
        var pluginsDirectory = Path.GetFullPath(plan.Target.PluginsDirectory);
        var destination = ResolveDestination(pluginsDirectory, plan.Plugin.Name);
        if (!PathsEqual(destination, Path.GetFullPath(plan.DestinationDirectory)))
        {
            throw new PluginOperationException(
                "La destination du plan d’installation n’est plus valide.",
                "Install",
                plan.Plugin.DescriptorPath,
                destination);
        }

        EnsureNoOverlap(Path.GetFullPath(plan.Plugin.RootPath), destination, plan.Plugin.DescriptorPath);
        var compatibility = _compatibilityService.Evaluate(
            plan.Plugin,
            plan.Target.Engine,
            plan.TargetPlatform);
        if (!compatibility.IsCompatible)
        {
            throw new PluginOperationException(
                string.Join(" ", compatibility.Warnings),
                "Install",
                plan.Plugin.DescriptorPath,
                destination);
        }

        Directory.CreateDirectory(pluginsDirectory);
        EnsureRealDirectory(pluginsDirectory, "Le dossier Plugins cible est un lien ou un point de jonction.", plan);
        if (File.Exists(destination))
        {
            throw new PluginOperationException(
                "Un fichier occupe la destination prévue pour le plugin.",
                "Install",
                plan.Plugin.DescriptorPath,
                destination);
        }

        if (Directory.Exists(destination))
        {
            EnsureRealDirectory(destination, "Le plugin cible est un lien ou un point de jonction.", plan);
        }

        var staging = Path.Combine(pluginsDirectory, $".cyenginehub-staging-{Guid.NewGuid():N}");
        var backupDirectory = default(string);
        long copiedBytes = 0;
        long copiedFiles = 0;
        try
        {
            Directory.CreateDirectory(staging);
            var manifest = CreateManifest(plan.Plugin, cancellationToken);
            for (var index = 0; index < manifest.Files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = manifest.Files[index];
                var targetPath = Path.GetFullPath(Path.Combine(staging, file.RelativePath));
                if (!IsDescendantOrEqual(staging, targetPath))
                {
                    throw new PluginOperationException(
                        "Un chemin de fichier sort du dossier de staging.",
                        "Install",
                        plan.Plugin.DescriptorPath,
                        targetPath);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                await CopyFileAsync(file.SourcePath, targetPath, cancellationToken).ConfigureAwait(false);
                copiedFiles++;
                copiedBytes += file.Length;
                progress?.Report(new PluginInstallProgress(
                    copiedFiles,
                    manifest.Files.Count,
                    $"Copie · {file.RelativePath}"));
            }

            var stagedDescriptor = Path.Combine(staging, Path.GetFileName(plan.Plugin.DescriptorPath));
            var verified = await _parser.ParseAsync(
                    new PluginCandidate(
                        stagedDescriptor,
                        plan.Plugin.SourceKind,
                        plan.Plugin.SourceLabel,
                        plan.Plugin.SourceOwnerId),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(verified.Name, plan.Plugin.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new PluginOperationException(
                    "Le descripteur copié ne correspond pas au plugin prévu.",
                    "VerifyInstall",
                    plan.Plugin.DescriptorPath,
                    stagedDescriptor);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(destination))
            {
                if (!plan.AllowReplacement) throw new PluginOperationException("Un plugin existe déjà à cette destination ; import éditeur arrêté.", "Install", plan.Plugin.DescriptorPath, destination);
                var targetParent = Directory.GetParent(Path.TrimEndingDirectorySeparator(pluginsDirectory))
                    ?? throw new PluginOperationException("La racine du volume ne peut pas héberger les plugins.", "Install", plan.Plugin.DescriptorPath);
                var backupRoot = Path.Combine(targetParent.FullName, ".cyenginehub-backups");
                Directory.CreateDirectory(backupRoot);
                EnsureRealDirectory(
                    backupRoot,
                    "Le dossier de sauvegarde est un lien ou un point de jonction.",
                    plan);
                backupDirectory = Path.Combine(
                    backupRoot,
                    $"{plan.Plugin.Name}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
                Directory.Move(destination, backupDirectory);
            }

            try
            {
                Directory.Move(staging, destination);
            }
            catch
            {
                if (backupDirectory is not null &&
                    Directory.Exists(backupDirectory) &&
                    !Directory.Exists(destination))
                {
                    Directory.Move(backupDirectory, destination);
                    backupDirectory = null;
                }

                throw;
            }

            stopwatch.Stop();
            progress?.Report(new PluginInstallProgress(
                copiedFiles,
                copiedFiles,
                "Plugin installé"));
            return new PluginInstallResult(
                destination,
                backupDirectory,
                copiedFiles,
                copiedBytes,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            SafeDeleteStaging(staging, pluginsDirectory);
            throw;
        }
        catch (PluginOperationException)
        {
            SafeDeleteStaging(staging, pluginsDirectory);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SafeDeleteStaging(staging, pluginsDirectory);
            throw new PluginOperationException(
                $"L’installation transactionnelle a échoué : {exception.Message}",
                "Install",
                plan.Plugin.DescriptorPath,
                destination,
                exception);
        }
    }

    private static void EnsureInstallable(UnrealPluginDescriptor plugin)
    {
        if (plugin.Health == PluginHealth.Broken || !File.Exists(plugin.DescriptorPath))
        {
            throw new PluginOperationException(
                "Le plugin est cassé ou son descripteur est introuvable.",
                "Install",
                plugin.DescriptorPath);
        }
    }

    private static string ResolveDestination(string pluginsDirectory, string pluginName)
    {
        if (string.IsNullOrWhiteSpace(pluginName) ||
            !string.Equals(pluginName, Path.GetFileName(pluginName), StringComparison.Ordinal) ||
            pluginName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new PluginOperationException(
                "Le nom interne du plugin ne peut pas former un dossier sûr.",
                "PlanInstall",
                pluginName,
                pluginsDirectory);
        }

        var destination = Path.GetFullPath(Path.Combine(pluginsDirectory, pluginName));
        if (!IsDirectChild(pluginsDirectory, destination))
        {
            throw new PluginOperationException(
                "La destination calculée sort du dossier Plugins.",
                "PlanInstall",
                pluginName,
                destination);
        }

        return destination;
    }

    private static void EnsureNoOverlap(string source, string destination, string descriptorPath)
    {
        if (IsDescendantOrEqual(source, destination) || IsDescendantOrEqual(destination, source))
        {
            throw new PluginOperationException(
                "La source et la destination du plugin se chevauchent.",
                "PlanInstall",
                descriptorPath,
                destination);
        }
    }

    private static FileManifest CreateManifest(
        UnrealPluginDescriptor plugin,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(plugin.RootPath);
        if (!Directory.Exists(root))
        {
            throw new PluginOperationException(
                "Le dossier source du plugin est introuvable.",
                "PlanInstall",
                plugin.DescriptorPath);
        }

        try
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                throw new PluginOperationException(
                    "Le dossier source du plugin est un lien ou un point de jonction.",
                    "PlanInstall",
                    plugin.DescriptorPath);
            }

            var files = new List<ManifestFile>();
            var stack = new Stack<string>();
            var skippedReparsePoints = 0;
            stack.Push(root);
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = stack.Pop();
                foreach (var file in Directory.GetFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    {
                        skippedReparsePoints++;
                        continue;
                    }

                    var relativePath = Path.GetRelativePath(root, file);
                    if (plugin.HasSource && IsGeneratedModuleOutput(relativePath, plugin.Modules))
                    {
                        continue;
                    }

                    if (files.Count >= MaximumFiles)
                    {
                        throw new PluginOperationException(
                            $"Le plugin dépasse la limite de sécurité de {MaximumFiles:N0} fichiers.",
                            "PlanInstall",
                            plugin.DescriptorPath);
                    }

                    files.Add(new ManifestFile(
                        file,
                        relativePath,
                        new FileInfo(file).Length));
                }

                foreach (var child in Directory.GetDirectories(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(child);
                    if (AlwaysExcludedDirectories.Contains(name))
                    {
                        continue;
                    }

                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    {
                        skippedReparsePoints++;
                        continue;
                    }

                    stack.Push(child);
                }
            }

            return new FileManifest(files, skippedReparsePoints);
        }
        catch (PluginOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PluginOperationException(
                $"Impossible de préparer la copie du plugin : {exception.Message}",
                "PlanInstall",
                plugin.DescriptorPath,
                innerException: exception);
        }
    }

    private static bool IsGeneratedModuleOutput(string relativePath, IReadOnlyList<UnrealPluginModule> modules)
    {
        // UBT writes its modules directly under Binaries/<platform>. Vendor runtimes can share that directory.
        if (!relativePath.StartsWith("Binaries" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = relativePath.Split(Path.DirectorySeparatorChar);
        if (parts.Length < 3 || parts[1].Equals("ThirdParty", StringComparison.OrdinalIgnoreCase)) return false;
        var file = parts[2];
        if (file.EndsWith(".modules", StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".target", StringComparison.OrdinalIgnoreCase)) return true;

        var stem = file.Split('.')[0];
        var separator = stem.IndexOf('-');
        if (separator < 0) return false;
        var target = stem[..separator];
        if (target.StartsWith("lib", StringComparison.OrdinalIgnoreCase)) target = target[3..];
        if (!target.Equals("UnrealEditor", StringComparison.OrdinalIgnoreCase) &&
            !target.Equals("UnrealGame", StringComparison.OrdinalIgnoreCase) &&
            !target.Equals("UE4Editor", StringComparison.OrdinalIgnoreCase) &&
            !target.Equals("UE4Game", StringComparison.OrdinalIgnoreCase)) return false;

        var module = stem[(separator + 1)..].Split('-')[0];
        return modules.Any(item => item.Name.Equals(module, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, CopyBufferSize, cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureRealDirectory(
        string path,
        string message,
        PluginInstallPlan plan)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new PluginOperationException(
                message,
                "Install",
                plan.Plugin.DescriptorPath,
                path);
        }
    }

    private static bool IsDirectChild(string parent, string candidate)
    {
        var parentFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var candidateFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        return string.Equals(
            Path.GetDirectoryName(candidateFull),
            parentFull,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static bool IsDescendantOrEqual(string parent, string candidate)
    {
        var parentFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var candidateFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(parentFull, candidateFull, comparison))
        {
            return true;
        }

        return candidateFull.StartsWith(parentFull + Path.DirectorySeparatorChar, comparison);
    }

    private bool PathsEqual(string first, string second) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(first),
            Path.TrimEndingDirectorySeparator(second),
            _pathComparison);

    private static void SafeDeleteStaging(string staging, string pluginsDirectory)
    {
        if (!Directory.Exists(staging) ||
            !IsDirectChild(pluginsDirectory, staging) ||
            !Path.GetFileName(staging).StartsWith(
                ".cyenginehub-staging-",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Directory.Delete(staging, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The original installation error is more useful than a best-effort cleanup failure.
        }
    }

    private sealed record ManifestFile(string SourcePath, string RelativePath, long Length);

    private sealed record FileManifest(IReadOnlyList<ManifestFile> Files, int SkippedReparsePoints);
}
