using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using Avalonia.Threading;
using CyEngineHub.Core.Ai;

namespace CyEngineHub.Desktop.ViewModels;

public sealed class AiProposalViewModel(HubAiProposal proposal, string assetName) : ObservableObject
{
    private string _status = "En attente";
    public HubAiProposal Proposal { get; } = proposal;
    public List<Guid> TaskIds { get; } = [];
    public string Title => $"{Proposal.Action} · {assetName} · {Proposal.Source}";
    public string Details => $"{Proposal.Reason}\nVariante : {Proposal.VariantId}\nCible : {Proposal.TargetId}\nCatégories : {string.Join(", ", Proposal.Categories)}";
    public string Status { get => _status; set => SetProperty(ref _status, value); }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "App shutdown calls StopMcp, which cancels and disposes the source after the server exits.")]
public sealed partial class MainWindowViewModel
{
    private static readonly JsonSerializerOptions McpConfigurationJson = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions McpPreferencesJson = new() { PropertyNameCaseInsensitive = true };
    private readonly string _mcpExecutable = Path.Combine(AppContext.BaseDirectory, "Mcp", OperatingSystem.IsWindows() ? "CyEngineHub.Mcp.exe" : "CyEngineHub.Mcp");
    private readonly string? _mcpPipe = ReadMcpPipeOverride();
    private CancellationTokenSource? _mcpStop;
    private Task? _mcpServer;
    private SdkMcpHttpServer? _mcpHttp;
    private bool _changingMcp;
    private bool _mcpEnabled;
    private SdkMcpRegistry? _sdkMcpRegistry;
    internal SdkMcpRegistry SdkRegistry => _sdkMcpRegistry ??= CreateSdkRegistry();

    private SdkMcpRegistry CreateSdkRegistry()
    {
        var operations = new HubMcpSession(new DesktopAiBackend(this));
        return new(HubMcpSession.Tools(), async (name, args, token) =>
        {
            var result = await operations.CallAsync(name, args, token).ConfigureAwait(false);
            return name is "hub_targets" or "hub_tasks" or "hub_proposals" ? new { items = result } : result;
        });
    }
    private bool _aiAutomaticActionRunning;
    private async Task ObserveAiDownloadAsync(AiProposalViewModel item, Task completion,
        List<CyEngineHub.Core.Tasks.HubTaskHandle> handles)
    {
        try
        {
            await completion.ConfigureAwait(true);
            var results = await Task.WhenAll(handles.Select(handle => handle.Completion)).ConfigureAwait(true);
            item.Status = results.All(result => result.Status == CyEngineHub.Core.Tasks.HubTaskStatus.Completed)
                ? "Téléchargement terminé" : "Lot partiel ou annulé · consulter Téléchargements";
        }
        catch (Exception exception)
        {
            item.Status = "Erreur · consulter Téléchargements";
            AppendRawLog("WARN", "MCP", exception.Message);
        }
    }
    private string _mcpStatus = "Désactivé · aucun accès IA";
    private AiProposalViewModel? _selectedAiProposal;
    public bool IsSettingsAiSelected => SelectedSettingsCategory?.Id == "ai";
    public bool McpEnabled { get => _mcpEnabled; private set => SetProperty(ref _mcpEnabled, value); }
    public string McpStatus { get => _mcpStatus; private set => SetProperty(ref _mcpStatus, value); }
    public ObservableCollection<AiProposalViewModel> AiProposals { get; } = [];
    public AiProposalViewModel? SelectedAiProposal { get => _selectedAiProposal; set => SetProperty(ref _selectedAiProposal, value); }
    public ICommand ToggleMcpCommand { get; private set; } = null!;
    public ICommand ApproveAiProposalCommand { get; private set; } = null!;
    public ICommand RejectAiProposalCommand { get; private set; } = null!;
    public string McpHttpUrl => _mcpHttp is { } server ? $"http://127.0.0.1:{server.Port}/mcp" : "";
    public string McpAccessToken => _mcpHttp?.Token ?? "";
    public bool McpHasAccessToken => McpAccessToken.Length != 0;
    public string McpConnectionConfigurations
    {
        get
        {
            if (!McpEnabled) return "Activez MCP pour afficher les informations de connexion.";
            var http = "Transport HTTP · " + McpHttpUrl;
            if (McpHasAccessToken) return http + "\nConfigurez le jeton de cette session comme identifiant Bearer.";

            var command = _mcpExecutable;
            var escaped = JsonSerializer.Serialize(command);
            string[] args = _mcpPipe is null ? [] : ["--pipe", _mcpPipe];
            return http + "\n\nCodex · config.toml (stdio)\n[mcp_servers.cyenginehub]\ncommand = " + escaped +
                "\nargs = " + JsonSerializer.Serialize(args) + "\n\nClaude Desktop · fusionner dans claude_desktop_config.json\nClaude Code · fusionner dans .mcp.json :\n" +
                JsonSerializer.Serialize(new { mcpServers = new { cyenginehub = new { command, args } } }, McpConfigurationJson) +
                "\n\nOpenCode · fusionner dans opencode.json :\n" +
                JsonSerializer.Serialize(new { mcp = new { cyenginehub = new { type = "local", command = new[] { command }.Concat(args), enabled = true } } }, McpConfigurationJson);
        }
    }

