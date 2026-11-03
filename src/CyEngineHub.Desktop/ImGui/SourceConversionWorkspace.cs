using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Conversion;
using CyEngineHub.Fab.Installation;
using Hexa.NET.ImGui;
using Gui = Hexa.NET.ImGui.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed unsafe partial class ImGuiWorkspace
{
    private void DrawSourceConversion(MainWindowViewModel vm)
    {
        const string title = "Convertir en dérivé local · Alpha";
        if (vm.IsSourceConversionVisible && !Gui.IsPopupOpen(title)) Gui.OpenPopup(title);
        Gui.SetNextWindowSize(new(Math.Min(820 * UiScale, Gui.GetIO().DisplaySize.X - 50), Math.Min(520 * UiScale, Gui.GetIO().DisplaySize.Y - 50)), ImGuiCond.Appearing);
        if (!Gui.BeginPopupModal(title, ImGuiWindowFlags.NoSavedSettings)) return;
        if (!vm.IsSourceConversionVisible) { Gui.CloseCurrentPopup(); Gui.EndPopup(); return; }
        var wasEditing = Gui.IsAnyItemActive();
        Gui.TextWrapped(vm.SourceConversionAsset?.Name ?? "Pack local");
        Gui.TextWrapped("Le convertisseur s’exécute sur une copie. Le dérivé sera ajouté à votre bibliothèque avec son lien d’origine, sans installation dans un projet. Blender doit être installé ; ce n’est pas un bac à sable système.");
        Gui.BeginDisabled(vm.IsSourceConversionBusy);
        var selected = vm.SourceConverters.Find(vm.SourceConversionConverterId)!;
        if (Gui.BeginCombo("Convertisseur", selected.Descriptor.DisplayName))
        {
            foreach (var item in vm.SourceConverters.Converters)
                if (Gui.Selectable(item.Descriptor.DisplayName, item == selected)) vm.SelectSourceConverter(item.Descriptor.Id);
            Gui.EndCombo();
        }
        Record("source-conversion/converter");
        Gui.TextWrapped(selected.Descriptor.Description);
        Gui.TextWrapped("Entrées : " + string.Join(", ", selected.Descriptor.SourceExtensions));
        foreach (var option in selected.Descriptor.Options)
        {
            Gui.PushID(option.Id);
            var value = vm.SourceConversionOptions.GetValueOrDefault(option.Id, option.DefaultValue);
            if (option.Kind == FabInstallationOptionKind.Boolean)
            { var flag = value == "true"; if (Gui.Checkbox(option.DisplayName, ref flag)) vm.SourceConversionOptions[option.Id] = flag ? "true" : "false"; }
            else if (option.Kind == FabInstallationOptionKind.Choice)
            { if (Gui.BeginCombo(option.DisplayName, value)) { foreach (var choice in option.Choices) if (Gui.Selectable(choice, choice == value)) vm.SourceConversionOptions[option.Id] = choice; Gui.EndCombo(); } }
            else
            {
                Gui.TextUnformatted(option.DisplayName); Gui.SetNextItemWidth(-1);
                if (Gui.InputTextWithHint("###value", option.Id == "blenderExecutable" ? "Vide : détecter Blender installé automatiquement" : "", ref value, 4096)) vm.SourceConversionOptions[option.Id] = value;
            }
            if (Gui.IsItemHovered()) Gui.SetTooltip(option.Description);
            Record("source-conversion/option/" + option.Id); Gui.PopID();
        }
        if (!selected.Descriptor.RequiresTargetEngine)
        {
            var unreal = vm.SourceConversionToUnreal;
            if (Gui.Checkbox("Convertir ensuite en pack UAsset avec Unreal", ref unreal)) vm.SourceConversionToUnreal = unreal;
            Record("source-conversion/to-unreal");
        }
        if (selected.Descriptor.RequiresTargetEngine || vm.SourceConversionToUnreal)
        {
            var engine = vm.Engines.FirstOrDefault(item => item.Engine.Id == vm.SourceConversionEngineId);
            if (Gui.BeginCombo("Moteur cible", engine?.DisplayName ?? "Choisir un moteur installé"))
            {
                foreach (var item in vm.Engines)
                    if (Gui.Selectable(item.DisplayName + " · " + item.Engine.Version + "###source-engine-" + item.Engine.Id)) vm.SourceConversionEngineId = item.Engine.Id;
                Gui.EndCombo();
            }
            Record("source-conversion/engine");
            if (engine is not null) Gui.TextWrapped(engine.Engine.RootPath);
            Gui.TextWrapped("L’import Unreal travaille dans un projet temporaire et recharge les fichiers produits. Confirmez les limites de l’import RAW avant de poursuivre.");
        }
        Gui.Separator();
        if (Gui.Button("Créer le dérivé local")) _actions.Enqueue(() => _ = vm.ConvertLocalSourceAsync());
        Record("source-conversion/run"); Gui.SameLine();
        if (Gui.Button("Fermer")) { vm.IsSourceConversionVisible = false; Gui.CloseCurrentPopup(); }
        Record("source-conversion/close"); Gui.EndDisabled();
        Gui.TextWrapped(vm.SourceConversionStatus);
        if (!wasEditing && !vm.IsSourceConversionBusy && Gui.IsKeyPressed(ImGuiKey.Escape)) { vm.IsSourceConversionVisible = false; Gui.CloseCurrentPopup(); }
        Gui.EndPopup();
    }
}
