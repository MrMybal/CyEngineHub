using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Unreal.Models;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private void DrawEngineGit(MainWindowViewModel vm, EngineCardViewModel card)
    {
        Gui.Spacing(); Gui.Separator(); Gui.TextUnformatted("DÉPÔT GIT DU MOTEUR");
        DetailActions((vm, "Lire la branche locale", "InspectEngineGitCommand", false),
            (vm, "Vérifier les mises à jour Git", "CheckEngineGitUpdatesCommand", false));
        Gui.TextDisabled("La vérification distante fait un fetch, jamais un pull. Aucune source n’est modifiée.");
        if (card.EngineGitStatus is { } git)
        {
            var status = git.Status;
            Gui.TextWrapped("Branche actuelle : " + status.Branch + " · commit " + status.HeadCommit[..Math.Min(status.HeadCommit.Length, 12)]);
            Gui.TextWrapped("Suivi : " + (status.Upstream ?? "aucun — configurez une branche de suivi dans Git"));
            if (status.Upstream is not null)
            {
                var verified = git.RemoteChecked && string.IsNullOrEmpty(card.EngineGitMessage);
                var color = verified && status.Behind == 0 && status.Ahead == 0 ? ImGuiTheme.Success : ImGuiTheme.Warning;
                Gui.TextColored(color, $"{status.Behind} commit(s) à récupérer · {status.Ahead} commit(s) locaux" +
                    (verified ? " · suivi distant vérifié" : " · références locales, état distant non vérifié"));
                if (status.Behind > 0 && status.Ahead > 0) Gui.TextColored(ImGuiTheme.Warning, "Branches divergentes — intégration manuelle nécessaire.");
            }
            Gui.TextWrapped(status.WorkingTree.IsDirty ? "Modifications locales suivies détectées (non écrasées)." : "Aucune modification de fichier suivi ; fichiers non suivis non analysés.");
            Gui.TextDisabled("Dernier contrôle : " + git.CheckedAt.ToString("g", System.Globalization.CultureInfo.CurrentCulture));
        }
        if (!string.IsNullOrEmpty(card.EngineGitMessage)) Gui.TextWrapped(card.EngineGitMessage);
        Record("engine-details/git");
    }
    private void DetailActions(params (object Model, string Label, string Command, bool Primary)[] actions)
    {
        var right = Gui.GetCursorScreenPos().X + Gui.GetContentRegionAvail().X;
        for (var index = 0; index < actions.Length; index++)
        {
            var action = actions[index];
            if (index > 0 && Gui.GetItemRectMax().X + Gui.GetStyle().ItemSpacing.X + Gui.CalcTextSize(action.Label).X + 24 <= right) Gui.SameLine();
            ActionButton(action.Model, action.Label, action.Command, action.Primary);
        }
    }
    private void DrawEngineDetails(MainWindowViewModel vm, UiNode page)
    {
        var unreal = vm.SelectedEngineDetails;
        object? engine = (object?)unreal ?? vm.SelectedLocalEngineDetails;
        if (engine is null) { Gui.TextWrapped("Sélectionnez un moteur dans la liste."); return; }
        if (_focusCompilation) { vm.EngineDetailsTab = 2; _focusCompilation = false; }
        Gui.TextUnformatted(unreal?.DisplayName ?? Value(engine, "Name")); Gui.SameLine();
        Gui.TextColored(ImGuiTheme.Accent, unreal?.VersionLabel ?? Value(engine, "Version"));
        if (unreal is not null)
        {
            Gui.SameLine(); Gui.TextColored(unreal.Engine.Health == EngineHealth.Healthy ? ImGuiTheme.Success : ImGuiTheme.Warning,
                unreal.Engine.Health == EngineHealth.Healthy ? "Disponible" : unreal.HealthLabel);
            if (unreal.IsOfficialEpic) { Gui.SameLine(); Gui.TextColored(ImGuiTheme.Success, "OFFICIEL EPIC"); }
        }
        Gui.TextDisabled(Elide(Value(engine, "Path"), Gui.GetContentRegionAvail().X));
        if (Gui.IsItemHovered()) TextTooltip(Value(engine, "Path"));
        Gui.Spacing();
        var tabs = new[] { "Général", "Options d’installation", "Compilation / cross-compilation" };
        for (var index = 0; index < tabs.Length; index++)
        {
            if (index > 0 && Gui.GetContentRegionAvail().X > Gui.CalcTextSize(tabs[index]).X + 24) Gui.SameLine();
            var selected = vm.EngineDetailsTab == index;
            if (selected) Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Selected);
            var clicked = Gui.Button(tabs[index]);
            Record("engine-detail-tab/" + index);
            if (selected) Gui.PopStyleColor();
            if (clicked) vm.EngineDetailsTab = index;
        }
        Gui.Separator();
        Gui.BeginChild("engine-detail-content", new(0, 0), ImGuiChildFlags.None);
        if (vm.EngineDetailsTab == 0)
        {
            DetailActions((engine, unreal is null ? "Lancer" : "Lancer · accueil Unreal", "LaunchCommand", true),
                (engine, "Ouvrir le dossier", "OpenFolderCommand", false), (vm, "Analyser la taille", "AnalyzeSelectedEngineSizeCommand", false),
                (vm, "Renommer…", "StartEngineRenameCommand", false));
            if (vm.IsEngineNameEditing)
            {
                Gui.TextWrapped("Nom dans CyEngineHub uniquement — laissez vide pour rétablir le nom d’origine.");
                var name = vm.EngineNameDraft; Gui.SetNextItemWidth(Math.Min(480 * UiScale, Gui.GetContentRegionAvail().X));
                if (Gui.InputTextWithHint("###engine-name", "Nom personnalisé", ref name, 512)) vm.EngineNameDraft = name;
                Record("field/EngineNameDraft");
                DetailActions((vm, "Enregistrer le nom", "SaveEngineNameCommand", false), (vm, "Annuler", "CancelEngineRenameCommand", false));
            }
            Gui.Spacing(); Gui.Separator();
            if (Gui.BeginTable("engine-general-info", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                Gui.TableSetupColumn("Info", ImGuiTableColumnFlags.WidthStretch, 1);
                Gui.TableSetupColumn("Valeur", ImGuiTableColumnFlags.WidthStretch, 4);
                void Info(string label, string text)
                { Gui.TableNextRow(); Gui.TableNextColumn(); Gui.TextDisabled(label); Gui.TableNextColumn(); Gui.TextWrapped(text); }
                Info("Type", unreal?.TypeLabel ?? Value(engine, "Family"));
                Info("Branche de compilation", unreal?.BranchLabel ?? Value(engine, "Source"));
                Info("Taille", Value(engine, "EffectiveSizeLabel"));
                if (unreal is not null) Info("Association", unreal.RegistrationLabel);
                Gui.EndTable();
            }
            if (unreal is { IsOfficialEpic: false })
            {
                ActionButton(vm, "Modifier l’association Unreal…", "StartEngineAssociationCommand");
                if (vm.IsEngineAssociationEditing)
                {
                    Gui.TextWrapped("Valeur du registre utilisateur : HKCU\\SOFTWARE\\Epic Games\\Unreal Engine\\Builds");
                    Gui.TextWrapped("Dossier associé : " + unreal.Path);
                    var association = vm.EngineAssociationDraft; Gui.SetNextItemWidth(Math.Min(480 * UiScale, Gui.GetContentRegionAvail().X));
                    if (Gui.InputTextWithHint("###engine-association", "Identifiant personnalisé ou GUID", ref association, 512)) vm.EngineAssociationDraft = association;
                    Record("field/EngineAssociationDraft");
                    Gui.TextColored(ImGuiTheme.Warning, "Les anciens identifiants de ce moteur seront remplacés.");
                    Gui.TextWrapped("Les .uproject ne seront pas modifiés : ceux qui utilisent l’ancienne association devront être réassociés. Aucun identifiant d’un autre moteur ne sera écrasé.");
                    if (!vm.EngineAssociationConfirm)
                        DetailActions((vm, "Préparer la modification", "PrepareEngineAssociationCommand", false), (vm, "Fermer", "CloseEngineAssociationCommand", false));
                    else
                    {
                        Gui.TextWrapped("Confirmer l’association « " + vm.EngineAssociationDraft.Trim() + " » vers « " + unreal.Path + " » ?");
                        DetailActions((vm, "Confirmer l’écriture au registre", "ApplyEngineAssociationCommand", false), (vm, "Annuler", "CancelEngineAssociationCommand", false));
                    }
                }
            }
            if (!string.IsNullOrEmpty(vm.EngineIdentityStatus)) Gui.TextWrapped(vm.EngineIdentityStatus);
            if (unreal is not null)
            {
                Gui.Spacing();
                Gui.TextColored(unreal.Engine.Health == EngineHealth.Healthy ? ImGuiTheme.Success : ImGuiTheme.Warning, unreal.IssueSummary);
                if (unreal.IsOfficialEpic)
                {
                    Gui.TextColored(unreal.OfficialUpdateAvailable ? ImGuiTheme.Warning : ImGuiTheme.Muted, unreal.OfficialUpdateLabel);
                    ActionButton(vm, "Vérifier la révision officielle", "CheckOfficialEngineUpdatesCommand");
                }
                DetailActions((vm, "Plugins du moteur", "ShowSelectedEnginePluginsCommand", false),
                    (vm, "Outils / prérequis", "ShowToolchainsCommand", false));
                if (unreal.CanRegister) ActionButton(unreal, "Enregistrer le moteur", "RegisterCommand");
                if (unreal.IsRegistered) ActionButton(unreal, "Désenregistrer…", "RequestUnregisterCommand");
                if (unreal.IsUnregisterConfirmationVisible)
                {
                    Gui.TextWrapped("Supprimer les associations utilisateur de ce moteur ? Les fichiers et projets seront conservés.");
                    DetailActions((unreal, "Confirmer le désenregistrement", "ConfirmUnregisterCommand", false),
                        (unreal, "Annuler", "CancelUnregisterCommand", false));
                }
                if (!unreal.IsOfficialEpic) DrawEngineGit(vm, unreal);
            }
            Gui.Spacing();
            if (unreal is null)
                DetailActions((vm, "Plugins du moteur", "ShowSelectedEnginePluginsCommand", false),
                    (vm, "Outils / prérequis", "ShowToolchainsCommand", false));
            if (Gui.CollapsingHeader("Gestion du catalogue / désinstallation"))
                DetailActions((vm, "Retirer du catalogue…", "RequestRemoveSelectedEngineCommand", false),
                    (vm, "Désinstaller…", "RequestUninstallSelectedEngineCommand", false));
            Record("engine-details/general");
        }
        else if (vm.EngineDetailsTab == 1)
        {
            if (unreal is not null)
            {
                if (unreal.CanSetup) ActionButton(unreal, "Préparer les dépendances (Setup)", "SetupCommand");
                DrawNode(page.Descendants().Single(node => node["Name"] == "EngineInstallationOptions"), vm);
            }
            else
            {
                Gui.TextWrapped("Les composants de cette installation se gèrent avec les outils de son fournisseur. Les prérequis locaux restent accessibles dans Outils.");
                var local = page.Descendants().Single(node => UiBinding.Path(node["IsVisible"]) == "IsLocalEngineDetailsSelected");
                DrawNode(local.Descendants().Single(node => node.Kind == "Border" && node["Grid.Column"] == "1"), vm);
            }
            Record("engine-details/installation");
        }
        else
        {
            if (unreal is not null)
            {
                DetailActions((unreal, "Compiler l’éditeur", "BuildEditorCommand", true),
                    (unreal, "Générer les fichiers de projet", "GenerateProjectFilesCommand", false),
                    (vm, "Voir les opérations", "ShowActionsCommand", false));
                ActionButton(vm, "Actualiser les prérequis", "RefreshToolchainsCommand");
                Gui.TextDisabled("Un outil détecté peut être d’une autre version. Requis indique la version attendue par ce moteur.");
            }
            DrawNode(page.Descendants().Single(node => node["Name"] == "EngineCompilationPanel"), vm);
            Record("engine-details/compilation");
        }
        Gui.EndChild(); Record("pane/engine-details/content");
    }
}