    private static string? ReadMcpPipeOverride()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--mcp-pipe");
        if (index < 0) return null;
        if (index + 1 < args.Length && args[index + 1] is { Length: > 0 and <= 100 } pipe &&
            pipe.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
            return pipe;
        throw new ArgumentException("--mcp-pipe exige un nom local de 1 à 100 caractères alphanumériques, points, tirets ou soulignés.");
    }

    private void InitializeAiCommands()
    {
        ToggleMcpCommand = new AsyncRelayCommand(() => ChangeMcpAsync(async () =>
        {
            if (McpEnabled)
            {
                StopMcp();
                if (_mcpServer is { } server) await server.ConfigureAwait(true);
            }
            else StartMcp();
            await _settings.SetAsync("ai.mcp.enabled.v1", McpEnabled.ToString()).ConfigureAwait(true);
        }), onError: ShowError);
        ApproveAiProposalCommand = new AsyncRelayCommand(ApproveAiProposalAsync, onError: ShowError);
        RejectAiProposalCommand = new RelayCommand(() =>
        {
            if (SelectedAiProposal is { Status: "En attente" } item) item.Status = "Refusée";
        });
    }

    private async Task ChangeMcpAsync(Func<Task> change)
    {
        if (_changingMcp) throw new InvalidOperationException("Un changement MCP est déjà en cours.");
        _changingMcp = true;
        try { await change().ConfigureAwait(true); }
        finally { _changingMcp = false; }
    }

