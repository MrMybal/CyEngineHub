using System.Windows.Input;
using Avalonia.Threading;
using CyEngineHub.Core.Ai;
using CyEngineHub.Core.Editor;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Fab.Installation;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private CancellationTokenSource? _editorStop;
    private Task? _editorServer;
    private bool _editorBridgeEnabled;
    private string _editorBridgeStatus = "Connexion Unreal désactivée";
    public bool EditorBridgeEnabled { get => _editorBridgeEnabled; private set => SetProperty(ref _editorBridgeEnabled, value); }
    public string EditorBridgeStatus { get => _editorBridgeStatus; private set => SetProperty(ref _editorBridgeStatus, value); }
    private bool _installEditorPluginConfirmed;
    public bool InstallEditorPluginConfirmed => _installEditorPluginConfirmed;
    public ICommand PrepareEditorPluginCommand => new RelayCommand(() => { _installEditorPluginConfirmed = true; OnPropertyChanged(nameof(InstallEditorPluginConfirmed)); });
    public ICommand CancelEditorPluginCommand => new RelayCommand(() => { _installEditorPluginConfirmed = false; OnPropertyChanged(nameof(InstallEditorPluginConfirmed)); });
    public ICommand InstallEditorPluginCommand => new AsyncRelayCommand(async () =>
    {
        var engine = SelectedEngineDetails ?? throw new InvalidOperationException("Sélectionnez un moteur Unreal.");
        if (!_installEditorPluginConfirmed) throw new InvalidOperationException("Confirmez l’installation.");
        _installEditorPluginConfirmed = false; OnPropertyChanged(nameof(InstallEditorPluginConfirmed));
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition("Unreal · Plugin éditeur", "Installer CyEngineHub dans " + engine.DisplayName,
            [EngineTaskLink(engine.Path, engine.DisplayName)], async (context, token) =>
            {
                context.ReportIndeterminate("Compilation pour ce moteur — Visual Studio C++ requis.");
                var progress = new InlineProgress<CyEngineHub.Core.Processes.ProcessOutputLine>(line => { AppendRawLog("INFO", "Editor plugin", line.Text); context.Report(null, line.Text); });
                await new CyEngineHub.Plugins.Installation.HubEditorPluginInstaller(_processRunner).InstallAsync(
                    Path.Combine(AppContext.BaseDirectory, "UnrealPlugin/CyEngineHub"), engine.Path,
                    Path.Combine(DataDirectory, "EditorPluginBuilds"), progress, token).ConfigureAwait(false);
                context.Report(1, "Plugin installé · activé par défaut au prochain démarrage de l’éditeur.");
            }));
        var completed = await handle.Completion.ConfigureAwait(true);
        if (completed.Status != HubTaskStatus.Completed) throw new InvalidOperationException(completed.Error ?? "Installation annulée.");
        if (!EditorBridgeEnabled) StartEditorBridge();
        await _settings.SetAsync("unreal.editor-bridge.enabled.v1", "True").ConfigureAwait(true);
        StatusMessage = "Plugin installé. Ouvrez Unreal > Fenêtre > CyEngineHub. Temporaires de compilation nettoyés.";
    }, onError: ShowError);
    public ICommand ToggleEditorBridgeCommand => new AsyncRelayCommand(async () =>
    {
        if (EditorBridgeEnabled) StopEditorBridge(); else StartEditorBridge();
        await _settings.SetAsync("unreal.editor-bridge.enabled.v1", EditorBridgeEnabled.ToString()).ConfigureAwait(true);
    }, onError: ShowError);

    private async Task LoadEditorBridgeAsync()
    {
        if (bool.TryParse(await _settings.GetAsync("unreal.editor-bridge.enabled.v1").ConfigureAwait(true), out var enabled) && enabled)
            StartEditorBridge();
    }
    private void StartEditorBridge(string? pipeName = null)
    {
        if (_editorServer is { IsCompleted: false }) throw new InvalidOperationException("La connexion précédente se ferme. Réessayez.");
        _editorStop?.Dispose();
        _editorStop = new CancellationTokenSource();
        var token = _editorStop.Token;
        EditorBridgeEnabled = true;
        EditorBridgeStatus = "Unreal · connexion locale active · projets enregistrés uniquement";
        _editorServer = Task.Run(async () =>
        {
            try { await HubEditorTransport.ServeAsync(new DesktopEditorBackend(this), token, pipeName).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) { Dispatcher.UIThread.Post(() => { EditorBridgeEnabled = false; EditorBridgeStatus = "Connexion Unreal arrêtée · consulter le journal"; }); }
        }, token);
    }
    public void StopEditorBridge()
    {
        EditorBridgeEnabled = false;
        var stop = _editorStop; _editorStop = null;
        stop?.Cancel();
        if (stop is not null) _ = DisposeMcpCancellationAsync(_editorServer, stop);
        EditorBridgeStatus = "Connexion Unreal désactivée · tâches déjà confirmées conservées";
    }

    private sealed class DesktopEditorBackend(MainWindowViewModel owner) : IHubEditorBackend
    {
        private readonly DesktopAiBackend _catalog = new(owner, editor: true);
        private readonly Dictionary<string, (string Fingerprint, object Result)> _requests = new(StringComparer.Ordinal);
        public async Task<HubEditorProject> ResolveProjectAsync(string projectFile, CancellationToken token) =>
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                if (!owner.EditorBridgeEnabled) throw new InvalidOperationException("Connexion éditeur désactivée dans CyEngineHub.");
                if (!Path.IsPathFullyQualified(projectFile) || !projectFile.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase) || !File.Exists(projectFile))
                    throw new ArgumentException("Projet Unreal invalide.");
                var project = owner.Projects.FirstOrDefault(p => SameWorkspaceKey(p.Project.FilePath, Path.GetFullPath(projectFile)))
                    ?? throw new InvalidOperationException("Importez d’abord ce projet dans CyEngineHub > Projets.");
                var engine = owner.ResolveEngine(project.Project.EngineAssociation)
                    ?? throw new InvalidOperationException("Associez ce projet à un moteur enregistré dans CyEngineHub.");
                return new HubEditorProject("project:" + project.Project.Id, project.Name, engine.Version.ToString());
            });
        public Task<IReadOnlyList<HubAiAsset>> GetAssetsAsync(CancellationToken token) => _catalog.GetAssetsAsync(token);
        public async Task<object> ReviewAsync(HubEditorProject project, string assetId, string source, CancellationToken token) =>
            await Dispatcher.UIThread.InvokeAsync<object>(() =>
            {
                token.ThrowIfCancellationRequested();
                if (owner.IsEpicBusy) throw new InvalidOperationException("Une installation est en cours dans le Hub.");
                var asset = owner._remoteFabCatalog.FirstOrDefault(a => a.AssetId == assetId && NormalizeAssetSource(a).Equals(source, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Consultez cette bibliothèque de connecteur dans le Hub.");
                owner.LibrarySource = IsQuixelAsset(asset) ? "Quixel" : "Fab";
                owner.ShowSection(PageSection.Epic);
                using var card = owner.CreateRemoteAssetCard(asset); owner.OpenRemoteFabProduct(card);
                foreach (var target in owner.FabInstallTargets) target.IsSelected = target.Id == project.Id;
                owner.IsFabInstallOptionsVisible = true;
                return new { status = "review_in_hub", note = "Fiche et options préparées dans le Hub. Aucune installation lancée." };
            });
        public async Task<object> GetTasksAsync(HubEditorProject project, CancellationToken token) =>
            await Dispatcher.UIThread.InvokeAsync<object>(() =>
            {
                token.ThrowIfCancellationRequested();
                var path = owner.Projects.FirstOrDefault(p => "project:" + p.Project.Id == project.Id)?.Project.RootPath;
                return owner._taskScheduler.GetSnapshot().Where(t => t.Links.Any(l => l.Kind == "project" && SameWorkspaceKey(l.Id, path ?? "")))
                    .Take(30).Select(t => new { t.Id, t.Title, status = t.Status.ToString(), t.Progress, t.Message }).ToArray();
            });
        public async Task<object> InstallAsync(HubEditorProject project, string assetId, string source, string variantId, string requestId, CancellationToken token) =>
            await Dispatcher.UIThread.InvokeAsync<object>(async () =>
            {
                token.ThrowIfCancellationRequested();
                if (!owner.EditorBridgeEnabled) throw new InvalidOperationException("Connexion éditeur désactivée.");
                var fingerprint = project.Id + "\n" + source + "\n" + assetId + "\n" + variantId;
                if (_requests.TryGetValue(requestId, out var previous))
                    return previous.Fingerprint == fingerprint ? previous.Result : throw new ArgumentException("requestId déjà utilisé pour une autre installation.");
                if (_requests.Count >= 1000) throw new InvalidOperationException("Limite de session atteinte. Reconnectez le pont depuis le Hub.");
                if (owner.IsEpicBusy || owner._aiAutomaticActionRunning) throw new InvalidOperationException("Une installation est déjà en cours. Réessayez après sa fin.");
                var asset = owner._remoteFabCatalog.FirstOrDefault(a => a.AssetId == assetId && NormalizeAssetSource(a).Equals(source, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Ce fournisseur nécessite une installation depuis le Hub.");
                if (!owner.IsMarketplaceAvailable(source) || IsFabWebAsset(asset)) throw new InvalidOperationException("Installez ce produit depuis le Hub / son fournisseur officiel.");
                using var card = owner.CreateRemoteAssetCard(asset);
                owner.OpenRemoteFabProduct(card);
                var originalTarget = owner.FabInstallTargets.FirstOrDefault(t => t.Id == project.Id && t.IsCompatible)
                    ?? throw new InvalidOperationException("Variante ou type de pack incompatible avec le projet ouvert.");
                // Live editor imports are additive: never overwrite a loaded package or plugin.
                var kind = originalTarget.ContentKind;
                if (kind is not (CyEngineHub.Fab.Packaging.LibraryPackageContentKind.ContentPack or CyEngineHub.Fab.Packaging.LibraryPackageContentKind.CodePlugin or CyEngineHub.Fab.Packaging.LibraryPackageContentKind.EnginePackage))
                    throw new InvalidOperationException("Ce type de pack nécessite le Hub, éditeur fermé.");
                var safeScript = kind == CyEngineHub.Fab.Packaging.LibraryPackageContentKind.ContentPack ? UnrealContentPackInstallationScript.ScriptId : UnrealProjectPluginInstallationScript.ScriptId;
                // Do not inherit or change saved advanced script preferences for a live import.
                var target = new FabInstallTargetOptionViewModel(originalTarget.Id, originalTarget.Name, originalTarget.Kind,
                    originalTarget.Path, originalTarget.EngineVersion, true, originalTarget.IgnoresEngineCompatibility, kind,
                    owner._fabInstallationScripts.GetCompatible(kind, FabInstallationTargetKind.Project), safeScript,
                    new Dictionary<string, string> { ["conflictPolicy"] = "fail", ["targetPlatform"] = "Win64" }, originalTarget.Project);
                owner.FabInstallTargets[owner.FabInstallTargets.IndexOf(originalTarget)] = target;
                foreach (var item in owner.FabInstallTargets) item.IsSelected = ReferenceEquals(item, target);
                owner.RebuildCompatibleFabInstallVersions();
                if (!owner.CompatibleFabInstallVersions.Any(v => v.Version.ArtifactId == variantId)) throw new InvalidOperationException("Variante incompatible.");
                foreach (var v in owner.CompatibleFabInstallVersions) v.IsSelected = v.Version.ArtifactId == variantId;
                Guid? taskId = null;
                var completion = owner.InstallSelectedFabVersionsIntoTargetsAsync(h => taskId = h.Id, editorSafe: true);
                if (completion.IsFaulted) await completion.ConfigureAwait(true);
                if (taskId is null) throw new InvalidOperationException("Aucune installation créée.");
                _ = ObserveAsync(completion);
                object result = new { taskId, status = "queued", note = "Import additif. Aucun fichier existant remplacé. Redémarrez Unreal pour les plugins." };
                _requests.Add(requestId, (fingerprint, result));
                return result;
            });
        private async Task ObserveAsync(Task completion)
        {
            try { await completion.ConfigureAwait(true); }
            catch (Exception e) { owner.AppendRawLog("WARN", "Unreal editor bridge", e.Message); }
        }
    }
}
