using System.Text.Json;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Acquisition;

public sealed partial class OfficialEpicGamesLauncherEngineBackend
{
    public async Task<EpicBinaryEngineDownloadResult> UpdateAsync(
        EpicBinaryEngineRelease release, string existingDirectory,
        IProgress<EpicBinaryEngineDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        var target = ValidateExistingDestination(existingDirectory);
        if (Directory.Exists(Path.Combine(target, ".git")) || File.Exists(Path.Combine(target, ".git")))
            throw new InvalidOperationException("Un moteur Git se met à jour depuis son dépôt, pas depuis Epic.");
        var stage = target + ".cyenginehub-update";
        EnsureSafeStaging(target, stage);
        RejectUpdateLinks(target);
        RejectUpdateTreeLinks(stage);
        // One writer per engine, including another running Hub instance.
        Directory.CreateDirectory(stage);
        using var updateLock = new FileStream(Path.Combine(stage, "update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        RecoverEngineUpdate(target, stage);
        using var localJson = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(target, "Engine", "Build", "Build.version"), cancellationToken).ConfigureAwait(false));
        var local = localJson.RootElement;
        var installed = new Version(local.GetProperty("MajorVersion").GetInt32(), local.GetProperty("MinorVersion").GetInt32(), local.GetProperty("PatchVersion").GetInt32());
        var session = await GetSessionAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new(null, "Lecture du manifest et vérification des fichiers locaux…", 0, null));
        var distribution = await GetDistributionAsync(release, session, cancellationToken).ConfigureAwait(false);
        var bytes = await ReadBoundedBytesAsync(distribution.ManifestUri, MaximumManifestBytes, cancellationToken).ConfigureAwait(false);
        var manifest = EpicManifestParser.Parse(bytes, distribution.BaseUri);
        var build = EpicEngineBuild.Parse(manifest.BuildVersion);
        if (build.Version is null || build.Version.Major != installed.Major || build.Version.Minor != installed.Minor || build.Version < installed)
            throw new InvalidOperationException("La révision Epic ne correspond pas à la branche installée ou serait un downgrade.");
        if (!manifest.Files.Any(file => file.RelativePath.Equals("Engine/Build/Build.version", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Manifest moteur incomplet : Build.version absent.");
        var installedManifest = await ReadInstalledPatchManifestAsync(target, cancellationToken).ConfigureAwait(false);
        var files = SelectPatchFiles(target, manifest, installedManifest);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var verified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = new List<EpicManifestFile>();
        var lastProgress = System.Diagnostics.Stopwatch.GetTimestamp();
        var inspected = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.RelativePath.Equals(".cyenginehub-install.manifest", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Le manifest Epic utilise un chemin réservé au suivi de l’installation.");
            var destination = GetSafeDestination(target, file.RelativePath);
            RejectUpdateLinks(destination);
            if (!paths.Add(destination)) throw new InvalidOperationException("Chemins moteur dupliqués dans le manifest.");
            if (!await MatchesPatchFileAsync(destination, file, cancellationToken).ConfigureAwait(false)) changed.Add(file);
            else verified.Add(file.RelativePath);
            inspected++;
            if (System.Diagnostics.Stopwatch.GetElapsedTime(lastProgress).TotalMilliseconds >= 250)
            {
                progress?.Report(new(.10 * inspected / Math.Max(1, files.Length), $"Vérification locale · {inspected:N0}/{files.Length:N0} fichier(s) · {changed.Count:N0} modifié(s)", 0, null));
                lastProgress = System.Diagnostics.Stopwatch.GetTimestamp();
            }
        }
        var cache = Path.Combine(stage, "chunks");
        var prepared = Path.Combine(stage, "files");
        RejectUpdateLinks(cache); RejectUpdateLinks(prepared);
        Directory.CreateDirectory(cache); Directory.CreateDirectory(prepared);
        var remaining = new List<EpicManifestFile>();
        long remainingBytes = 0;
        foreach (var file in changed)
        {
            var path = GetSafeDestination(prepared, file.RelativePath); RejectUpdateLinks(path);
            if (await MatchesPatchFileAsync(path, file, cancellationToken).ConfigureAwait(false)) continue;
            if (File.Exists(path)) File.Delete(path); // Invalid prepared output only, never an installed engine file.
            remaining.Add(file);
            // Account for allocation rounding and file metadata, not only logical payload bytes.
            remainingBytes = checked(remainingBytes + ((file.Size + 4095) / 4096 * 4096) + 1024);
        }
        var localContent = await EpicPatchLocalContent.CreateAsync(target, manifest, installedManifest, remaining, verified, (index, count) =>
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(lastProgress).TotalMilliseconds < 250) return;
            progress?.Report(new(.10 + .08 * index / Math.Max(1, count), $"Analyse du patch · {index:N0}/{count:N0} bloc(s) · réutilisation des fichiers locaux vérifiés", 0, null));
            lastProgress = System.Diagnostics.Stopwatch.GetTimestamp();
        }, cancellationToken).ConfigureAwait(false);
        var network = remaining.SelectMany(file => file.Parts).Select(part => part.ChunkGuid).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id => !localContent.Contains(id)).Select(id => manifest.Chunks[id]).ToArray();
        var cacheLimit = Math.Max(PatchChunkCacheBytes, remaining.SelectMany(file => file.Parts).Select(part => manifest.Chunks[part.ChunkGuid].DownloadSize + 4).DefaultIfEmpty(0).Max());
        var peakCache = Math.Min(cacheLimit, network.Sum(chunk => checked(chunk.DownloadSize + 4)));
        TrimPatchChunkCache(cache, peakCache);
        var existingCache = PatchCacheFiles(cache).Sum(file => file.Length);
        var networkBytes = network.Where(chunk => !File.Exists(GetSafeDestination(cache, chunk.ChunkId + ".chunk"))).Sum(chunk => chunk.DownloadSize);
        var sizeDelta = changed.Sum(file => file.Size - (File.Exists(GetSafeDestination(target, file.RelativePath)) ? new FileInfo(GetSafeDestination(target, file.RelativePath)).Length : 0));
        var journalBytes = changed.Count == 0 ? 0 : changed.Append(new EpicManifestFile(".cyenginehub-install.manifest", [], [], 0, []))
            .Sum(file => (long)System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new EngineUpdateEntry(file.RelativePath, false))) + 1) + 2;
        var plan = new EpicEnginePatchPlan(changed.Count, networkBytes, localContent.ReusableBytes,
            EpicEnginePatchPlan.RequiredSpace(checked(remainingBytes + 2 * journalBytes + (changed.Count > 0 ? bytes.Length : 0)), peakCache, existingCache), sizeDelta, cacheLimit);
        progress?.Report(new(.18, plan.DisplayText, 0, networkBytes) { PatchPlan = plan });
        var available = PatchFreeSpace(target);
        if (available < plan.AdditionalSpaceBytes)
            throw new IOException($"Espace insuffisant pour préparer le patch : {EpicEnginePatchPlan.Size(plan.AdditionalSpaceBytes)} supplémentaires requis, {EpicEnginePatchPlan.Size(available)} disponibles. {plan.Summary}");
        var content = new PatchChunkSource(this, manifest, localContent, cache, remaining, plan, progress);
        using (var decoded = new DecodedChunkCache(128L << 20, content.LoadAsync))
        {
            for (var index = 0; index < changed.Count; index++)
            {
                var file = changed[index];
                var destination = GetSafeDestination(prepared, file.RelativePath);
                RejectUpdateLinks(destination);
                content.Fraction = .18 + .70 * index / Math.Max(1, changed.Count);
                var usedLocal = file.Parts.Any(part => localContent.Contains(part.ChunkGuid));
                try { await ReconstructFileAsync(file, destination, decoded, () => InvalidateFileChunks(cache, file), cancellationToken).ConfigureAwait(false); }
                catch (InvalidOperationException) when (usedLocal)
                {
                    // If a local source changed since verification, retry from Epic.
                    // The engine is still untouched and no unverified file is committed.
                    foreach (var part in file.Parts) { localContent.Disable(part.ChunkGuid); decoded.Remove(part.ChunkGuid); }
                    await ReconstructFileAsync(file, destination, decoded, () => InvalidateFileChunks(cache, file), cancellationToken).ConfigureAwait(false);
                }
                progress?.Report(new(.18 + .70 * (index + 1d) / changed.Count, $"Préparation du patch · {index + 1}/{changed.Count} fichier(s) modifié(s)", content.Downloaded, Math.Max(networkBytes, content.Downloaded)) { PatchPlan = plan });
            }
        }
        var snapshot = Path.Combine(stage, "new-manifest"); RejectUpdateLinks(snapshot);
        if (changed.Count > 0)
        {
            await using var output = new FileStream(snapshot, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
            await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        // Check all overwritten files before changing any of them (editor, UBT, antivirus locks).
        foreach (var file in changed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = GetSafeDestination(target, file.RelativePath);
            RejectUpdateLinks(path);
            if (File.Exists(path)) { using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        }
        var journal = new List<EngineUpdateEntry>();
        try
        {
            if (changed.Count > 0)
                CommitPatchFile(target, stage, snapshot, ".cyenginehub-install.manifest", journal);
            var installedFiles = 0;
            // Build.version is committed last so discovery never announces the new version early.
            foreach (var file in changed.OrderBy(file => file.RelativePath.Equals("Engine/Build/Build.version", StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = GetSafeDestination(target, file.RelativePath);
                CommitPatchFile(target, stage, GetSafeDestination(prepared, file.RelativePath), file.RelativePath, journal);
                if (!EpicManifestParser.MatchesSha1(destination, file.Sha1)) throw new IOException("Copie moteur non conforme : " + file.RelativePath);
                progress?.Report(new(.90 + .09 * ++installedFiles / changed.Count,
                    $"Installation · {installedFiles}/{changed.Count} fichier(s)", content.Downloaded, Math.Max(networkBytes, content.Downloaded)) { PatchPlan = plan });
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(stage, "committed"), manifest.BuildVersion);
        }
        catch
        {
            RecoverEngineUpdate(target, stage);
            throw;
        }
        updateLock.Dispose();
        TryDeleteCompletedStaging(stage, target);
        progress?.Report(new(1, $"Moteur mis à jour · {changed.Count} fichier(s) remplacé(s), fichiers personnels conservés.", content.Downloaded, content.Downloaded) { PatchPlan = plan });
        return new(release, target, manifest.BuildVersion, changed.Count, content.Downloaded);
    }

    private static void CommitPatchFile(string target, string stage, string prepared, string relative, List<EngineUpdateEntry> journal)
    {
        var destination = GetSafeDestination(target, relative);
        var backup = GetSafeDestination(Path.Combine(stage, "backup"), relative);
        RejectUpdateLinks(destination); RejectUpdateLinks(backup); RejectUpdateLinks(prepared);
        journal.Add(new(relative, File.Exists(destination)));
        WriteUpdateJournal(stage, journal);
        if (File.Exists(destination)) { Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Move(destination, backup); }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(prepared, destination, overwrite: false);
    }

    private static void WriteUpdateJournal(string stage, List<EngineUpdateEntry> journal)
    {
        var path = Path.Combine(stage, "journal.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(journal));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    private static void RecoverEngineUpdate(string target, string stage)
    {
        RejectUpdateLinks(stage);
        var path = Path.Combine(stage, "journal.json");
        if (File.Exists(Path.Combine(stage, "committed")))
        {
            // A crash after the commit must not undo a successfully installed revision.
            var backupDirectory = Path.Combine(stage, "backup");
            RejectUpdateTreeLinks(backupDirectory);
            if (Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, recursive: true);
            if (File.Exists(path)) File.Delete(path);
            File.Delete(Path.Combine(stage, "committed"));
            return;
        }
        if (!File.Exists(path)) return;
        var entries = JsonSerializer.Deserialize<EngineUpdateEntry[]>(File.ReadAllText(path)) ?? throw new IOException("Journal moteur illisible.");
        foreach (var entry in entries.Reverse())
        {
            var destination = GetSafeDestination(target, entry.Path);
            var backup = GetSafeDestination(Path.Combine(stage, "backup"), entry.Path);
            RejectUpdateLinks(destination); RejectUpdateLinks(backup);
            if (entry.Existed && File.Exists(backup)) File.Move(backup, destination, overwrite: true);
            else if (!entry.Existed && File.Exists(destination)) File.Delete(destination);
        }
        File.Delete(path);
    }

    private static void RejectUpdateLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Un chemin de mise à jour traverse un lien : " + current);
    }

    private static void RejectUpdateTreeLinks(string root)
    {
        RejectUpdateLinks(root);
        if (!Directory.Exists(root)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            RejectUpdateLinks(entry);
            if (Directory.Exists(entry)) RejectUpdateTreeLinks(entry);
        }
    }

    private sealed record EngineUpdateEntry(string Path, bool Existed);
}
