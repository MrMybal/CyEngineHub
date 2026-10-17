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
        }
    }
}
