using CyEngineHub.Desktop.ViewModels;
using Hexa.NET.ImGui;
using Gui = Hexa.NET.ImGui.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed unsafe partial class ImGuiWorkspace
{
    private void DrawProductActions(MainWindowViewModel vm)
    {
        if (vm.SelectedRemoteFabAssetCard is not { } asset) return;
        var columns = Gui.GetContentRegionAvail().X > CyUiNative.Dp(420) &&
            Gui.BeginTable("product-action-groups", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings);
        if (columns) Gui.TableNextColumn();
        Gui.BeginChild("product-acquisition", new(0, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY, ImGuiWindowFlags.NoScrollbar);
        Gui.TextDisabled("UTILISER CE PRODUIT");
        Gui.BeginDisabled(!asset.CanDownload);
        ActionButton(asset, "Télécharger les versions…", "DownloadCommand", true, -1);
        Record("product-action/download"); Gui.EndDisabled();
        Gui.BeginDisabled(!asset.CanInstall);
        ActionButton(vm, "Installer dans un projet…", "OpenFabInstallOptionsCommand", true, -1);
        Record("product-action/install"); Gui.EndDisabled();
        Gui.BeginDisabled(!vm.HasSelectedRemoteFabListing);
        ActionButton(vm, "Ouvrir la page web", "OpenSelectedRemoteFabListingCommand", width: -1);
        Record("product-action/web"); Gui.EndDisabled();
        Gui.EndChild(); Record("product-action-group/acquisition");
        if (columns) Gui.TableNextColumn();
        Gui.BeginChild("product-organization", new(0, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY, ImGuiWindowFlags.NoScrollbar);
        Gui.TextDisabled("MA BIBLIOTHÈQUE");
        ActionButton(asset, "Collections…", "OpenCollectionsCommand", width: -1);
        Record("product-action/collections");
        ActionButton(vm, asset.IsHidden ? "Réafficher ce produit" : "Masquer ce produit", "HideSelectedFabAssetCommand", width: -1);
        Record("product-action/visibility");
        if (Gui.IsItemHovered()) Gui.SetTooltip(vm.SelectedFabAssetVisibilityLabel);
        Gui.TextWrapped("Masquer ne supprime aucun fichier téléchargé.");
        Gui.EndChild(); Record("product-action-group/organization");
        if (columns) Gui.EndTable();
    }
}
