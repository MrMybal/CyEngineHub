using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private string _settingsMarket = "Fab";
    private string _settingsUnreal = "profiles";
    private static readonly string[] ConnectionSettingsProviders = ["Epic", "Fab", "Git", "Quixel", "Unity"];

    private void SettingsTab(string scope, string id, string label, bool selected, Action activate)
    {
        var width = Gui.CalcTextSize(label).X + Gui.GetStyle().FramePadding.X * 2;
        if (Gui.GetContentRegionAvail().X < width) Gui.NewLine();
        if (selected)
        {
            Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Selected);
            Gui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiTheme.Selected);
            Gui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiTheme.Selected);
        }
        if (Gui.Button(label + "###" + scope + id)) _actions.Enqueue(activate);
        Record("settings-subtab/" + scope + "/" + id);
        if (selected) { Record("settings-subtab-selected/" + scope + "/" + id); Gui.PopStyleColor(3); }
        Gui.SameLine();
    }
    private void SettingNumber(MainWindowViewModel vm, string property, string label, int value, int min, int max)
    {
        Gui.TextWrapped(label); Gui.SetNextItemWidth(150 * UiScale);
        if (Gui.InputInt("###" + property, ref value)) Set(vm, "{Binding " + property + "}", Math.Clamp(value, min, max));
        Record("field/" + property);
    }
    private static UiNode? SettingsPanel(UiNode page, string property) => page.Descendants()
        .FirstOrDefault(n => n["IsVisible"] == "{Binding " + property + "}");

    private void MarketplaceSettingsFooter(object model, string label, string command, string status, bool openInventory = false)
    {
        ActionButton(model, label, command, true);
        Record("marketplace-settings/save");
        if (openInventory)
        {
            const string openLabel = "Ouvrir l’inventaire dans CyEngineHub";
            var required = Gui.GetItemRectSize().X + Gui.GetStyle().ItemSpacing.X + Gui.CalcTextSize(openLabel).X + Gui.GetStyle().FramePadding.X * 2;
            if (Gui.GetContentRegionAvail().X >= required) Gui.SameLine();
            ActionButton(model, openLabel, "OpenInventoryCommand");
            Record("marketplace-settings/open-inventory");
        }
        Gui.TextWrapped("Chemins, quotas et ordre des bibliothèques : sauvegarde automatique après modification.");
        Gui.TextWrapped(status);
    }

    private bool DrawOrganizedSettings(MainWindowViewModel vm, UiNode page)
    {
        if (vm.IsSettingsAppearanceSelected)
        {
            Gui.TextUnformatted("APPARENCE"); Gui.Separator();
            Gui.TextWrapped("Thème de l’application — aperçu immédiat");
            var themes = MainWindowViewModel.InterfaceThemes.ToArray(); var selected = Array.IndexOf(themes, vm.InterfaceTheme);
            Gui.SetNextItemWidth(220 * UiScale);
            if (Gui.Combo("###interface-theme", ref selected, themes, themes.Length)) Set(vm, "{Binding InterfaceTheme}", themes[selected]);
            Record("field/InterfaceTheme");
            var density = vm.UseCompactInterface ? 0 : 1;
            Gui.SetNextItemWidth(220 * UiScale);
            if (Gui.Combo("Densité de l’interface", ref density, "Compact\0Confort\0")) Set(vm, "{Binding UseCompactInterface}", density == 0);
            Record("interface-density");
            ActionButton(vm, "Enregistrer l’apparence", "SaveAppearanceSettingsCommand", true);
            Gui.TextWrapped(vm.AppearanceStatus);
            return true;
        }
        if (vm.IsSettingsConnectionsSelected)
        {
            Gui.TextUnformatted("CONNEXIONS");
            foreach (var provider in ConnectionSettingsProviders)
            {
                var current = provider;
                SettingsTab("connections", provider, provider == "Quixel" ? "Megascans" : provider,
                    Flag(vm, "Is" + provider + "ConnectionPageSelected"), () =>
                    {
                        if (UiBinding.Read(vm, "{Binding Show" + current + "ConnectionSettingsCommand}") is System.Windows.Input.ICommand command) command.Execute(null);
                    });
            }
            Gui.NewLine(); Gui.Separator();
            var selected = ConnectionSettingsProviders.First(p => Flag(vm, "Is" + p + "ConnectionPageSelected"));
            var module = vm.ConnectionModules.FirstOrDefault(m => m.Id.Equals(selected, StringComparison.OrdinalIgnoreCase));
            if (module is not null)
            {
                Gui.TextWrapped(module.IsEnabled ? "Service activé — ceci ne signifie pas que le compte est connecté." : "Service désactivé — les données locales sont conservées.");
                ActionButton(module, module.ToggleLabel + " le service", "ToggleEnabledCommand");
                Gui.Separator();
            }
            var panel = SettingsPanel(page, "Is" + selected + "ConnectionPageSelected");
            if (panel is not null) DrawNode(panel, vm);
            return true;
        }
        if (vm.IsSettingsMarketplacesSelected)
        {
            Gui.TextUnformatted("MARKETPLACES");
            foreach (var market in new[] { "Fab", "Megascans", "Unity" })
            { var current = market; SettingsTab("marketplaces", market, market, _settingsMarket == market, () => _settingsMarket = current); }
            foreach (var connector in vm.MarketplaceConnectors)
            { var current = connector; SettingsTab("marketplaces", connector.Id, connector.Name, _settingsMarket == connector.Id, () => _settingsMarket = current.Id); }
            Gui.NewLine(); Gui.Separator();
            var panel = SettingsPanel(page, "IsSettingsMarketplacesSelected");
            var heading = _settingsMarket switch { "Fab" => "FAB", "Megascans" => "MEGASCANS", "Unity" => "UNITY ASSET STORE", _ => "" };
            if (heading.Length > 0)
            {
                var card = panel?.Children.FirstOrDefault(n => n.Kind == "Border" && n.Descendants().Any(c => c["Text"] == heading));
                if (_settingsMarket is "Fab" or "Megascans")
                {
                    var fab = _settingsMarket == "Fab";
                    SettingNumber(vm, fab ? "FabDownloadConnections" : "QuixelDownloadConnections", "Connexions simultanées par téléchargement (1–32)", fab ? vm.FabDownloadConnections : vm.QuixelDownloadConnections, 1, 32);
                    Gui.TextWrapped("La limite globale de packs simultanés et la reprise automatique se règlent dans Téléchargements. Les transferts actifs ne sont pas interrompus.");
                    ActionButton(vm, "Enregistrer les téléchargements", "SaveDownloadQueueSettingsCommand", true);
                    Gui.TextWrapped(vm.DownloadQueueStatus); Gui.Separator();
                }
                else Gui.TextWrapped("Le téléchargement authentifié Unity n’est pas encore disponible. Aucun réglage de connexion fictif n’est appliqué.");
                if (card is not null) DrawNode(card, vm);
                MarketplaceSettingsFooter(vm, "Enregistrer " + _settingsMarket,
                    _settingsMarket == "Fab" ? "SaveFabMarketplacePanelCommand" : _settingsMarket == "Megascans" ? "SaveQuixelMarketplacePanelCommand" : "SaveUnityMarketplacePanelCommand",
                    vm.MarketplaceStorageSaveStatus);
                Gui.TextWrapped(vm.StatusMessage);
            }
            else
            {
                var connector = vm.MarketplaceConnectors.FirstOrDefault(c => c.Id == _settingsMarket);
                var template = panel?.Descendants().FirstOrDefault(n => n.Kind == "ItemsControl" && n["ItemsSource"] == "{Binding MarketplaceConnectors}")?.Descendants().FirstOrDefault(n => n.Kind == "DataTemplate");
                if (connector is not null && template is not null)
                {
                    Gui.TextWrapped("Téléchargements via le navigateur intégré. Le réglage de connexions Fab / Megascans ne s’applique pas à ce connecteur.");
                    DrawChildren(template, connector);
                    MarketplaceSettingsFooter(connector, connector.SettingsSaveLabel, "SaveCommand", connector.SaveStatus, openInventory: true);
                    if (connector.Id == "cyenginehub.marketplace-cosmos") DrawCosmosImportOptions(vm);
                }
            }
            return true;
        }
        if (vm.IsSettingsProjectProfilesSelected)
        {
            SettingsTab("unreal", "profiles", "Profils de projet", _settingsUnreal == "profiles", () => _settingsUnreal = "profiles");
            SettingsTab("unreal", "downloads", "Téléchargement des moteurs", _settingsUnreal == "downloads", () => _settingsUnreal = "downloads");
            Gui.NewLine(); Gui.Separator();
            if (_settingsUnreal == "downloads")
            {
                Gui.TextUnformatted("UNREAL ENGINE · TÉLÉCHARGEMENTS");
                SettingNumber(vm, "EngineChunkConcurrency", "Connexions de chunks par moteur (1–32)", vm.EngineChunkConcurrency, 1, 32);
                ActionButton(vm, "Enregistrer", "SaveDownloadQueueSettingsCommand", true);
                Gui.TextWrapped(vm.DownloadQueueStatus);
            }
            else
            {
                var panel = SettingsPanel(page, "IsSettingsProjectProfilesSelected");
                if (panel is not null) DrawNode(panel, vm);
                if (vm.SelectedManagedProjectProfile is { } selected)
                {
                    Gui.Separator(); Gui.TextUnformatted("CONTENU DU PROFIL · " + selected.Name);
                    Gui.TextWrapped("Activer : " + string.Join(", ", selected.Profile.EnabledPlugins));
                    Gui.TextWrapped("Désactiver : " + string.Join(", ", selected.Profile.DisabledPlugins));
                    foreach (var setting in selected.Profile.EngineSettings) Gui.TextWrapped("DefaultEngine.ini · /Script/Engine.RendererSettings · " + setting.Key + " = " + setting.Value);
                    foreach (var setting in selected.Profile.IniSettings) Gui.TextWrapped(setting.FileName + " · " + setting.Section + " · " + setting.Key + " = " + setting.Value);
                    Record("settings/profile-preview");
                }
            }
            return true;
        }
        if (vm.IsSettingsDownloadsSelected)
        {
            Gui.TextUnformatted("TÉLÉCHARGEMENTS · FILE GLOBALE");
            SettingNumber(vm, "DownloadConcurrency", "Nombre maximal de packs simultanés (Fab et Megascans, limite partagée)", vm.DownloadConcurrency, 1, 16);
            var resume = vm.ResumeDownloadsOnStartup;
            if (Gui.Checkbox("Reprendre au lancement", ref resume)) Set(vm, "{Binding ResumeDownloadsOnStartup}", resume);
            Record("check/ResumeDownloadsOnStartup");
            Gui.TextWrapped("Réglages de connexion : Marketplaces → Fab / Megascans. Pour les moteurs : Unreal Engine → Téléchargement des moteurs.");
            ActionButton(vm, "Enregistrer", "SaveDownloadQueueSettingsCommand", true);
            ActionButton(vm, "Reprendre / réessayer les échecs", "ResumeDownloadQueueCommand");
            Gui.TextWrapped(vm.DownloadQueueStatus);
            var path = vm.DownloadRecoveryPath; Gui.SetNextItemWidth(-1);
            if (Gui.InputTextWithHint("###recovery-path", "Fichier de sauvegarde de la file (JSON)", ref path, 4096)) Set(vm, "{Binding DownloadRecoveryPath}", path);
            ActionButton(vm, "Importer la sauvegarde sans lancer", "ImportDownloadRecoveryCommand");
            return true;
        }
        return false;
    }
}
