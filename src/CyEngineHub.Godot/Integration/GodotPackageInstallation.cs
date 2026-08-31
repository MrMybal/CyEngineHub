using CyEngineHub.Engines;

namespace CyEngineHub.Godot.Integration;

public sealed partial class GodotEngineModule
{
    public async Task<LocalEnginePackageInstallPlan> PreparePackageInstallAsync(
        string sourceDirectory,
        LocalEngineProject targetProject,
        CancellationToken cancellationToken = default)
    {
        ValidateGodotPackageTarget(targetProject);
        var source = ResolveGodotAddonSource(sourceDirectory);
        var addonId = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
        ValidateGodotAddonId(addonId, source);
        var metadata = ReadInstallableGodotAddon(source, addonId);
        var destination = Path.Combine(targetProject.RootPath, "addons", addonId);
        var workspace = Path.Combine(
            targetProject.RootPath,
            ".cyenginehub",
            "PackageOperations",
            "Godot");
        return await LocalEnginePackageTransaction.PrepareAsync(
                EngineFamily.Godot,
                Descriptor.Id,
                targetProject,
                addonId,
                metadata.Name,
                string.IsNullOrWhiteSpace(metadata.Version) ? "Non déclarée" : metadata.Version,
                source,
                destination,
                workspace,
                "plugin.cfg",
                "L’addon reste désactivé lors d’une première installation. Un remplacement conserve toutefois le même chemin qu’un plugin déjà activé.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LocalEnginePackageInstallResult> InstallPackageAsync(
        LocalEnginePackageInstallPlan plan,
        IProgress<LocalEnginePackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Family != EngineFamily.Godot ||
            !string.Equals(plan.ProviderId, Descriptor.Id, StringComparison.Ordinal))
        {
            throw GodotPackageFailure("Le plan n’appartient pas au provider Godot.", "ValidateGodotPlan", plan.DestinationDirectory);
        }

        ValidateGodotPackageTarget(plan.TargetProject);
        ValidateGodotAddonId(plan.PackageId, plan.DestinationDirectory);
        var expectedDestination = Path.Combine(
            plan.TargetProject.RootPath,
            "addons",
            plan.PackageId);
        var expectedWorkspace = Path.Combine(
            plan.TargetProject.RootPath,
            ".cyenginehub",
            "PackageOperations",
            "Godot");
        if (!SameGodotPackagePath(expectedDestination, plan.DestinationDirectory) ||
            !SameGodotPackagePath(expectedWorkspace, plan.WorkspaceDirectory))
        {
            throw GodotPackageFailure("La destination Godot du plan est incohérente.", "ValidateGodotPlan", plan.DestinationDirectory);
        }

        return await LocalEnginePackageTransaction.ExecuteAsync(
                plan,
                async (staging, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    var metadata = ReadInstallableGodotAddon(staging, plan.PackageId);
                    if (!string.Equals(metadata.Name, plan.DisplayName, StringComparison.Ordinal) ||
                        !string.Equals(
                            string.IsNullOrWhiteSpace(metadata.Version) ? "Non déclarée" : metadata.Version,
                            plan.Version,
                            StringComparison.Ordinal))
                    {
                        throw GodotPackageFailure(
                            "Le plugin.cfg copié ne correspond plus au plan confirmé.",
                            "VerifyGodotAddon",
                            staging);
                    }

                    await Task.CompletedTask;
                },
                progress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LocalEnginePackageInstallReceipt>> LoadPackageInstallHistoryAsync(
        IReadOnlyCollection<LocalEngineProject> projects,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var receipts = new List<LocalEnginePackageInstallReceipt>();
        foreach (var project in projects.Where(item =>
                     item.Family == EngineFamily.Godot &&
                     string.Equals(item.ProviderId, Descriptor.Id, StringComparison.Ordinal)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateGodotPackageTarget(project);
            receipts.AddRange(await LocalEnginePackageTransaction.LoadReceiptsAsync(
                    project,
                    EngineFamily.Godot,
                    Descriptor.Id,
                    GetGodotPackageWorkspace(project),
                    cancellationToken)
                .ConfigureAwait(false));
        }

        return receipts.OrderByDescending(receipt => receipt.InstalledAt).ToArray();
    }

    public async Task<LocalEnginePackageRollbackPlan> PreparePackageRollbackAsync(
        LocalEnginePackageInstallReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ValidateGodotReceipt(receipt);
        return await LocalEnginePackageTransaction.PrepareRollbackAsync(receipt, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LocalEnginePackageRollbackResult> RollbackPackageAsync(
        LocalEnginePackageRollbackPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateGodotReceipt(plan.Receipt);
        return await LocalEnginePackageTransaction.RollbackAsync(plan, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LocalEnginePackageRemovalPlan> PreparePackageRemovalAsync(
        LocalEnginePackage package,
        LocalEngineProject targetProject,
        CancellationToken cancellationToken = default)
    {
        ValidateGodotPackageTarget(targetProject);
        var addonId = ValidateRemovableGodotPackage(package, targetProject);
        return await LocalEnginePackageTransaction.PrepareRemovalAsync(
                package,
                targetProject,
                addonId,
                package.RootPath,
                GetGodotPackageWorkspace(targetProject),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LocalEnginePackageRemovalResult> RemovePackageAsync(
        LocalEnginePackageRemovalPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateGodotPackageTarget(plan.TargetProject);
        var addonId = ValidateRemovableGodotPackage(plan.Package, plan.TargetProject);
        var expectedDestination = Path.Combine(
            plan.TargetProject.RootPath,
            "addons",
            addonId);
        if (!SameGodotPackagePath(expectedDestination, plan.DestinationDirectory) ||
            !SameGodotPackagePath(GetGodotPackageWorkspace(plan.TargetProject), plan.WorkspaceDirectory))
        {
            throw GodotPackageFailure(
                "Les chemins du plan de suppression Godot sont incohérents.",
                "ValidateGodotRemovalPlan",
                plan.DestinationDirectory);
        }

        return await LocalEnginePackageTransaction.ExecuteRemovalAsync(plan, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LocalEnginePackageRemovalReceipt>> LoadPackageRemovalHistoryAsync(
        IReadOnlyCollection<LocalEngineProject> projects,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var receipts = new List<LocalEnginePackageRemovalReceipt>();
        foreach (var project in projects.Where(item =>
                     item.Family == EngineFamily.Godot &&
                     string.Equals(item.ProviderId, Descriptor.Id, StringComparison.Ordinal)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateGodotPackageTarget(project);
            receipts.AddRange(await LocalEnginePackageTransaction.LoadRemovalReceiptsAsync(
                    project,
                    EngineFamily.Godot,
                    Descriptor.Id,
                    GetGodotPackageWorkspace(project),
                    cancellationToken)
                .ConfigureAwait(false));
        }

        return receipts.OrderByDescending(receipt => receipt.RemovedAt).ToArray();
    }

    public async Task<LocalEnginePackageRestorePlan> PreparePackageRestoreAsync(
        LocalEnginePackageRemovalReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ValidateGodotRemovalReceipt(receipt);
        return await LocalEnginePackageTransaction.PrepareRestoreAsync(receipt, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LocalEnginePackageRestoreResult> RestorePackageAsync(
        LocalEnginePackageRestorePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateGodotRemovalReceipt(plan.Receipt);
        return await LocalEnginePackageTransaction.ExecuteRestoreAsync(plan, cancellationToken)
            .ConfigureAwait(false);
    }

    private static GodotInstallMetadata ReadInstallableGodotAddon(string root, string addonId)
    {
        var configurationPath = Path.Combine(root, "plugin.cfg");
        var metadata = ReadGodotPluginMetadata(configurationPath);
        if (metadata.Error is not null || string.IsNullOrWhiteSpace(metadata.Name))
        {
            throw GodotPackageFailure(
                metadata.Error ?? "Le nom du plugin Godot est absent.",
                "ParseGodotAddon",
                configurationPath);
        }

        if (string.IsNullOrWhiteSpace(metadata.Script) ||
            !TryResolveGodotAddonSourceScript(root, addonId, metadata.Script, out _))
        {
            throw GodotPackageFailure(
                "Le script déclaré par plugin.cfg est absent, externe ou non sûr.",
                "ParseGodotAddon",
                configurationPath);
        }

        return new GodotInstallMetadata(metadata.Name.Trim(), metadata.Version.Trim());
    }

    private static string ResolveGodotAddonSource(string sourceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        var source = Path.GetFullPath(sourceDirectory.Trim().Trim('"'));
        if (File.Exists(source) &&
            string.Equals(Path.GetFileName(source), "plugin.cfg", StringComparison.OrdinalIgnoreCase))
        {
            source = Path.GetDirectoryName(source)!;
        }

        if (!Directory.Exists(source) || IsReparsePoint(source))
        {
            throw GodotPackageFailure("Le dossier source Godot est absent ou constitue un lien.", "ValidateGodotSource", source);
        }

        if (File.Exists(Path.Combine(source, "plugin.cfg")))
        {
            return source;
        }

        var addons = Path.Combine(source, "addons");
        if (!Directory.Exists(addons) || IsReparsePoint(addons))
        {
            throw GodotPackageFailure(
                "Sélectionnez un addon contenant plugin.cfg ou un package avec addons/<id>/plugin.cfg.",
                "ValidateGodotSource",
                source);
        }

        var candidates = Directory
            .EnumerateDirectories(addons, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !IsReparsePoint(path) && File.Exists(Path.Combine(path, "plugin.cfg")))
            .Take(2)
            .ToArray();
        if (candidates.Length != 1)
        {
            throw GodotPackageFailure(
                "Le package Godot doit contenir exactement un addon installable.",
                "ValidateGodotSource",
                addons);
        }

        return candidates[0];
    }

    private static bool TryResolveGodotAddonSourceScript(
        string addonRoot,
        string addonId,
        string script,
        out string resolvedPath)
    {
        resolvedPath = string.Empty;
        try
        {
            string relative;
            var expectedPrefix = $"res://addons/{addonId}/";
            if (script.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            {
                if (!script.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                relative = script[expectedPrefix.Length..];
            }
            else
            {
                relative = script;
            }

            resolvedPath = Path.GetFullPath(Path.Combine(
                addonRoot,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(addonRoot));
            var relativeToRoot = Path.GetRelativePath(normalizedRoot, resolvedPath);
            return !Path.IsPathRooted(relativeToRoot) &&
                   !relativeToRoot.Equals("..", StringComparison.Ordinal) &&
                   !relativeToRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                   File.Exists(resolvedPath) &&
                   !IsReparsePoint(resolvedPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void ValidateGodotAddonId(string addonId, string path)
    {
        if (string.IsNullOrWhiteSpace(addonId) ||
            !string.Equals(addonId, Path.GetFileName(addonId), StringComparison.Ordinal) ||
            addonId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            addonId is "." or "..")
        {
            throw GodotPackageFailure("L’identifiant de dossier de l’addon est non sûr.", "ValidateGodotAddon", path);
        }
    }

    private static void ValidateGodotPackageTarget(LocalEngineProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var marker = Path.Combine(project.RootPath, "project.godot");
        if (project.Family != EngineFamily.Godot ||
            !string.Equals(project.ProviderId, "godot", StringComparison.Ordinal) ||
            !Directory.Exists(project.RootPath) ||
            IsReparsePoint(project.RootPath) ||
            !File.Exists(marker) ||
            IsReparsePoint(marker))
        {
            throw GodotPackageFailure("Le projet Godot cible n’est plus valide.", "ValidateGodotTarget", project.RootPath);
        }
    }

    private static void ValidateGodotReceipt(LocalEnginePackageInstallReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.Family != EngineFamily.Godot ||
            !string.Equals(receipt.ProviderId, "godot", StringComparison.Ordinal))
        {
            throw GodotPackageFailure("Le reçu n’appartient pas au provider Godot.", "ValidateGodotReceipt", receipt.DestinationDirectory);
        }

        ValidateGodotPackageTarget(receipt.TargetProject);
        ValidateGodotAddonId(receipt.PackageId, receipt.DestinationDirectory);
        var expectedDestination = Path.Combine(
            receipt.TargetProject.RootPath,
            "addons",
            receipt.PackageId);
        if (!SameGodotPackagePath(expectedDestination, receipt.DestinationDirectory) ||
            !SameGodotPackagePath(GetGodotPackageWorkspace(receipt.TargetProject), receipt.WorkspaceDirectory))
        {
            throw GodotPackageFailure("Les chemins du reçu Godot sont incohérents.", "ValidateGodotReceipt", receipt.DestinationDirectory);
        }
    }

    private static string ValidateRemovableGodotPackage(
        LocalEnginePackage package,
        LocalEngineProject targetProject)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Family != EngineFamily.Godot ||
            !string.Equals(package.ProviderId, "godot", StringComparison.Ordinal) ||
            !string.Equals(package.ProjectId, targetProject.Id, StringComparison.Ordinal))
        {
            throw GodotPackageFailure(
                "Le package n’appartient pas au projet Godot cible.",
                "ValidateGodotRemoval",
                package.RootPath);
        }

        if (package.State.StartsWith("Plugin éditeur", StringComparison.OrdinalIgnoreCase) &&
            !package.State.Contains("désactivé", StringComparison.OrdinalIgnoreCase))
        {
            throw GodotPackageFailure(
                "Désactivez d’abord le plugin dans Godot et actualisez son état afin d’éviter une entrée enabled_plugins orpheline.",
                "ValidateGodotRemoval",
                package.ManifestPath);
        }

        var addonId = Path.GetFileName(Path.TrimEndingDirectorySeparator(package.RootPath));
        ValidateGodotAddonId(addonId, package.RootPath);
        var expected = Path.Combine(targetProject.RootPath, "addons", addonId);
        if (!SameGodotPackagePath(expected, package.RootPath) ||
            !Path.GetFullPath(package.ManifestPath).StartsWith(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw GodotPackageFailure(
                "Le package Godot ne correspond pas à un addon contenu dans le projet cible.",
                "ValidateGodotRemoval",
                package.RootPath);
        }

        return addonId;
    }

    private static void ValidateGodotRemovalReceipt(LocalEnginePackageRemovalReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.Family != EngineFamily.Godot ||
            !string.Equals(receipt.ProviderId, "godot", StringComparison.Ordinal))
        {
            throw GodotPackageFailure(
                "Le reçu de suppression n’appartient pas au provider Godot.",
                "ValidateGodotRemovalReceipt",
                receipt.DestinationDirectory);
        }

        ValidateGodotPackageTarget(receipt.TargetProject);
        ValidateGodotAddonId(receipt.PackageId, receipt.DestinationDirectory);
        var expectedDestination = Path.Combine(
            receipt.TargetProject.RootPath,
            "addons",
            receipt.PackageId);
        if (!SameGodotPackagePath(expectedDestination, receipt.DestinationDirectory) ||
            !SameGodotPackagePath(GetGodotPackageWorkspace(receipt.TargetProject), receipt.WorkspaceDirectory))
        {
            throw GodotPackageFailure(
                "Les chemins du reçu de suppression Godot sont incohérents.",
                "ValidateGodotRemovalReceipt",
                receipt.DestinationDirectory);
        }
    }

    private static string GetGodotPackageWorkspace(LocalEngineProject project) =>
        Path.Combine(project.RootPath, ".cyenginehub", "PackageOperations", "Godot");

    private static bool SameGodotPackagePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static LocalEnginePackageOperationException GodotPackageFailure(
        string message,
        string operation,
        string? path) =>
        new(message, operation, path);

    private sealed record GodotInstallMetadata(string Name, string Version);
}
