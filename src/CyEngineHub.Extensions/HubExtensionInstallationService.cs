using System.Security.Cryptography;

namespace CyEngineHub.Extensions;

public interface IHubExtensionInstallationService
{
    Task<HubExtensionInstallPlan> PrepareAsync(
        string sourceDirectory,
        string userExtensionsRoot,
        CancellationToken cancellationToken = default);

    Task<HubExtensionInstallResult> ExecuteAsync(
        HubExtensionInstallPlan plan,
        CancellationToken cancellationToken = default);
}

public sealed class HubExtensionInstallationService(
    IHubExtensionManifestParser manifestParser) : IHubExtensionInstallationService
{
    private const int MaximumFileCount = 2_000;
    private const long MaximumTotalBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(15);

    public async Task<HubExtensionInstallPlan> PrepareAsync(
        string sourceDirectory,
        string userExtensionsRoot,
        CancellationToken cancellationToken = default)
    {
        var source = ValidatePackageRoot(sourceDirectory);
        var installRoot = ValidateInstallRoot(userExtensionsRoot);
        if (IsSameOrChild(source, installRoot) || IsSameOrChild(installRoot, source))
        {
            throw new HubExtensionException(
                "Le package source et la bibliothèque d’extensions doivent être séparés.",
                "PrepareInstall",
                source);
        }

        var manifest = await manifestParser.ParseAsync(source, cancellationToken)
            .ConfigureAwait(false);
        var files = await InventoryAsync(source, cancellationToken).ConfigureAwait(false);
        var target = HubExtensionManifestParser.CombineContained(installRoot, manifest.Id);
        if (Directory.Exists(target) && HubExtensionManifestParser.IsReparsePoint(target))
        {
            throw new HubExtensionException(
                "L’extension installée existante constitue un lien et ne peut pas être remplacée.",
                "PrepareInstall",
                target);
        }

        var now = DateTimeOffset.UtcNow;
        return new HubExtensionInstallPlan(
            Guid.NewGuid(),
            manifest,
            source,
            target,
            files,
            files.Sum(file => file.Length),
            Directory.Exists(target),
            now,
            now + PlanLifetime);
    }

    public async Task<HubExtensionInstallResult> ExecuteAsync(
        HubExtensionInstallPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Id == Guid.Empty || DateTimeOffset.UtcNow > plan.ExpiresAt)
        {
            throw new HubExtensionException(
                "Le plan d’installation de l’extension a expiré.",
                "ExecuteInstall",
                plan.SourceDirectory);
        }

        var source = ValidatePackageRoot(plan.SourceDirectory);
        var installRoot = ValidateInstallRoot(Path.GetDirectoryName(plan.TargetDirectory)!);
        var expectedTarget = HubExtensionManifestParser.CombineContained(
            installRoot,
            plan.Manifest.Id);
        if (!SamePath(expectedTarget, plan.TargetDirectory))
        {
            throw new HubExtensionException(
                "La destination du plan d’installation est incohérente.",
                "ExecuteInstall",
                plan.TargetDirectory);
        }

        var currentManifest = await manifestParser.ParseAsync(source, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(currentManifest.Id, plan.Manifest.Id, StringComparison.Ordinal) ||
            !string.Equals(currentManifest.Version, plan.Manifest.Version, StringComparison.Ordinal))
        {
            throw new HubExtensionException(
                "Le manifeste a changé après confirmation.",
                "ExecuteInstall",
                source);
        }

        var currentFiles = await InventoryAsync(source, cancellationToken).ConfigureAwait(false);
        if (!InventoriesMatch(plan.Files, currentFiles))
        {
            throw new HubExtensionException(
                "Le package a changé après confirmation.",
                "ExecuteInstall",
                source);
        }

        var stagingParent = HubExtensionManifestParser.CombineContained(installRoot, ".staging");
        var staging = HubExtensionManifestParser.CombineContained(
            stagingParent,
            plan.Id.ToString("N"));
        var backupsParent = HubExtensionManifestParser.CombineContained(installRoot, ".backups");
        var backup = HubExtensionManifestParser.CombineContained(
            backupsParent,
            $"{plan.Manifest.Id}-{plan.Id:N}");
        string? retainedBackup = null;
        Directory.CreateDirectory(stagingParent);
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in currentFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var from = HubExtensionManifestParser.CombineContained(source, file.RelativePath);
                var to = HubExtensionManifestParser.CombineContained(staging, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                await CopyFileAsync(from, to, cancellationToken).ConfigureAwait(false);
            }

            await manifestParser.ParseAsync(staging, cancellationToken).ConfigureAwait(false);
            if (Directory.Exists(expectedTarget))
            {
                if (HubExtensionManifestParser.IsReparsePoint(expectedTarget))
                {
                    throw new HubExtensionException(
                        "La version installée constitue un lien et ne peut pas être remplacée.",
                        "ExecuteInstall",
                        expectedTarget);
                }

                Directory.CreateDirectory(backupsParent);
                Directory.Move(expectedTarget, backup);
                retainedBackup = backup;
            }

            try
            {
                Directory.Move(staging, expectedTarget);
            }
            catch
            {
                if (retainedBackup is not null &&
                    Directory.Exists(retainedBackup) &&
                    !Directory.Exists(expectedTarget))
                {
                    Directory.Move(retainedBackup, expectedTarget);
                    retainedBackup = null;
                }

                throw;
            }

            var installedManifest = await manifestParser.ParseAsync(expectedTarget, cancellationToken)
                .ConfigureAwait(false);
            return new HubExtensionInstallResult(
                new HubExtensionSnapshot(
                    installedManifest,
                    expectedTarget,
                    HubExtensionOrigin.User,
                    HubExtensionHealth.Ready,
                    null),
                retainedBackup);
        }
        catch (HubExtensionException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new HubExtensionException(
                $"Échec de l’installation de l’extension : {exception.Message}",
                "ExecuteInstall",
                expectedTarget,
                exception);
        }
        finally
        {
            DeleteContainedDirectory(staging, stagingParent);
        }
    }

    private static string ValidatePackageRoot(string sourceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        var source = Path.GetFullPath(sourceDirectory.Trim().Trim('"'));
        if (!Directory.Exists(source) ||
            IsVolumeRoot(source) ||
            HubExtensionManifestParser.IsReparsePoint(source))
        {
            throw new HubExtensionException(
                "Le dossier du package est absent, trop large ou constitue un lien.",
                "ValidatePackage",
                source);
        }

        return source;
    }

    private static string ValidateInstallRoot(string userExtensionsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userExtensionsRoot);
        var root = Path.GetFullPath(userExtensionsRoot.Trim().Trim('"'));
        if (IsVolumeRoot(root) ||
            (Directory.Exists(root) && HubExtensionManifestParser.IsReparsePoint(root)))
        {
            throw new HubExtensionException(
                "La bibliothèque d’extensions n’est pas un dossier sûr.",
                "ValidateInstallRoot",
                root);
        }

        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<IReadOnlyList<HubExtensionPackageFile>> InventoryAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var files = new List<HubExtensionPackageFile>();
        var directories = new Stack<string>();
        directories.Push(root);
        long totalBytes = 0;
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
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new HubExtensionException(
                        "Les liens symboliques sont interdits dans un package d’extension.",
                        "InventoryPackage",
                        entry);
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Push(entry);
                    continue;
                }

                var info = new FileInfo(entry);
                totalBytes = checked(totalBytes + info.Length);
                if (files.Count >= MaximumFileCount || totalBytes > MaximumTotalBytes)
                {
                    throw new HubExtensionException(
                        "Le package dépasse les limites de 2 000 fichiers ou 512 Mio.",
                        "InventoryPackage",
                        root);
                }

                files.Add(new HubExtensionPackageFile(
                    Path.GetRelativePath(root, entry),
                    info.Length,
                    info.LastWriteTimeUtc,
                    await ComputeSha256Async(entry, cancellationToken).ConfigureAwait(false)));
            }
        }

        if (files.Count == 0)
        {
            throw new HubExtensionException(
                "Le package d’extension est vide.",
                "InventoryPackage",
                root);
        }

        return files
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool InventoriesMatch(
        IReadOnlyList<HubExtensionPackageFile> expected,
        IReadOnlyList<HubExtensionPackageFile> current) =>
        expected.Count == current.Count && expected.Zip(current).All(pair =>
            string.Equals(
                pair.First.RelativePath,
                pair.Second.RelativePath,
                StringComparison.OrdinalIgnoreCase) &&
            pair.First.Length == pair.Second.Length &&
            pair.First.LastWriteTimeUtc == pair.Second.LastWriteTimeUtc &&
            string.Equals(pair.First.Sha256, pair.Second.Sha256, StringComparison.OrdinalIgnoreCase));

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
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void DeleteContainedDirectory(string path, string expectedParent)
    {
        try
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(path));
            if (Directory.Exists(path) && SamePath(parent!, expectedParent))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Un staging contenu et inutilisé ne doit pas masquer le résultat principal.
        }
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
        SamePath(Path.GetFullPath(path), Path.GetPathRoot(Path.GetFullPath(path))!);

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
