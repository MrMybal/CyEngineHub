using CyEngineHub.Core.Tasks;
using CyEngineHub.Storage.Analysis;
using CyEngineHub.Storage.Models;
using System.Windows.Input;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private ICommand? _analyzeSelectedEngineSizeCommand;
    public ICommand AnalyzeSelectedEngineSizeCommand => _analyzeSelectedEngineSizeCommand ??= new RelayCommand(() =>
    {
        var path = SelectedEngineDetails?.Path ?? SelectedLocalEngineDetails?.Path;
        if (path is not null) RunEngineSelectionAction([path], "size");
    }, () => !IsEngineSelectionBusy && (SelectedEngineDetails is not null || SelectedLocalEngineDetails is not null));
    private async Task AnalyzeEngineSizesAsync(HashSet<string> selected)
    {
        var roots = Engines.Where(item => selected.Contains(item.Path)).Select(item => (Path: item.Path, Name: item.DisplayName))
            .Concat(LocalEngineInstallations.Where(item => selected.Contains(item.Path) || selected.Contains(item.Installation.RootPath))
                .Select(item => (Path: item.Installation.RootPath, Name: item.Name)))
            .DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length == 0) throw new InvalidOperationException("Sélectionnez des moteurs installés pour analyser leur taille.");
        var entries = new List<StorageEntry>();
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition("Stockage · Moteurs", $"Analyser la taille de {roots.Length} moteur(s)",
            roots.Select(root => EngineTaskLink(root.Path, root.Name)).ToArray(), async (context, token) =>
            {
                await Task.Run(() =>
                {
                    var sizer = new BoundedDirectorySizer(1_000_000, TimeSpan.FromSeconds(30));
                    for (var index = 0; index < roots.Length; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        var root = roots[index];
                        context.Report((double)index / roots.Length, $"Mesure · {root.Name} ({index + 1}/{roots.Length})");
                        var result = sizer.Analyze(root.Path, token);
                        if (!result.Exists) continue;
                        entries.Add(new("engine-size:" + root.Path, StorageCategory.EngineInstallation, StorageEntryRole.Footprint,
                            "Installation moteur", root.Name, root.Path, root.Path, result.SizeBytes, result.FileCount, result.DirectoryCount,
                            result.LastModified, result.IsPartial ? StorageHealth.Partial : StorageHealth.Ready, false, result.Diagnostic));
                    }
                    context.Report(1, "Mesure des moteurs sélectionnés terminée");
                }, token).ConfigureAwait(false);
            }));
        var completed = await handle.Completion.ConfigureAwait(true);
        if (completed.Status != HubTaskStatus.Completed)
        {
            StatusMessage = completed.Status == HubTaskStatus.Canceled ? "Analyse des tailles annulée ; mesures précédentes conservées." : "Échec de l’analyse des tailles : " + completed.Error;
            return;
        }
        var old = _currentStorageSnapshot;
        var measuredRoots = roots.Select(root => root.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merged = new StorageSnapshot((old?.Entries ?? []).Where(entry =>
            !(entry.Category == StorageCategory.EngineInstallation && measuredRoots.Contains(entry.OwnerRoot))).Concat(entries).ToArray(),
            old?.Drives ?? [], old?.Diagnostics ?? [], DateTimeOffset.UtcNow);
        await _storageSnapshotCache.SaveAsync(merged).ConfigureAwait(true);
        ReplaceStorageSnapshot(merged);
        StatusMessage = $"Taille mesurée pour {entries.Count} moteur(s) · {entries.Count(entry => entry.Health == StorageHealth.Partial)} mesure(s) partielle(s).";
    }
}
