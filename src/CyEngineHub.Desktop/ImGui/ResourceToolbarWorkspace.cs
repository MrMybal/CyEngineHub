using System.Numerics;
using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private void DrawResourceDisplayToolbar(MainWindowViewModel vm, bool engines, int mode, int count)
    {
        var padding = Gui.GetStyle().WindowPadding.X * 2 + 8 * UiScale;
        var scope = engines ? "engines" : "projects";
        // One explicit row owns both frames. A wrapping action toolbar or a long
        // status/counter must never put the favorite filter on a separate line.
        Gui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(0, 0));
        if (!Gui.BeginTable("resource-display-row/" + scope, 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        { Gui.PopStyleVar(); return; }
        Gui.TableSetupColumn("Affichage", ImGuiTableColumnFlags.WidthFixed, ViewModeWidth() + padding + Gui.GetStyle().ItemSpacing.X);
        Gui.TableSetupColumn("Favoris", ImGuiTableColumnFlags.WidthFixed, ViewModeButtonSize() + padding + Gui.GetStyle().ItemSpacing.X);
        Gui.TableSetupColumn("Résultats", ImGuiTableColumnFlags.WidthStretch);
        Gui.TableNextColumn();
        var first = true;
        var right = Gui.GetCursorScreenPos().X + Gui.GetContentRegionAvail().X;
        ToolbarGroup("resource-display/" + scope, ref first, right, ViewModeWidth() + padding, () =>
        {
            foreach (var view in new[] { (Label: "Cartes", Mode: 0), (Label: "Image + nom", Mode: 1), (Label: "Lignes", Mode: 2), (Label: "Lignes + image", Mode: 3) })
            {
                if (view.Mode > 0) Gui.SameLine();
                ViewModeIconButton((engines ? "engines/" : "projects/") + view.Mode, view.Mode, mode == view.Mode,
                    () => vm.SetResourcePresentation(engines, view.Mode));
                Record("resource-view/" + view.Label);
                if (view.Mode == 2) Record("resource-view/Liste");
                if (mode == view.Mode) Record("resource-view-selected/" + view.Mode);
            }
        });
        Gui.TableNextColumn();
        first = true; right = Gui.GetCursorScreenPos().X + Gui.GetContentRegionAvail().X;
        DrawFavoriteGroup(vm, ref first, right);
        var frameMin = Gui.GetItemRectMin(); var frameMax = Gui.GetItemRectMax();
        Gui.TableNextColumn();
        var selected = engines ? _selectedEnginePaths.Count : _itemSelection.Keys.Count;
        var label = count + (engines ? " moteur(s)" : " projet(s)") + (selected > 0 ? $" · {selected} sélectionné(s)" : "");
        Gui.SetCursorScreenPos(new Vector2(Gui.GetCursorScreenPos().X, frameMin.Y + (frameMax.Y - frameMin.Y - Gui.GetTextLineHeight()) / 2));
        Gui.TextDisabled(Elide(label, Math.Max(1, Gui.GetContentRegionAvail().X)));
        Record("resource-counter");
        if (Gui.IsItemHovered()) Gui.SetTooltip("Ctrl + clic : ajouter ou retirer · Maj + clic : sélectionner une plage · clic droit : actions");
        Gui.EndTable(); Gui.PopStyleVar();
        Record("resource-toolbar-row/" + scope);
    }
}
