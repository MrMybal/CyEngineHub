using System.Text.Json;
using System.Text.RegularExpressions;
using CyEngineHub.Engines;

namespace CyEngineHub.Unity.Integration;

public sealed partial class UnityEngineModule
{
    public async Task<LocalEnginePackageInstallPlan> PreparePackageInstallAsync(
        string sourceDirectory,
        LocalEngineProject targetProject,
        CancellationToken cancellationToken = default)
    {
        ValidateUnityPackageTarget(targetProject);
        var source = ResolveUnityPackageSource(sourceDirectory);
        var metadata = ReadInstallableUnityPackage(source);
        EnsureUnityPackageCompatibility(metadata, targetProject);
        var destination = Path.Combine(targetProject.RootPath, "Packages", metadata.Name);
        var workspace = Path.Combine(
            targetProject.RootPath,
            ".cyenginehub",
            "PackageOperations",
            "Unity");
        return await LocalEnginePackageTransaction.PrepareAsync(
                EngineFamily.Unity,
                Descriptor.Id,
                targetProject,
                metadata.Name,
                string.IsNullOrWhiteSpace(metadata.DisplayName) ? metadata.Name : metadata.DisplayName,
                string.IsNullOrWhiteSpace(metadata.Version) ? "Non déclarée" : metadata.Version,
                source,
                destination,
                workspace,
                "package.json",
                "Un package Unity peut contenir du code C# ou natif que l’Editor chargera à l’ouverture du projet.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LocalEnginePackageInstallResult> InstallPackageAsync(
        LocalEnginePackageInstallPlan plan,
        IProgress<LocalEnginePackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Family != EngineFamily.Unity ||
            !string.Equals(plan.ProviderId, Descriptor.Id, StringComparison.Ordinal))
        {
            throw PackageFailure("Le plan n’appartient pas au provider Unity.", "ValidateUnityPlan", plan.DestinationDirectory);
        }

        ValidateUnityPackageTarget(plan.TargetProject);
        var expectedDestination = Path.Combine(
            plan.TargetProject.RootPath,
            "Packages",
            plan.PackageId);
        var expectedWorkspace = Path.Combine(
            plan.TargetProject.RootPath,
            ".cyenginehub",
            "PackageOperations",
            "Unity");
        if (!SameUnityPackagePath(expectedDestination, plan.DestinationDirectory) ||
            !SameUnityPackagePath(expectedWorkspace, plan.WorkspaceDirectory))
        {
            throw PackageFailure("La destination Unity du plan est incohérente.", "ValidateUnityPlan", plan.DestinationDirectory);
        }

        return await LocalEnginePackageTransaction.ExecuteAsync(
                plan,
                async (staging, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    var metadata = ReadInstallableUnityPackage(staging);
                    if (!string.Equals(metadata.Name, plan.PackageId, StringComparison.Ordinal) ||
                        !string.Equals(
                            string.IsNullOrWhiteSpace(metadata.Version) ? "Non déclarée" : metadata.Version,
                            plan.Version,
                            StringComparison.Ordinal))
                    {
                        throw PackageFailure(
                            "Le package Unity copié ne correspond plus au plan confirmé.",
                            "VerifyUnityPackage",
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
                     item.Family == EngineFamily.Unity &&
                     string.Equals(item.ProviderId, Descriptor.Id, StringComparison.Ordinal)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateUnityPackageTarget(project);
            receipts.AddRange(await LocalEnginePackageTransaction.LoadReceiptsAsync(
                    project,
                    EngineFamily.Unity,
                    Descriptor.Id,
                    GetUnityPackageWorkspace(project),
                    cancellationToken)
                .ConfigureAwait(false));
        }

        return receipts.OrderByDescending(receipt => receipt.InstalledAt).ToArray();
    }

    public async Task<LocalEnginePackageRollbackPlan> PreparePackageRollbackAsync(
        LocalEnginePackageInstallReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ValidateUnityReceipt(receipt);
        return await LocalEnginePackageTransaction.PrepareRollbackAsync(receipt, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LocalEnginePackageRollbackResult> RollbackPackageAsync(
        LocalEnginePackageRollbackPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateUnityReceipt(plan.Receipt);
        return await LocalEnginePackageTransaction.RollbackAsync(plan, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LocalEnginePackageRemovalPlan> PreparePackageRemovalAsync(
        LocalEnginePackage package,
        LocalEngineProject targetProject,
        CancellationToken cancellationToken = default)
    {
        ValidateUnityPackageTarget(targetProject);
        var packageId = ValidateRemovableUnityPackage(package, targetProject);
        return await LocalEnginePackageTransaction.PrepareRemovalAsync(
                package,
                targetProject,
                packageId,
                package.RootPath,
                GetUnityPackageWorkspace(targetProject),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LocalEnginePackageRemovalResult> RemovePackageAsync(
        LocalEnginePackageRemovalPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateUnityPackageTarget(plan.TargetProject);
        var packageId = ValidateRemovableUnityPackage(plan.Package, plan.TargetProject);
        var expectedDestination = Path.Combine(
            plan.TargetProject.RootPath,
            "Packages",
            packageId);
        if (!SameUnityPackagePath(expectedDestination, plan.DestinationDirectory) ||
            !SameUnityPackagePath(GetUnityPackageWorkspace(plan.TargetProject), plan.WorkspaceDirectory))
        {
            throw PackageFailure(
                "Les chemins du plan de suppression Unity sont incohérents.",
                "ValidateUnityRemovalPlan",
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
                     item.Family == EngineFamily.Unity &&
                     string.Equals(item.ProviderId, Descriptor.Id, StringComparison.Ordinal)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateUnityPackageTarget(project);
            receipts.AddRange(await LocalEnginePackageTransaction.LoadRemovalReceiptsAsync(
                    project,
                    EngineFamily.Unity,
                    Descriptor.Id,
                    GetUnityPackageWorkspace(project),
                    cancellationToken)
                .ConfigureAwait(false));
        }

        return receipts.OrderByDescending(receipt => receipt.RemovedAt).ToArray();
    }

    public async Task<LocalEnginePackageRestorePlan> PreparePackageRestoreAsync(
        LocalEnginePackageRemovalReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ValidateUnityRemovalReceipt(receipt);
        return await LocalEnginePackageTransaction.PrepareRestoreAsync(receipt, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LocalEnginePackageRestoreResult> RestorePackageAsync(
        LocalEnginePackageRestorePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateUnityRemovalReceipt(plan.Receipt);
        return await LocalEnginePackageTransaction.ExecuteRestoreAsync(plan, cancellationToken)
            .ConfigureAwait(false);
    }

    private static UnityInstallMetadata ReadInstallableUnityPackage(string root)
    {
        var manifestPath = Path.Combine(root, "package.json");
        if (!TryOpenJson(
                manifestPath,
                MaximumUnityManifestBytes,
                out var document,
                out var error))
        {
            throw PackageFailure($"package.json invalide : {error}", "ParseUnityPackage", manifestPath);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw PackageFailure("package.json doit contenir un objet JSON.", "ParseUnityPackage", manifestPath);
            }

            var name = ReadJsonString(document.RootElement, "name");
            if (!UnityPackageNamePattern.IsMatch(name))
            {
                throw PackageFailure("Le nom interne du package Unity est absent ou non sûr.", "ParseUnityPackage", manifestPath);
            }

            return new UnityInstallMetadata(
                name,
                ReadJsonString(document.RootElement, "displayName"),
                ReadJsonString(document.RootElement, "version"),
                ReadJsonString(document.RootElement, "unity"));
        }
    }

    private static void EnsureUnityPackageCompatibility(
        UnityInstallMetadata metadata,
        LocalEngineProject targetProject)
    {
        if (string.IsNullOrWhiteSpace(metadata.MinimumUnityVersion))
        {
            return;
        }

        var targetVersion = targetProject.RequiredEditorVersion;
        if (string.IsNullOrWhiteSpace(targetVersion))
        {
            var marker = Path.Combine(
                targetProject.RootPath,
                "ProjectSettings",
                "ProjectVersion.txt");
            targetVersion = File.ReadLines(marker)
                .Take(32)
                .Select(line => Regex.Match(
                    line,
                    "^\\s*m_EditorVersion\\s*:\\s*(?<version>[^\\s]+)\\s*$",
                    RegexOptions.CultureInvariant))
                .FirstOrDefault(match => match.Success)?
                .Groups["version"].Value ?? string.Empty;
        }

        var required = VersionSortKey(metadata.MinimumUnityVersion);
        var target = VersionSortKey(targetVersion);
        if (required > target)
        {
            throw PackageFailure(
                $"Le package requiert Unity {metadata.MinimumUnityVersion} ou plus récent, " +
                $"mais le projet utilise {targetVersion}.",
                "ValidateUnityCompatibility",
                targetProject.RootPath);
        }
    }

    private static string ResolveUnityPackageSource(string sourceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        var source = Path.GetFullPath(sourceDirectory.Trim().Trim('"'));
        if (File.Exists(source) &&
            string.Equals(Path.GetFileName(source), "package.json", StringComparison.OrdinalIgnoreCase))
        {
            source = Path.GetDirectoryName(source)!;
        }

        if (!Directory.Exists(source) || IsReparsePoint(source))
        {
            throw PackageFailure("Le dossier source Unity est absent ou constitue un lien.", "ValidateUnitySource", source);
        }

        return source;
    }

    private static void ValidateUnityPackageTarget(LocalEngineProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var marker = Path.Combine(project.RootPath, "ProjectSettings", "ProjectVersion.txt");
        if (project.Family != EngineFamily.Unity ||
            !string.Equals(project.ProviderId, "unity", StringComparison.Ordinal) ||
            !Directory.Exists(project.RootPath) ||
            IsReparsePoint(project.RootPath) ||
            !Directory.Exists(Path.Combine(project.RootPath, "Assets")) ||
            !File.Exists(marker) ||
            IsReparsePoint(marker))
        {
            throw PackageFailure("Le projet Unity cible n’est plus valide.", "ValidateUnityTarget", project.RootPath);
        }
    }

    private static void ValidateUnityReceipt(LocalEnginePackageInstallReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.Family != EngineFamily.Unity ||
            !string.Equals(receipt.ProviderId, "unity", StringComparison.Ordinal))
        {
            throw PackageFailure("Le reçu n’appartient pas au provider Unity.", "ValidateUnityReceipt", receipt.DestinationDirectory);
        }

        ValidateUnityPackageTarget(receipt.TargetProject);
        var expectedDestination = Path.Combine(
            receipt.TargetProject.RootPath,
            "Packages",
            receipt.PackageId);
        if (!SameUnityPackagePath(expectedDestination, receipt.DestinationDirectory) ||
            !SameUnityPackagePath(GetUnityPackageWorkspace(receipt.TargetProject), receipt.WorkspaceDirectory))
        {
            throw PackageFailure("Les chemins du reçu Unity sont incohérents.", "ValidateUnityReceipt", receipt.DestinationDirectory);
        }
    }

    private static string ValidateRemovableUnityPackage(
        LocalEnginePackage package,
        LocalEngineProject targetProject)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Family != EngineFamily.Unity ||
            !string.Equals(package.ProviderId, "unity", StringComparison.Ordinal) ||
            !string.Equals(package.ProjectId, targetProject.Id, StringComparison.Ordinal) ||
            !string.Equals(package.State, "Package embarqué", StringComparison.Ordinal) ||
            !string.Equals(package.Source, "UPM · embarqué", StringComparison.Ordinal))
        {
            throw PackageFailure(
                "Seuls les packages Unity embarqués non déclarés dans manifest.json peuvent être retirés sans modifier le manifeste.",
                "ValidateUnityRemoval",
                package.RootPath);
        }

        var packageId = Path.GetFileName(Path.TrimEndingDirectorySeparator(package.RootPath));
        if (!UnityPackageNamePattern.IsMatch(packageId))
        {
            throw PackageFailure(
                "L’identifiant du package Unity à supprimer est non sûr.",
                "ValidateUnityRemoval",
                package.RootPath);
        }

        var expected = Path.Combine(targetProject.RootPath, "Packages", packageId);
        if (!SameUnityPackagePath(expected, package.RootPath) ||
            !SameUnityPackagePath(Path.Combine(expected, "package.json"), package.ManifestPath))
        {
            throw PackageFailure(
                "Le package Unity ne correspond pas à un dossier embarqué du projet cible.",
                "ValidateUnityRemoval",
                package.RootPath);
        }

        var metadata = ReadInstallableUnityPackage(package.RootPath);
        if (!string.Equals(metadata.Name, packageId, StringComparison.Ordinal))
        {
            throw PackageFailure(
                "Le nom interne du package Unity ne correspond pas à son dossier.",
                "ValidateUnityRemoval",
                package.ManifestPath);
        }

        return packageId;
    }

    private static void ValidateUnityRemovalReceipt(LocalEnginePackageRemovalReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.Family != EngineFamily.Unity ||
            !string.Equals(receipt.ProviderId, "unity", StringComparison.Ordinal) ||
            !UnityPackageNamePattern.IsMatch(receipt.PackageId))
        {
            throw PackageFailure(
                "Le reçu de suppression n’appartient pas au provider Unity.",
                "ValidateUnityRemovalReceipt",
                receipt.DestinationDirectory);
        }

        ValidateUnityPackageTarget(receipt.TargetProject);
        var expectedDestination = Path.Combine(
            receipt.TargetProject.RootPath,
            "Packages",
            receipt.PackageId);
        if (!SameUnityPackagePath(expectedDestination, receipt.DestinationDirectory) ||
            !SameUnityPackagePath(GetUnityPackageWorkspace(receipt.TargetProject), receipt.WorkspaceDirectory))
        {
            throw PackageFailure(
                "Les chemins du reçu de suppression Unity sont incohérents.",
                "ValidateUnityRemovalReceipt",
                receipt.DestinationDirectory);
        }
    }

    private static string GetUnityPackageWorkspace(LocalEngineProject project) =>
        Path.Combine(project.RootPath, ".cyenginehub", "PackageOperations", "Unity");

    private static bool SameUnityPackagePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static LocalEnginePackageOperationException PackageFailure(
        string message,
        string operation,
        string? path) =>
        new(message, operation, path);

    private sealed record UnityInstallMetadata(
        string Name,
        string DisplayName,
        string Version,
        string MinimumUnityVersion);
}