    private async Task LoadAiSettingsAsync()
    {
        await LoadAssistantSettingsAsync().ConfigureAwait(true);
        var json = await _settings.GetAsync("ai.mcp.options.v1").ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try { ApplyMcpOptions(JsonSerializer.Deserialize<HubMcpOptions>(json, McpPreferencesJson) ?? new()); }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                ApplyMcpOptions(new() { AccessProfile = HubMcpAccessProfile.Protected });
                McpOptionsStatus = "Options MCP illisibles · vérifiez le profil et enregistrez les options. " + exception.Message;
                return;
            }
        }
        if (bool.TryParse(await _settings.GetAsync("ai.mcp.enabled.v1").ConfigureAwait(true), out var enabled) && enabled)
        {
            try { StartMcp(); }
            catch (InvalidOperationException) { /* StartMcp has already reported the bind failure. */ }
        }
    }

    private void StartMcp(string? pipeName = null)
    {
        if (_assistantStopping || McpEnabled) return;
        if (_mcpServer is { IsCompleted: false }) throw new InvalidOperationException("Le serveur MCP précédent termine sa fermeture. Réessayez.");
        SdkMcpRegistry registry;
        try
        {
            registry = SdkRegistry;
            _mcpHttp = new SdkMcpHttpServer(registry, _mcpOptions);
        }
        catch (InvalidOperationException exception)
        {
            McpStatus = $"MCP indisponible · port {_mcpOptions.LocalPort} · {exception.Message}";
            throw;
        }
        McpEnabled = true;
        NotifyMcpConnectionChanged();
        McpStatus = $"Actif · {McpProfileName(_mcpOptions.AccessProfile)} · {McpHttpUrl}";
        _mcpServer = null;
        // Token profiles expose only HTTP. The legacy same-account pipe has no Bearer exchange.
        if (_mcpOptions.RequiresToken) return;
        _mcpStop = new CancellationTokenSource();
        var token = _mcpStop.Token;
        _mcpServer = Task.Run(async () =>
        {
            try { await HubMcpTransport.ServeAsync(registry, token, pipeName ?? _mcpPipe).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!token.IsCancellationRequested) McpStatus = "MCP HTTP actif · proxy stdio indisponible · " + exception.Message;
                });
            }
        }, token);
    }

    public void StopMcp()
    {
        McpEnabled = false;
        var http = _mcpHttp;
        _mcpHttp = null;
        NotifyMcpConnectionChanged();
        var stop = _mcpStop;
        _mcpStop = null;
        stop?.Cancel();
        if (http is not null)
        {
            var pipe = _mcpServer;
            // HTTP workers may still be unwinding an operation dispatched to the UI thread.
            _mcpServer = Task.Run(async () =>
            {
                try
                {
                    http.Dispose();
                    if (pipe is not null) await pipe.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                finally { stop?.Dispose(); }
            });
        }
        McpStatus = "Désactivé · connexions coupées";
        foreach (var item in AiProposals.Where(item => item.Status == "En attente")) item.Status = "Révoquée";
    }

    private void NotifyMcpConnectionChanged()
    {
        OnPropertyChanged(nameof(McpHttpUrl));
        OnPropertyChanged(nameof(McpAccessToken));
        OnPropertyChanged(nameof(McpHasAccessToken));
        OnPropertyChanged(nameof(McpConnectionConfigurations));
    }

    private static async Task DisposeMcpCancellationAsync(Task? server, CancellationTokenSource stop)
    {
        try { if (server is not null) await server.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally { stop.Dispose(); }
    }

    private Task ApproveAiProposalAsync() => ExecuteAiProposalAsync(
        SelectedAiProposal ?? throw new InvalidOperationException("Sélectionnez une proposition."), false);

    private async Task ExecuteAiProposalAsync(AiProposalViewModel item, bool automatic)
    {
        if (!McpEnabled || item.Status != "En attente") throw new InvalidOperationException("Cette proposition n’est plus active.");
        var proposal = item.Proposal;
        var asset = _remoteFabCatalog.FirstOrDefault(asset => asset.AssetId == proposal.AssetId && NormalizeAssetSource(asset).Equals(proposal.Source, StringComparison.OrdinalIgnoreCase));
        if (asset is null)
        {
            if (proposal.Action != "categorize") throw new InvalidOperationException("Fiche absente : actualisez le catalogue.");
            var local = (await ConnectorDownloadCatalog.LoadAsync().ConfigureAwait(true)).FirstOrDefault(entry => entry.Id == proposal.AssetId && entry.MarketplaceId == proposal.Source)
                ?? throw new InvalidOperationException("Fiche locale absente.");
            if (!MarketplaceConnectors.Any(connector => connector.Id == local.MarketplaceId && connector.IsEnabled)) throw new InvalidOperationException("Marketplace désactivée.");
            item.Status = "Application…";
            try
            {
                await ConnectorDownloadCatalog.RenameAsync(local.Id, local.Name, local.Notes + "\nCatégories IA validées : " + string.Join(", ", proposal.Categories)).ConfigureAwait(true);
                if (_connectorLibraries.TryGetValue(local.MarketplaceId, out var library)) await library.LoadAsync().ConfigureAwait(true);
                item.Status = "Catégories appliquées";
            }
            catch { item.Status = "Erreur · nouvelle proposition nécessaire"; throw; }
            return;
        }
        if (!IsMarketplaceAvailable(proposal.Source)) throw new InvalidOperationException("Marketplace désactivée.");
        if (proposal.Action == "categorize")
        {
            item.Status = "Application…";
            try
            {
                var updated = _fabCollections.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                updated[asset.AssetId] = (_fabCollections.GetValueOrDefault(asset.AssetId) ?? []).Concat(proposal.Categories).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                await _settings.SetAsync(FabCollectionsSetting, JsonSerializer.Serialize(updated)).ConfigureAwait(true);
                _fabCollections[asset.AssetId] = updated[asset.AssetId];
                RebuildProjectCreationCollections();
                await RefreshRemoteFabViewNowAsync().ConfigureAwait(true);
                item.Status = "Collections appliquées";
            }
            catch { item.Status = "Erreur · nouvelle proposition nécessaire"; throw; }
            return;
        }
        if (IsFabWebAsset(asset))
        {
            if (proposal.Action != "download" || !asset.WebCanDownload)
                throw new InvalidOperationException("Téléchargez d’abord cette variante depuis Fab. L’installation web automatique n’est pas disponible.");
            await OpenFabWebAssetAsync(asset).ConfigureAwait(true);
            item.Status = "Page officielle ouverte · téléchargement à confirmer sur Fab";
            return;
        }
        if (!asset.ProjectVersions.Any(version => version.ArtifactId == proposal.VariantId)) throw new InvalidOperationException("Variante devenue indisponible.");
        // Hand off to the same reviewed, version-aware UI as a human click. Never execute a model-supplied command.
        LibrarySource = IsQuixelAsset(asset) ? "Quixel" : "Fab";
        ShowSection(PageSection.Epic);
        using var card = CreateRemoteAssetCard(asset);
        OpenRemoteFabProduct(card);
        if (proposal.Action == "install")
        {
            var target = FabInstallTargets.FirstOrDefault(target => target.Id == proposal.TargetId && target.IsCompatible)
                ?? throw new InvalidOperationException("La cible n’est plus compatible avec ce produit. Rien n’a été installé.");
            foreach (var option in FabInstallTargets) option.IsSelected = ReferenceEquals(option, target);
            RebuildCompatibleFabInstallVersions();
            if (!CompatibleFabInstallVersions.Any(option => option.Version.ArtifactId == proposal.VariantId))
                throw new InvalidOperationException("Cette variante ne correspond pas au moteur cible.");
        }
        foreach (var option in FabInstallVersions) option.IsSelected = option.Version.ArtifactId == proposal.VariantId;
        foreach (var option in CompatibleFabInstallVersions) option.IsSelected = option.Version.ArtifactId == proposal.VariantId;
        if (automatic)
        {
            if (!McpEnabled || !_mcpOptions.IsAutomatic(proposal.Action))
                throw new InvalidOperationException("Autorisation automatique révoquée.");
            item.Status = "Exécution automatique…";
            if (proposal.Action == "download") await DownloadSelectedFabVersionsAsync(FabInstallDestination.Library).ConfigureAwait(true);
            else await InstallSelectedFabVersionsIntoTargetsAsync().ConfigureAwait(true);
            item.Status = "Workflow automatique terminé · consulter Téléchargements pour le résultat des tâches";
            return;
        }
        IsFabDownloadOptionsVisible = proposal.Action == "download";
        IsFabInstallOptionsVisible = proposal.Action == "install";
        item.Status = "Préparée dans l’interface · confirmation finale requise";
        StatusMessage = "Proposition IA préparée. Vérifiez la variante, le stockage et le script, puis confirmez dans le popup.";
    }

    private sealed class DesktopAiBackend(MainWindowViewModel owner, bool editor = false) : IHubAiBackend
    {
        private bool CanRead => editor ? owner.EditorBridgeEnabled : owner.McpEnabled;
        private IReadOnlyList<HubAiAsset>? _cachedAssets;
        private DateTimeOffset _cacheExpires;
        private string? _cachedAvailability;

        public async Task<HubAiDownloadEstimate> EstimateDownloadAsync(string assetId, string source, string variantId, CancellationToken cancellationToken)
        {
            var asset = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CanRead || !owner.IsMarketplaceAvailable(source)) throw new InvalidOperationException("Connexion ou marketplace désactivée.");
                return owner._remoteFabCatalog.FirstOrDefault(item => item.AssetId == assetId && NormalizeAssetSource(item).Equals(source, StringComparison.OrdinalIgnoreCase));
            });
            var version = asset?.ProjectVersions.FirstOrDefault(item => item.ArtifactId == variantId);
            if (asset is null || version is null || !IsLauncherFabAsset(asset))
                return new(assetId, source, variantId, null, null, null, "unknown: this provider does not expose a preflight size here");
            try
            {
                var estimate = await owner._directFabClient.EstimateDownloadAsync(asset, version, cancellationToken).ConfigureAwait(false);
                return new(assetId, source, variantId, estimate?.DownloadBytes, estimate?.UncompressedBytes, estimate?.FileCount,
                    estimate is null ? "unknown" : "Fab manifest; full transfer without deduplication; final ZIP size unknown");
            }
            catch (Exception exception) when (exception is HttpRequestException or CyEngineHub.Fab.Remote.DirectFabException or IOException)
            { return new(assetId, source, variantId, null, null, null, "unknown: manifest unavailable; reconnect or retry"); }
        }
        public async Task<HubMcpOptions> GetOptionsAsync(CancellationToken cancellationToken) =>
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CanRead) throw new InvalidOperationException("Connection disabled.");
                return owner._mcpOptions;
            });
        public async Task<IReadOnlyList<HubAiAsset>> GetAssetsAsync(CancellationToken cancellationToken)
        {
            // Catalogs are hydrated from the marketplace disk caches by the hub.
            // MCP reads metadata only: never create cards or load thumbnail bitmaps here.
            var cached = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CanRead) throw new InvalidOperationException("Connection disabled.");
                var availability = owner.IsMarketplaceAvailable("Fab") + ":" + owner.IsMarketplaceAvailable("Quixel") + ":"
                    + string.Join(",", owner.MarketplaceConnectors.Where(item => item.IsEnabled).Select(item => item.Id));
                return (Assets: _cachedAvailability == availability && DateTimeOffset.UtcNow < _cacheExpires ? _cachedAssets : null,
                    Availability: availability);
            });
            if (cached.Assets is not null) return cached.Assets;
            var snapshot = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CanRead) throw new InvalidOperationException("Connection disabled.");
                return (Assets: owner._remoteFabCatalog.ToArray(), Downloads: owner._fabStoredDownloads.ToArray(),
                    Collections: owner._fabCollections.ToDictionary(pair => pair.Key, pair => pair.Value),
                    Fab: owner.IsMarketplaceAvailable("Fab"), Quixel: owner.IsMarketplaceAvailable("Quixel"),
                    Connectors: owner.MarketplaceConnectors.Where(item => item.IsEnabled).Select(item => item.Id).ToHashSet(),
                    Catalog: owner.ConnectorDownloadCatalog);
            });
            var local = await snapshot.Catalog.LoadAsync().ConfigureAwait(false);
            var assets = await Task.Run<IReadOnlyList<HubAiAsset>>(() =>
            {
                var downloads = snapshot.Downloads.ToLookup(item => item.Source.ToLowerInvariant() + ":" + item.AssetId);
                var result = new List<HubAiAsset>();
                foreach (var asset in snapshot.Assets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = NormalizeAssetSource(asset);
                    if ((source == "Fab" && !snapshot.Fab) || (source == "Quixel" && !snapshot.Quixel)) continue;
                    if (IsFabWebAsset(asset) && !snapshot.Connectors.Contains(MegascansFabConnectorId)) continue;
                    result.Add(new(asset.AssetId, source, asset.Title, asset.Description,
                        asset.Categories.Concat(snapshot.Collections.GetValueOrDefault(asset.AssetId) ?? []).Distinct().ToArray(),
                        asset.ProjectVersions.Select(version => new HubAiVariant(version.ArtifactId, version.EngineVersions.ToArray(), version.Platforms.ToArray())).ToArray(),
                        downloads[source.ToLowerInvariant() + ":" + asset.AssetId].Select(item => item.Path).ToArray(),
                        asset.ListingUri?.AbsoluteUri, asset.ThumbnailUri?.AbsoluteUri,
                        IsFabWebAsset(asset) ? (asset.WebCanDownload ? "official_browser" : "unavailable") : "native"));
                }
                result.AddRange(local.Where(item => snapshot.Connectors.Contains(item.MarketplaceId)).Select(item =>
                    new HubAiAsset(item.Id, item.MarketplaceId, item.Name, item.Notes, [item.AssetKind], [], [item.FilePath])));
                return result;
            }, cancellationToken).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _cachedAssets = assets;
                _cachedAvailability = cached.Availability;
                _cacheExpires = DateTimeOffset.UtcNow.AddSeconds(5);
            });
            return assets;
        }

        public async Task<IReadOnlyList<HubAiTarget>> GetTargetsAsync(CancellationToken cancellationToken) =>
            await Dispatcher.UIThread.InvokeAsync<IReadOnlyList<HubAiTarget>>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return owner.Projects.Select(item => new HubAiTarget("project:" + item.Project.Id, item.Name, item.EngineLabel, "project"))
                    .Concat(owner.Engines.Select(item => new HubAiTarget("engine:" + item.Engine.Id, item.DisplayName, item.Engine.Version.ToString(), "engine"))).ToArray();
            });

        public async Task<object> GetTasksAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = await GetOptionsAsync(cancellationToken).ConfigureAwait(false);
            return owner._taskScheduler.GetSnapshot().Take(HubMcpOptions.Bound(options.TaskHistoryLimit)).Select(item => new {
                item.Id, item.Title, status = item.Status.ToString(), item.Progress, item.CreatedAt, item.StartedAt, item.FinishedAt }).ToArray();
        }

        public async Task<object> ProposeAsync(HubAiProposal proposal, CancellationToken cancellationToken) =>
            await Dispatcher.UIThread.InvokeAsync<object>(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!owner.McpEnabled) throw new InvalidOperationException("MCP disabled.");
                var automatic = owner._mcpOptions.IsAutomatic(proposal.Action);
                var queuedDownload = automatic && proposal.Action == "download";
                if (automatic && !queuedDownload && owner._aiAutomaticActionRunning) throw new InvalidOperationException("Une action automatique est en cours. Réessayez après sa fin.");
                if (owner.AiProposals.Count(item => item.Status == "En attente") >= HubMcpOptions.Bound(owner._mcpOptions.PendingProposalLimit)) throw new InvalidOperationException("Limite de propositions atteinte. Réglez-la dans les options MCP.");
                var name = owner._remoteFabCatalog.FirstOrDefault(asset => asset.AssetId == proposal.AssetId && NormalizeAssetSource(asset).Equals(proposal.Source, StringComparison.OrdinalIgnoreCase))?.Title ?? proposal.AssetId;
                var item = new AiProposalViewModel(proposal, name);
                owner.AiProposals.Insert(0, item);
                while (owner.AiProposals.Count > HubMcpOptions.Bound(owner._mcpOptions.ProposalHistoryLimit))
                {
                    var terminal = owner.AiProposals.LastOrDefault(value => value.Status != "En attente" && value.Status != "Exécution automatique…" && value.Status != "Téléchargement en file");
                    if (terminal is null) break;
                    owner.AiProposals.Remove(terminal);
                }
                owner.SelectedAiProposal ??= item;
                if (queuedDownload)
                {
                    var asset = owner._remoteFabCatalog.FirstOrDefault(value => value.AssetId == proposal.AssetId && NormalizeAssetSource(value).Equals(proposal.Source, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("Fiche indisponible dans le cache.");
                    if (!owner.IsMarketplaceAvailable(proposal.Source)) throw new InvalidOperationException("Marketplace désactivée.");
                    if (IsFabWebAsset(asset))
                    {
                        item.Status = "Téléchargement à effectuer sur Fab";
                        return new { proposal.Id, status = "requires_browser", productUrl = asset.ListingUri?.AbsoluteUri };
                    }
                    var version = asset.ProjectVersions.FirstOrDefault(value => value.ArtifactId == proposal.VariantId)
                        ?? throw new InvalidOperationException("Variante indisponible.");
                    var handles = new List<CyEngineHub.Core.Tasks.HubTaskHandle>();
                    var completion = owner.DownloadSelectedFabVersionsAsync(FabInstallDestination.Library, asset,
                        [new FabInstallVersionOptionViewModel(version, true)], handle => { handles.Add(handle); item.TaskIds.Add(handle.Id); });
                    if (completion.IsFaulted) await completion.ConfigureAwait(true);
                    if (handles.Count == 0)
                    {
                        var existing = owner._fabVersionDownloadsByTaskId.Where(pair => !pair.Value.IsTerminal && pair.Value.AssetId == asset.AssetId
                            && pair.Value.Source == proposal.Source && pair.Value.ArtifactId == proposal.VariantId).Select(pair => pair.Key).ToArray();
                        item.TaskIds.AddRange(existing);
                        item.Status = "Déjà en file";
                        return new { proposal.Id, status = "already_queued", taskIds = item.TaskIds.ToArray() };
                    }
                    item.Status = "Téléchargement en file";
                    _ = owner.ObserveAiDownloadAsync(item, completion, handles);
                    return new { proposal.Id, status = "queued", taskIds = item.TaskIds.ToArray(), automatic = true };
                }
                if (automatic)
                {
                    owner._aiAutomaticActionRunning = true;
                    try
                    {
                        await owner.ExecuteAiProposalAsync(item, true).ConfigureAwait(true);
                        return new { proposal.Id, status = item.Status, automatic = true };
                    }
                    catch { item.Status = "Erreur · consulter les tâches avant de réessayer"; throw; }
                    finally { owner._aiAutomaticActionRunning = false; }
                }
                owner.StatusMessage = "Nouvelle proposition IA · Paramètres > IA / MCP";
                return new { proposal.Id, status = "pending_human_approval", review = "CyEngineHub > Paramètres > IA / MCP. No files changed, nothing downloaded/installed." };
            });

        public async Task<object> GetProposalsAsync(CancellationToken cancellationToken) => await Dispatcher.UIThread.InvokeAsync<object>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return owner.AiProposals.Select(item => new { item.Proposal.Id, item.Proposal.Action, item.Proposal.AssetId, item.Proposal.Source, item.Status, taskIds = item.TaskIds.ToArray() }).ToArray();
        });
    }
}
