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
        var prepared = Path.Combine(stage, "deltas");
        RejectUpdateLinks(cache); RejectUpdateLinks(prepared);
        Directory.CreateDirectory(cache); Directory.CreateDirectory(prepared);
        var remaining = new List<EpicManifestFile>();
        var patches = new Dictionary<string, EpicFileDelta.Info>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in changed)
        {
            var path = DeltaPath(prepared, file.RelativePath); RejectUpdateLinks(path);
            if (TryReuseDelta(path, GetSafeDestination(target, file.RelativePath), file, out var ready))
            { patches[file.RelativePath] = ready!; continue; }
            if (File.Exists(path)) File.Delete(path); // Owned invalid patch only, never an engine file.
            remaining.Add(file);
        }
        var alreadyPrepared = patches.Values.Sum(patch => patch.AllocatedBytes);
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
        var growth = changed.Sum(file => Math.Max(0, file.Size - (File.Exists(GetSafeDestination(target, file.RelativePath)) ? new FileInfo(GetSafeDestination(target, file.RelativePath)).Length : 0)));
        var journalBytes = changed.Count == 0 ? 0 : changed.Sum(file => (long)System.Text.Encoding.UTF8.GetByteCount(
            JsonSerializer.Serialize(new EngineUpdateEntry(file.RelativePath, true, Path.GetRelativePath(stage, DeltaPath(prepared, file.RelativePath))))) + 1) + 1024;
        var metadataBytes = changed.Count == 0 ? 0 : checked(2 * journalBytes + bytes.Length);
        var plan = new EpicEnginePatchPlan(changed.Count, networkBytes, localContent.ReusableBytes,
            EpicEnginePatchPlan.RequiredSpace(checked(growth + metadataBytes), peakCache, existingCache), sizeDelta, cacheLimit)
            { IsDeltaPreparationPending = remaining.Count > 0, PreparedDeltaBytes = alreadyPrepared, ChangedDataBytes = patches.Values.Sum(patch => patch.NewBytes) };
        progress?.Report(new(.18, plan.DisplayText, 0, networkBytes) { PatchPlan = plan });
        var available = PatchFreeSpace(target);
        if (available < plan.AdditionalSpaceBytes)
            throw new IOException($"Espace insuffisant pour préparer le patch : {EpicEnginePatchPlan.Size(plan.AdditionalSpaceBytes)} supplémentaires requis, {EpicEnginePatchPlan.Size(available)} disponibles. {plan.Summary}");
        var content = new PatchChunkSource(this, manifest, localContent, cache, remaining, plan, progress);
        using (var decoded = new DecodedChunkCache(128L << 20, content.LoadAsync))
        {
            for (var index = 0; index < remaining.Count; index++)
            {
                var file = remaining[index];
                var destination = DeltaPath(prepared, file.RelativePath);
                RejectUpdateLinks(destination);
                content.Fraction = .18 + .70 * index / Math.Max(1, remaining.Count);
                var usedLocal = file.Parts.Any(part => localContent.Contains(part.ChunkGuid));
                try { patches[file.RelativePath] = await Prepare().ConfigureAwait(false); }
                catch (InvalidOperationException) when (usedLocal)
                {
                    // If a local source changed since verification, retry from Epic.
                    // The engine is still untouched and no unverified file is committed.
                    foreach (var part in file.Parts) { localContent.Disable(part.ChunkGuid); decoded.Remove(part.ChunkGuid); }
                    InvalidateFileChunks(cache, file);
                    patches[file.RelativePath] = await Prepare().ConfigureAwait(false);
                }
                plan = plan with { PreparedDeltaBytes = patches.Values.Sum(patch => patch.DiskBytes), ChangedDataBytes = patches.Values.Sum(patch => patch.NewBytes) };
                progress?.Report(new(.18 + .70 * (index + 1d) / remaining.Count, $"Préparation du patch · {index + 1}/{remaining.Count} delta(s), sans copie complète des fichiers moteur", content.Downloaded, Math.Max(networkBytes, content.Downloaded)) { PatchPlan = plan });

                Task<EpicFileDelta.Info> Prepare() => EpicFileDelta.CreateAsync(GetSafeDestination(target, file.RelativePath), file, destination, decoded, count =>
                {
                    var reserve = EpicEnginePatchPlan.RequiredSpace(checked(growth + metadataBytes + count), peakCache, 0);
                    if (PatchFreeSpace(target) < reserve) throw new IOException($"Espace insuffisant pour les prochaines plages du patch : {EpicEnginePatchPlan.Size(reserve)} requis à cette étape. Aucun fichier moteur n’a été remplacé.");
                }, cancellationToken);
            }
        }
        plan = plan with { IsDeltaPreparationPending = false, AdditionalSpaceBytes = EpicEnginePatchPlan.RequiredSpace(
            checked(growth + metadataBytes + patches.Values.Sum(patch => patch.AllocatedBytes) - alreadyPrepared), peakCache, existingCache) };
        progress?.Report(new(.89, plan.DisplayText, content.Downloaded, Math.Max(networkBytes, content.Downloaded)) { PatchPlan = plan });
        var snapshot = Path.Combine(stage, "new-manifest"); RejectUpdateLinks(snapshot);
        if (changed.Count > 0)
        {
            await using var output = new FileStream(snapshot, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
            await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }
        var journal = new List<EngineUpdateEntry>();
        var locked = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
        var patchLocks = new List<FileStream>();
        try
        {
            // Keep existing binaries exclusive throughout the commit, not just during a probe.
            foreach (var file in changed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var patch = patches[file.RelativePath];
                var path = GetSafeDestination(target, file.RelativePath); RejectUpdateLinks(path);
                patchLocks.Add(new FileStream(patch.PatchPath, FileMode.Open, FileAccess.Read, FileShare.Read));
                if (EpicFileDelta.Read(patch.PatchPath).Digest != patch.Digest) throw new IOException("Le delta préparé a changé.");
                FileStream? handle = null;
                if (File.Exists(path))
                {
                    handle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    locked.Add(file.RelativePath, handle); EpicPatchFileSafety.RequireSingleLink(handle);
                }
                if (!EpicFileDelta.MatchesOriginal(patch, handle)) throw new IOException("Un fichier moteur a changé depuis la préparation ; patch interrompu avant remplacement.");
            }
            if (changed.Count > 0)
                CommitPatchFile(target, stage, snapshot, ".cyenginehub-install.manifest", journal);
            var installedFiles = 0;
            // Build.version is committed last so discovery never announces the new version early.
            foreach (var file in changed.OrderBy(file => file.RelativePath.Equals("Engine/Build/Build.version", StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = GetSafeDestination(target, file.RelativePath);
                var patch = patches[file.RelativePath];
                void RecordDelta() { journal.Add(new(file.RelativePath, patch.Header.Existed, Path.GetRelativePath(stage, patch.PatchPath))); WriteUpdateJournal(stage, journal); }
                if (!locked.TryGetValue(file.RelativePath, out var output))
                {
                    // Journal the creation before it can leave a partial new engine file.
                    RecordDelta(); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                    locked.Add(file.RelativePath, output); EpicPatchFileSafety.RequireSingleLink(output);
                    EpicFileDelta.Apply(patch, output, cancellationToken);
                }
                else EpicFileDelta.Apply(patch, output, cancellationToken, RecordDelta);
                progress?.Report(new(.90 + .09 * ++installedFiles / changed.Count,
                    $"Installation · {installedFiles}/{changed.Count} fichier(s)", content.Downloaded, Math.Max(networkBytes, content.Downloaded)) { PatchPlan = plan });
            }
            cancellationToken.ThrowIfCancellationRequested();
            var committed = Path.Combine(stage, "committed");
            RejectUpdateLinks(committed); RejectUpdateLinks(committed + ".tmp");
            using (var marker = new FileStream(committed + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            { marker.Write(System.Text.Encoding.UTF8.GetBytes(manifest.BuildVersion)); marker.Flush(flushToDisk: true); }
            EpicPatchFileSafety.PublishJournal(committed + ".tmp", committed);
        }
        catch
        {
            ReleaseLocks();
            RecoverEngineUpdate(target, stage);
            throw;
        }
        finally { ReleaseLocks(); }
        updateLock.Dispose();
        TryDeleteCompletedStaging(stage, target);
        progress?.Report(new(1, $"Moteur mis à jour · {changed.Count} fichier(s) remplacé(s), fichiers personnels conservés.", content.Downloaded, content.Downloaded) { PatchPlan = plan });
        return new(release, target, manifest.BuildVersion, changed.Count, content.Downloaded);

        void ReleaseLocks() { foreach (var handle in locked.Values) handle.Dispose(); locked.Clear(); foreach (var handle in patchLocks) handle.Dispose(); patchLocks.Clear(); }
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
        RejectUpdateLinks(path); RejectUpdateLinks(path + ".tmp");
        using (var output = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        { output.Write(JsonSerializer.SerializeToUtf8Bytes(journal)); output.Flush(flushToDisk: true); }
        EpicPatchFileSafety.PublishJournal(path + ".tmp", path);
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
            if (entry.Delta is not null)
            {
                var delta = GetSafeDestination(stage, entry.Delta);
                EpicFileDelta.Restore(delta, destination, entry.Path, entry.Existed);
                continue;
            }
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

    private static string DeltaPath(string directory, string relative) => GetSafeDestination(directory,
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(relative.ToUpperInvariant()))) + ".delta");

    private static bool TryReuseDelta(string path, string original, EpicManifestFile file, out EpicFileDelta.Info? info)
    {
        info = null;
        if (!File.Exists(path)) return false;
        try
        {
            var candidate = EpicFileDelta.Read(path);
            if (candidate.Header.Path != file.RelativePath || candidate.Header.TargetLength != file.Size ||
                candidate.Header.TargetSha1 != Convert.ToHexString(file.Sha1)) return false;
            using var input = File.Exists(original) ? new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
            if (!EpicFileDelta.MatchesOriginal(candidate, input)) return false;
            info = candidate; return true;
        }
        catch (Exception error) when (error is IOException or JsonException or ArgumentException or OverflowException) { return false; }
    }

    private sealed record EngineUpdateEntry(string Path, bool Existed, string? Delta = null);
}
