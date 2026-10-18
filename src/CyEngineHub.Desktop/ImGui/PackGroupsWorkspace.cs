using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Installation;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private bool _openPackGroups;
    private int _packGroupIndex, _packCandidateIndex, _packProjectIndex;
    private string _packSearch = "";
    private HashSet<string> _packSelectionKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _packSelectionOnly;
    private void OpenPackGroups(MainWindowViewModel vm, object[]? selection = null)
    {
        _packSelectionKeys = new((selection ?? []).Select(MainWindowViewModel.ResourceIdentity), StringComparer.OrdinalIgnoreCase);
        _packSelectionOnly = _packSelectionKeys.Count > 0;
        _openPackGroups = true; _actions.Enqueue(() => _ = vm.LoadPackGroupsAsync());
    }
    private void DrawPackGroups(MainWindowViewModel vm)
    {
        if (_openPackGroups) { Gui.OpenPopup("Groupes de packs · Alpha"); _openPackGroups = false; }
        Gui.SetNextWindowSize(new(Math.Min(1100, Gui.GetIO().DisplaySize.X - 50), Math.Min(760, Gui.GetIO().DisplaySize.Y - 50)), ImGuiCond.Appearing);
        if (!Gui.BeginPopupModal("Groupes de packs · Alpha", ImGuiWindowFlags.NoSavedSettings)) return;
        Gui.TextWrapped("Variantes locales et scripts ordonnés · aucun téléchargement implicite. Arrêt au premier échec ; les installations précédentes sont conservées.");
        if (Gui.Button("Fermer")) Gui.CloseCurrentPopup(); Record("pack-groups/close"); Gui.SameLine();
        Gui.BeginDisabled(vm.IsPackGroupBusy);
        if (Gui.Button("Nouveau groupe")) { vm.PackGroups.Add(new(Guid.NewGuid().ToString("N"), "Nouveau groupe", [])); _packGroupIndex = vm.PackGroups.Count - 1; }
        Record("pack-groups/new"); Gui.SameLine();
        if (Gui.Button("Enregistrer")) _actions.Enqueue(() => _ = vm.SavePackGroupsAsync()); Record("pack-groups/save");
        Gui.Separator();
        Gui.BeginChild("group-list", new(200 * UiScale, -55), ImGuiChildFlags.Borders);
        for (var i = 0; i < vm.PackGroups.Count; i++) if (Gui.Selectable(vm.PackGroups[i].Name + "###group-" + i, _packGroupIndex == i)) _packGroupIndex = i;
        Gui.EndChild(); Gui.SameLine();
        Gui.BeginChild("group-editor", new(0, -55), ImGuiChildFlags.Borders);
        if (vm.PackGroups.Count > 0)
        {
            _packGroupIndex = Math.Clamp(_packGroupIndex, 0, vm.PackGroups.Count - 1);
            var group = vm.PackGroups[_packGroupIndex];
            var name = group.Name;
            if (Gui.InputText("Nom du groupe", ref name, 120)) vm.PackGroups[_packGroupIndex] = group = group with { Name = name };
            Record("pack-groups/name");
            Gui.SameLine();
            Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Rgb(0x882F43));
            var removeGroup = Gui.Button("Retirer le groupe"); Gui.PopStyleColor();
            if (removeGroup) { vm.PackGroups.RemoveAt(_packGroupIndex); Gui.EndChild(); Gui.EndDisabled(); Gui.TextWrapped("Groupe retiré de la liste de travail. Enregistrez pour conserver ce changement ; fichiers intacts."); Gui.EndPopup(); return; }
            Gui.InputText("Rechercher un pack local", ref _packSearch, 200);
            if (_packSelectionKeys.Count > 0) Gui.Checkbox("Variantes locales de la sélection uniquement", ref _packSelectionOnly);
            var candidates = vm.PackGroupCandidates.Where(p => p.Name.Contains(_packSearch, StringComparison.OrdinalIgnoreCase) &&
                (!_packSelectionOnly || _packSelectionKeys.Contains("asset|" + p.Source + "|" + p.AssetId) ||
                 _packSelectionKeys.Contains("pack|" + (Path.IsPathFullyQualified(p.Path) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(p.Path)) : p.Path)))).ToArray();
            if (candidates.Length == 0) Gui.TextWrapped("Aucune variante locale correspondante. Téléchargez le pack ou désactivez le filtre de sélection.");
            if (candidates.Length > 0)
            {
                _packCandidateIndex = Math.Clamp(_packCandidateIndex, 0, candidates.Length - 1);
                if (Gui.BeginCombo("Variante", candidates[_packCandidateIndex].Name + " · " + string.Join(", ", candidates[_packCandidateIndex].EngineVersions)))
                {
                    for (var i = 0; i < candidates.Length; i++)
                    { if (Gui.Selectable(candidates[i].Name + " · " + string.Join(", ", candidates[i].EngineVersions) + "###candidate" + i, i == _packCandidateIndex)) _packCandidateIndex = i; Record("pack-groups/candidate/" + i); }
                    Gui.EndCombo();
                }
                Record("pack-groups/variant");
                Gui.TextWrapped(candidates[_packCandidateIndex].Path);
                if (Gui.Button("Ajouter cette variante") && !group.Items.Any(p => p.Path == candidates[_packCandidateIndex].Path))
                    vm.PackGroups[_packGroupIndex] = group = group with { Items = group.Items.Append(candidates[_packCandidateIndex]).ToArray() };
                Record("pack-groups/add");
            }
            for (var i = 0; i < group.Items.Count; i++)
            {
                    Gui.PushID(i); var item = group.Items[i];
                if (Gui.CollapsingHeader((i + 1) + " · " + item.Name + " · " + string.Join(", ", item.EngineVersions)))
                {
                    Gui.TextWrapped(item.Path);
                    var items = group.Items.ToList();
                    Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Rgb(0x882F43));
                    var remove = Gui.Button("Retirer le pack"); Gui.PopStyleColor(); Gui.SameLine();
                    var up = Gui.Button("Monter"); Gui.SameLine(); var down = Gui.Button("Descendre");
                    if (remove || up && i > 0 || down && i + 1 < items.Count)
                    {
                        if (remove) items.RemoveAt(i);
                        else { var other = up ? i - 1 : i + 1; (items[other], items[i]) = (items[i], items[other]); }
                        vm.PackGroups[_packGroupIndex] = group with { Items = items }; Gui.PopID(); break;
                    }
                    var steps = item.Steps.ToList();
                    for (var s = 0; s < steps.Count; s++)
                    {
                        Gui.PushID(s); var step = steps[s]; var descriptor = vm.PackGroupScripts.FirstOrDefault(script => script.Descriptor.Id == step.ScriptId)?.Descriptor;
                        Gui.TextColored(ImGuiTheme.Warning, $"{s + 1} · {descriptor?.DisplayName ?? step.ScriptId}");
                        if (descriptor?.IsBuiltIn == false) Gui.TextWrapped("Script externe : du code local sera exécuté lors de l’installation.");
                        var config = new Dictionary<string, string>(step.Configuration);
                        foreach (var option in descriptor?.Options ?? [])
                        {
                            var value = config.GetValueOrDefault(option.Id, option.DefaultValue); Gui.PushID(option.Id);
                            if (option.Kind == FabInstallationOptionKind.Boolean)
                            { var flag = value == "true"; if (Gui.Checkbox(option.DisplayName, ref flag)) config[option.Id] = flag ? "true" : "false"; }
                            else if (option.Kind == FabInstallationOptionKind.Choice)
                            { if (Gui.BeginCombo(option.DisplayName, value)) { foreach (var choice in option.Choices) if (Gui.Selectable(choice, choice == value)) config[option.Id] = choice; Gui.EndCombo(); } }
                            else if (Gui.InputText(option.DisplayName, ref value, 4096)) config[option.Id] = value;
                            if (Gui.IsItemHovered()) Gui.SetTooltip(option.Description); Gui.PopID();
                        }
                        steps[s] = step with { Configuration = config };
                        Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Rgb(0x882F43));
                        var drop = Gui.Button("Retirer l’étape"); Gui.PopStyleColor(); Gui.SameLine(); var earlier = Gui.Button("↑"); Gui.SameLine(); var later = Gui.Button("↓");
                        Gui.Separator(); Gui.PopID();
                        if (drop || earlier && s > 0 || later && s + 1 < steps.Count)
                        { if (drop) steps.RemoveAt(s); else { var other = earlier ? s - 1 : s + 1; (steps[s], steps[other]) = (steps[other], steps[s]); } break; }
                    }
                    if (Gui.BeginCombo("Ajouter un script", "Choisir une étape…"))
                    {
                        foreach (var script in vm.PackGroupScripts.Where(s => s.Descriptor.SupportedContentKinds.Contains(item.ContentKind) && s.Descriptor.SupportedTargetKinds.Contains(FabInstallationTargetKind.Project)))
                            if (Gui.Selectable(script.Descriptor.DisplayName)) steps.Add(new(script.Descriptor.Id, script.Descriptor.Options.ToDictionary(o => o.Id, o => o.DefaultValue)));
                        Gui.EndCombo();
                    }
                    items[i] = item with { Steps = steps }; vm.PackGroups[_packGroupIndex] = group = group with { Items = items };
                }
                Record("pack-groups/item/" + i);
                Gui.PopID();
            }
            Gui.Separator();
            if (vm.Projects.Count > 0)
            {
                _packProjectIndex = Math.Clamp(_packProjectIndex, 0, vm.Projects.Count - 1); var project = vm.Projects[_packProjectIndex];
                if (Gui.BeginCombo("Projet cible", project.Name + " · " + project.EngineLabel))
                { for (var i = 0; i < vm.Projects.Count; i++) if (Gui.Selectable(vm.Projects[i].Name + "###project" + i, _packProjectIndex == i)) _packProjectIndex = i; Gui.EndCombo(); }
                Gui.TextWrapped(project.Path);
                Gui.BeginDisabled(group.Items.Count == 0 || group.Items.Any(p => p.Steps.Count == 0));
                Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Rgb(0x246E45));
                if (Gui.Button("Installer le groupe dans ce projet")) { var plan = group; var target = vm.Projects[_packProjectIndex]; _actions.Enqueue(() => _ = vm.InstallPackGroupAsync(plan, target)); }
                Record("pack-groups/install"); Gui.PopStyleColor(); Gui.EndDisabled();
            }
            else Gui.TextDisabled("Ajoutez un projet Unreal pour installer ce groupe.");
        }
        Gui.EndChild(); Gui.EndDisabled(); Gui.TextWrapped(vm.PackGroupStatus); Gui.EndPopup();
    }
}
