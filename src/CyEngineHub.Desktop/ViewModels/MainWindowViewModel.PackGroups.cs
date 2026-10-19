using System.Text.Json;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Core.Settings;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public List<LibraryPackGroup> PackGroups { get; } = [];
    public List<LibraryPackGroupItem> PackGroupCandidates { get; } = [];
    public IReadOnlyList<IFabInstallationScript> PackGroupScripts => _fabInstallationScripts.Scripts;
    private bool _packGroupsLoaded;
    public bool IsPackGroupBusy { get; private set; }
    public string PackGroupStatus { get; private set; } = "Alpha · seules les variantes locales sont utilisées. Aucun téléchargement implicite.";
    public async Task LoadPackGroupsAsync()
    {
        try
        {
            if (!_packGroupsLoaded)
            {
                PackGroups.AddRange(await new LibraryPackGroupStore(_settings).LoadAsync());
                _packGroupsLoaded = true;
            }
            PackGroupCandidates.Clear();
            foreach (var item in _fabStoredDownloads)
                PackGroupCandidates.Add(Candidate(item.AssetId, item.ArtifactId, item.AssetTitle ?? item.Version,
                    item.Source, item.Path, ParseContentKind(item.PackageKind), item.EngineVersions ?? []));
            foreach (var item in FabLocalContentPacks.Where(p => p.Pack.Kind == Fab.Library.FabLocalContentKind.Unreal && !PackGroupCandidates.Any(c => c.Path.Equals(p.Path, StringComparison.OrdinalIgnoreCase))))
                PackGroupCandidates.Add(Candidate(item.Path, item.Path, item.Name, "Local", item.Path, LibraryPackageContentKind.ContentPack, [item.Pack.EngineAssociation ?? ""]));
            foreach (var item in await ConnectorDownloadCatalog.LoadAsync())
                PackGroupCandidates.Add(Candidate(item.Id, item.Id, item.Name, item.MarketplaceId, item.FilePath,
                    item.AssetKind.Contains("Plugin", StringComparison.OrdinalIgnoreCase) ? LibraryPackageContentKind.CodePlugin :
                    item.AssetKind.Contains("Projet", StringComparison.OrdinalIgnoreCase) ? LibraryPackageContentKind.CompleteProject :
                    item.AssetKind.Contains("RAW", StringComparison.OrdinalIgnoreCase) ? LibraryPackageContentKind.RawAsset : LibraryPackageContentKind.ContentPack, []));
        }
        catch (Exception ex) { PackGroupStatus = ex.Message; ShowError(ex); }
    }
    private LibraryPackGroupItem Candidate(string asset, string artifact, string name, string source, string path,
        LibraryPackageContentKind kind, IReadOnlyList<string> versions)
    {
        var script = _fabInstallationScripts.Find(kind is LibraryPackageContentKind.CodePlugin or LibraryPackageContentKind.EnginePackage
            ? UnrealProjectPluginInstallationScript.ScriptId : UnrealContentPackInstallationScript.ScriptId);
        if (source == "cyenginehub.marketplace-cosmos" && kind == LibraryPackageContentKind.ContentPack)
            script = _fabInstallationScripts.Find(UnrealRelocatedContentInstallationScript.ScriptId);
        if (source == "cyenginehub.marketplace-cosmos" && kind == LibraryPackageContentKind.RawAsset)
        {
            var raw = _fabInstallationScripts.Find(UnrealMegascansInstallationScript.ScriptId)!;
            var move = _fabInstallationScripts.Find(UnrealRelocatedContentInstallationScript.ScriptId)!;
            return new(asset, artifact, name, source, path, kind, versions,
                [new(raw.Descriptor.Id, raw.Descriptor.Options.ToDictionary(o => o.Id, o => o.Id == "importMode" ? "raw" : o.DefaultValue)),
                 new(move.Descriptor.Id, move.Descriptor.Options.ToDictionary(o => o.Id, o => o.Id == "conflictPolicy" ? "fail" : o.DefaultValue))]);
        }
        return new(asset, artifact, name, source, path, kind, versions,
            script is null ? [] : [new(script.Descriptor.Id, script.Descriptor.Options.ToDictionary(o => o.Id,
                o => o.Id == "conflictPolicy" ? "fail" : o.DefaultValue))]);
    }
    public async Task SavePackGroupsAsync()
    {
        try
        {
            if (!_packGroupsLoaded) throw new InvalidOperationException("Rechargez d’abord les groupes.");
            await new LibraryPackGroupStore(_settings).SaveAsync(PackGroups);
            PackGroupStatus = "Groupes et chaînes de scripts enregistrés.";
        }
        catch (Exception ex) { PackGroupStatus = ex.Message; ShowError(ex); }
    }
    public async Task InstallPackGroupAsync(LibraryPackGroup group, ProjectCardViewModel project)
    {
        if (IsPackGroupBusy) return;
        IsPackGroupBusy = true;
        var completedPacks = 0;
        string? pendingAgentWorkspace = null;
        try
        {
            var engine = ResolveEngine(project.Project.EngineAssociation) ?? throw new InvalidOperationException("Moteur du projet non installé.");
            if (!File.Exists(project.Project.FilePath)) throw new FileNotFoundException("Projet cible absent.");
            if (group.Items.Count == 0) throw new InvalidOperationException("Groupe vide.");
            // Freeze the entire plan before enqueueing; subsequent edits cannot change a running operation.
            var plan = JsonSerializer.Deserialize<LibraryPackGroup>(JsonSerializer.Serialize(group))!;
            var pipeline = new FabInstallationPipeline(_fabInstallationScripts);
            FabInstallationScriptContext Context(LibraryPackGroupItem item, string path) => new(
                new(item.AssetId, "ue", item.Name, "", item.Source, "ASSET_PACK", null, null, [], []),
                new(item.ArtifactId, [], item.EngineVersions, []), item.ContentKind, path,
                new(project.Project.Id, project.Name, FabInstallationTargetKind.Project, project.Project.RootPath,
                    engine.Version.ToString(), project.Project, engine), new Dictionary<string, string>());
            foreach (var item in plan.Items)
            {
                if (!Directory.Exists(item.Path) && !File.Exists(item.Path)) throw new FileNotFoundException("Pack absent : " + item.Name);
                if (item.ContentKind == LibraryPackageContentKind.CompleteProject) throw new InvalidOperationException("Un projet complet ne peut pas être injecté dans un groupe.");
                pipeline.Validate(Context(item, item.Path), item.Steps);
                var sourceVersion = item.EngineVersions.Select(ParseEngineVersion).Where(v => v.Major > 0).OrderBy(v => v.Major).ThenBy(v => v.Minor).FirstOrDefault();
                if (sourceVersion is not null && (sourceVersion.Major, sourceVersion.Minor).CompareTo((engine.Version.Major, engine.Version.Minor)) > 0 &&
                    !item.Steps.Any(s => s.ScriptId == UnrealVersionBridgeInstallationScript.ScriptId))
                    throw new InvalidOperationException("Pack plus récent que le projet : ajoutez la conversion avant installation. " + item.Name);
            }
            var handle = _taskScheduler.Enqueue(new HubTaskDefinition("Bibliothèque · Groupes Alpha", "Installer " + plan.Name,
                [ProjectTaskLink(project.Project.RootPath, project.Name)], async (task, token) =>
                {
                    foreach (var item in plan.Items)
                    {
                        token.ThrowIfCancellationRequested();
                        var scratch = Path.Combine(DataDirectory, "DerivedInstallSources", Guid.NewGuid().ToString("N"));
                        try
                        {
                            if (File.Exists(item.Path)) await ConnectorLibraryPackageService.MaterializeFileAsync(item.Path, scratch, token);
                            else await DerivedLibraryPackageService.MaterializeAsync(item.Path, scratch, item.ContentKind, cancellationToken: token);
                            var progress = new InlineProgress<FabInstallationScriptProgress>(p => task.Report(p.Fraction, $"{completedPacks + 1}/{plan.Items.Count} · {item.Name} · {p.Message}"));
                            var steps = item.Steps;
                            if (item.Source == "cyenginehub.marketplace-cosmos")
                            {
                                var destination = await new CosmosProjectSettingsStore(_settings).LoadAsync(project.Project.RootPath);
                                steps = steps.Select(step => step.ScriptId == UnrealRelocatedContentInstallationScript.ScriptId &&
                                    step.Configuration.GetValueOrDefault("destinationFolder") is null or "" or "CyEngineHubImports"
                                    ? step with { Configuration = new Dictionary<string, string>(step.Configuration) { ["destinationFolder"] = destination } } : step).ToArray();
                            }
                            var packContext = Context(item, scratch);
                            var results = await pipeline.ExecuteAsync(packContext, steps, progress, token);
                            var pending = results.FirstOrDefault(r => r.Outputs.GetValueOrDefault("agentPending") == "true");
                            if (pending is not null)
                            {
                                pendingAgentWorkspace = pending.Outputs["agentWorkspace"];
                                task.Report(null, "Groupe suspendu : réparation IA à reprendre dans " + pendingAgentWorkspace);
                                break;
                            }
                            foreach (var converted in results.Where(r => r.Outputs.GetValueOrDefault("registerDerivedVariant") == "true" && r.Outputs.ContainsKey("variant")))
                            {
                                var registration = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => RegisterDerivedVariantAsync(
                                    packContext.Asset, packContext.Version, engine.Version.ToString(), item.ContentKind,
                                    converted.Outputs["variant"], converted.Outputs.GetValueOrDefault("manifest", "")));
                                await registration;
                            }
                            completedPacks++;
                        }
                        finally { DeleteOwnedDerivedDirectory(scratch, "DerivedInstallSources"); }
                    }
                }));
            var result = await handle.Completion;
            PackGroupStatus = $"{completedPacks}/{plan.Items.Count} packs installés · {result.Status}. " +
                (result.Status == HubTaskStatus.Completed ? "" : "Les packs déjà installés sont conservés. " + result.Error);
            if (pendingAgentWorkspace is not null) PackGroupStatus = $"{completedPacks}/{plan.Items.Count} packs installés · Groupe suspendu pour réparation IA : {pendingAgentWorkspace}";
            StatusMessage = PackGroupStatus;
        }
        catch (Exception ex) { PackGroupStatus = $"{completedPacks} pack(s) terminé(s). " + ex.Message; ShowError(ex); }
        finally { IsPackGroupBusy = false; }
    }
}
