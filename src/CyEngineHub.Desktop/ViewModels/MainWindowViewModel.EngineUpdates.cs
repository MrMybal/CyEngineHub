using System.Windows.Input;
using CyEngineHub.Epic.Acquisition;
using CyEngineHub.Unreal.Detection;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private AsyncRelayCommand? _checkOfficialEngineUpdatesCommand;
    private bool _isCheckingOfficialEngineUpdates;
    private string _officialEngineUpdateSummary = "Les révisions officielles se vérifient avec la connexion Epic/Fab.";
    private readonly Dictionary<string, (EpicEngineBuild? Build, DateTimeOffset Checked)> _officialBuildCache = new(StringComparer.Ordinal);
    private EngineCardViewModel? _pendingEngineUpdate;
    private readonly HashSet<string> _updatingEnginePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, EpicEnginePatchPlan> _enginePatchPlans = new(StringComparer.OrdinalIgnoreCase);
    internal EpicEnginePatchPlan? GetEnginePatchPlan(string path) => _enginePatchPlans.GetValueOrDefault(path);
    public bool IsEngineUpdateConfirmationVisible => _pendingEngineUpdate is not null;
    public string EngineUpdateConfirmationText => _pendingEngineUpdate is { } engine
        ? $"Mettre à jour {engine.DisplayName} ({engine.VersionLabel}) dans {engine.Path} ? {engine.OfficialUpdateLabel}. Fermez l’éditeur et les compilations utilisant ce moteur. Seuls les fichiers modifiés sont préparés puis remplacés ; les composants installés et les fichiers personnels sont conservés."
        : string.Empty;
    public bool CanUpdateSelectedOfficialEngine => SelectedEngineDetails is { IsOfficialEpic: true, OfficialUpdateAvailable: true, IsInstallationAvailable: true } engine && !_updatingEnginePaths.Contains(engine.Path) && IsDirectFabConnected && !IsEpicBusy && !IsEngineUninstallActive;
    public ICommand RequestOfficialEngineUpdateCommand => new RelayCommand(() => RequestOfficialEngineUpdate(SelectedEngineDetails), () => CanUpdateSelectedOfficialEngine);
    public ICommand CancelOfficialEngineUpdateCommand => new RelayCommand(() => { _pendingEngineUpdate = null; OnPropertyChanged(nameof(IsEngineUpdateConfirmationVisible)); });
    public ICommand ConfirmOfficialEngineUpdateCommand => new AsyncRelayCommand(ConfirmOfficialEngineUpdateAsync, () => _pendingEngineUpdate is not null, ShowError);

    public void RequestOfficialEngineUpdate(EngineCardViewModel? engine)
    {
        if (engine is not { IsOfficialEpic: true, OfficialUpdateAvailable: true, IsInstallationAvailable: true } || _updatingEnginePaths.Contains(engine.Path) || IsEpicBusy || IsEngineUninstallActive) return;
        OpenUnrealEngineDetails(engine);
        _pendingEngineUpdate = engine;
        OnPropertyChanged(nameof(IsEngineUpdateConfirmationVisible));
        OnPropertyChanged(nameof(EngineUpdateConfirmationText));
    }

    private async Task ConfirmOfficialEngineUpdateAsync()
    {
        var engine = _pendingEngineUpdate ?? throw new InvalidOperationException("Aucun moteur à mettre à jour.");
        if (IsEpicBusy || IsEngineUninstallActive || !IsDirectFabConnected) throw new InvalidOperationException("Connectez Epic et attendez la fin des autres modifications du moteur.");
        if (!_updatingEnginePaths.Add(engine.Path)) return;
        _enginePatchPlans.TryRemove(engine.Path, out _);
        _pendingEngineUpdate = null;
        OnPropertyChanged(nameof(IsEngineUpdateConfirmationVisible));
        OnPropertyChanged(nameof(CanUpdateSelectedOfficialEngine));
        try
        {
            var branch = $"{engine.Engine.Version.Major}.{engine.Engine.Version.Minor}";
            var releases = await _epicBinaryEngineBackend.GetAvailableReleasesAsync().ConfigureAwait(true);
            var release = releases.FirstOrDefault(item => item.Version == branch)
                ?? throw new InvalidOperationException("La version officielle n’est plus disponible.");
            var handle = _taskScheduler.Enqueue(new CyEngineHub.Core.Tasks.HubTaskDefinition(
                "Moteurs · Epic", $"Mettre à jour {engine.DisplayName}", [EngineTaskLink(engine.Path, engine.DisplayName)],
                async (context, cancellationToken) =>
                {
                    var transfer = new CyEngineHub.Core.Marketplaces.DownloadProgressText();
                    string? patchSummary = null;
                    var progress = new InlineProgress<EpicBinaryEngineDownloadProgress>(value =>
                    {
                        if (value.PatchPlan is { } plan) { patchSummary = plan.DisplayText; _enginePatchPlans[engine.Path] = plan; }
                        var message = transfer.Format(value.Message, value.DownloadedBytes, value.TotalBytes, value.CachedBytes);
                        context.Report(value.Fraction, patchSummary is null || value.Message == patchSummary ? message : patchSummary + "\n" + message);
                    });
                    await _epicBinaryEngineBackend.UpdateAsync(release, engine.Path, progress, cancellationToken).ConfigureAwait(false);
                }) { IsDownload = true });
            StatusMessage = $"Mise à jour de {engine.DisplayName} ajoutée aux téléchargements.";
            var completed = await handle.Completion.ConfigureAwait(true);
            if (completed.Status != CyEngineHub.Core.Tasks.HubTaskStatus.Completed)
            {
                StatusMessage = completed.Status == CyEngineHub.Core.Tasks.HubTaskStatus.Canceled
                    ? "Mise à jour annulée ; installation restaurée si le remplacement avait commencé. Cache vérifié conservé."
                    : $"Échec de la mise à jour : {completed.Error}";
                return;
            }
            _officialBuildCache.Remove(branch);
            await RefreshCatalogAsync().ConfigureAwait(true);
            var refreshed = Engines.FirstOrDefault(item => SameWorkspaceKey(item.Path, engine.Path));
            if (refreshed is not null) OpenUnrealEngineDetails(refreshed);
            StatusMessage = "Moteur mis à jour et vérifié.";
        }
        finally
        {
            _updatingEnginePaths.Remove(engine.Path);
            OnPropertyChanged(nameof(CanUpdateSelectedOfficialEngine));
        }
    }
    public bool IsCheckingOfficialEngineUpdates => _isCheckingOfficialEngineUpdates;
    public string OfficialEngineUpdateSummary => _officialEngineUpdateSummary;
    public ICommand CheckOfficialEngineUpdatesCommand => _checkOfficialEngineUpdatesCommand ??=
        new AsyncRelayCommand(() => CheckOfficialEngineUpdatesAsync(true), () => !_isCheckingOfficialEngineUpdates, ShowError);

    private void QueueOfficialEngineUpdateCheck()
    {
        if (IsDirectFabConnected && Engines.Any(item => item.IsOfficialEpic)) _ = CheckOfficialEngineUpdatesAsync(false);
    }
    private async Task CheckOfficialEngineUpdatesAsync(bool force, HashSet<string>? selectedPaths = null)
    {
        if (_isCheckingOfficialEngineUpdates) return;
        var official = Engines.Where(item => item.IsOfficialEpic && (selectedPaths is null || selectedPaths.Contains(item.Path))).ToArray();
        if (!IsDirectFabConnected)
        {
            foreach (var engine in official) engine.ApplyOfficialUpdate("Connexion Epic requise", false);
            SetProperty(ref _officialEngineUpdateSummary, "Connectez Epic/Fab pour vérifier les révisions officielles.", nameof(OfficialEngineUpdateSummary));
            return;
        }
        SetProperty(ref _isCheckingOfficialEngineUpdates, true, nameof(IsCheckingOfficialEngineUpdates));
        _checkOfficialEngineUpdatesCommand?.NotifyCanExecuteChanged();
        SetProperty(ref _officialEngineUpdateSummary, "Vérification des révisions officielles…", nameof(OfficialEngineUpdateSummary));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            var present = await Task.Run(() => official.Where(item => item.IsInstallationAvailable && EngineCatalogValidation.IsPresent(item.Engine)).ToArray(), timeout.Token).ConfigureAwait(true);
            foreach (var engine in official.Except(present)) engine.ApplyOfficialUpdate("Installation absente ou indisponible", false);
            official = present;
            IReadOnlyList<EpicBinaryEngineRelease>? releases = null;
            foreach (var branch in official.Select(item => $"{item.Engine.Version.Major}.{item.Engine.Version.Minor}").Distinct(StringComparer.Ordinal))
            {
                EpicEngineBuild? build = null;
                try
                {
                    if (!force && _officialBuildCache.TryGetValue(branch, out var cached) && DateTimeOffset.UtcNow - cached.Checked < TimeSpan.FromMinutes(cached.Build is null ? 2 : 30)) build = cached.Build;
                    else
                    {
                        releases ??= await _epicBinaryEngineBackend.GetAvailableReleasesAsync(timeout.Token).ConfigureAwait(true);
                        var release = releases.FirstOrDefault(item => Version.TryParse(item.Version, out var version) && $"{version.Major}.{version.Minor}" == branch);
                        if (release is not null) build = await _epicBinaryEngineBackend.GetLatestBuildAsync(release, timeout.Token).ConfigureAwait(true);
                        _officialBuildCache[branch] = (build, DateTimeOffset.UtcNow);
                    }
                    foreach (var engine in official.Where(item => $"{item.Engine.Version.Major}.{item.Engine.Version.Minor}" == branch))
                    {
                        var version = engine.Engine.Version;
                        var newer = build?.IsNewerThan(new Version(version.Major, version.Minor, version.Patch), engine.Engine.Changelist);
                        var label = newer switch { true => $"Mise à jour : {build!.Version}", false => "À jour", _ => "Révision non vérifiable" };
                        if (newer == true && build!.Version?.Build == version.Patch && build.Changelist is { } cl) label += $" · CL {cl}";
                        engine.ApplyOfficialUpdate(label, newer == true);
                    }
                }
                catch (Exception)
                {
                    foreach (var engine in official.Where(item => $"{item.Engine.Version.Major}.{item.Engine.Version.Minor}" == branch))
                        engine.ApplyOfficialUpdate("Vérification indisponible", false);
                }
            }
            var count = Engines.Count(item => item.IsOfficialEpic && item.OfficialUpdateAvailable);
            var unknown = Engines.Count(item => item.IsOfficialEpic && item.OfficialUpdateLabel != "À jour" && !item.OfficialUpdateAvailable);
            SetProperty(ref _officialEngineUpdateSummary, $"Révisions officielles : {count} mise(s) à jour disponible(s) · {unknown} non vérifiable(s). Aucun téléchargement lancé.", nameof(OfficialEngineUpdateSummary));
        }
        finally
        {
            SetProperty(ref _isCheckingOfficialEngineUpdates, false, nameof(IsCheckingOfficialEngineUpdates));
            _checkOfficialEngineUpdatesCommand?.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanUpdateSelectedOfficialEngine));
        }
    }
}
