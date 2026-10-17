using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private void DrawProductActions(MainWindowViewModel vm)
    {
        if (vm.SelectedRemoteFabAssetCard is not { } asset) return;
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
        Gui.BeginChild("product-organization", new(0, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY, ImGuiWindowFlags.NoScrollbar);
        Gui.TextDisabled("MA BIBLIOTHÈQUE");
        ActionButton(asset, "Collections…", "OpenCollectionsCommand", width: -1);
        Record("product-action/collections");
        ActionButton(vm, asset.IsHidden ? "Réafficher ce produit" : "Masquer ce produit", "HideSelectedFabAssetCommand", width: -1);
        Record("product-action/visibility");
        if (Gui.IsItemHovered()) Gui.SetTooltip(vm.SelectedFabAssetVisibilityLabel);
        Gui.TextWrapped("Masquer ne supprime aucun fichier téléchargé.");
        Gui.EndChild(); Record("product-action-group/organization");
    }
}
