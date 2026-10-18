using System.Windows.Input;
using CyEngineHub.Git.Analysis;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string _engineNameDraft = "", _engineAssociationDraft = "", _engineIdentityStatus = "";
    private bool _engineIdentityBusy, _engineAssociationConfirm;
    public bool IsEngineNameEditing { get; set; }
    public bool IsEngineAssociationEditing { get; set; }
    public bool EngineGitBusy { get; private set; }
    public ICommand StartEngineRenameCommand => new RelayCommand(() => IsEngineNameEditing = true);
    public ICommand CancelEngineRenameCommand => new RelayCommand(() => { EngineNameDraft = SelectedEngineDetails?.Organization.Alias ?? SelectedLocalEngineDetails?.Organization.Alias ?? ""; IsEngineNameEditing = false; });
    public ICommand StartEngineAssociationCommand => new RelayCommand(() => IsEngineAssociationEditing = true);
    public ICommand CloseEngineAssociationCommand => new RelayCommand(() => { IsEngineAssociationEditing = false; EngineAssociationConfirm = false; EngineAssociationDraft = SelectedEngineDetails?.Registration?.Identifier ?? ""; });
    private ICommand? _saveEngineName, _prepareEngineAssociation, _applyEngineAssociation, _cancelEngineAssociation, _inspectEngineGit, _checkEngineGit;
    public string EngineNameDraft { get => _engineNameDraft; set => SetProperty(ref _engineNameDraft, value); }
    public string EngineAssociationDraft { get => _engineAssociationDraft; set { if (SetProperty(ref _engineAssociationDraft, value)) EngineAssociationConfirm = false; } }
    public string EngineIdentityStatus { get => _engineIdentityStatus; private set => SetProperty(ref _engineIdentityStatus, value); }
    public bool EngineIdentityBusy { get => _engineIdentityBusy; private set => SetProperty(ref _engineIdentityBusy, value); }
    public bool EngineAssociationConfirm { get => _engineAssociationConfirm; private set => SetProperty(ref _engineAssociationConfirm, value); }
    public ICommand SaveEngineNameCommand => _saveEngineName ??= new AsyncRelayCommand(async () =>
    {
        var organization = SelectedEngineDetails?.Organization ?? SelectedLocalEngineDetails?.Organization;
        if (organization is null) return;
        var alias = EngineNameDraft.Trim();
        if (alias.Length > 128 || alias.Any(char.IsControl)) throw new ArgumentException("Le nom doit tenir sur une ligne (128 caractères maximum).");
        await SaveWorkspaceItemAsync(organization.Kind, organization.Key, alias, organization.FolderPath).ConfigureAwait(true);
        organization.Alias = alias;
        IsEngineNameEditing = false;
        OnPropertyChanged(nameof(SelectedEngineManagementName)); OnPropertyChanged(nameof(HeaderTitle));
        EngineIdentityStatus = "Nom enregistré dans CyEngineHub ; aucun dossier ni projet renommé.";
    }, () => !EngineIdentityBusy, ShowError);
    public ICommand PrepareEngineAssociationCommand => _prepareEngineAssociation ??= new RelayCommand(() => EngineAssociationConfirm = true,
        () => SelectedEngineDetails is { IsOfficialEpic: false } && !EngineIdentityBusy && !string.IsNullOrWhiteSpace(EngineAssociationDraft));
    public ICommand CancelEngineAssociationCommand => _cancelEngineAssociation ??= new RelayCommand(() => EngineAssociationConfirm = false);
    public ICommand ApplyEngineAssociationCommand => _applyEngineAssociation ??= new AsyncRelayCommand(async () =>
    {
        var card = SelectedEngineDetails;
        if (card is null || card.IsOfficialEpic || !EngineAssociationConfirm) return;
        var identifier = EngineAssociationDraft;
        EngineIdentityBusy = true;
        try
        {
            var registration = await Task.Run(() => _engineRegistration.SetAssociation(card.Path, identifier)).ConfigureAwait(true);
            card.UpdateRegistration(registration);
            // Keep the association immediately usable by project creation / resolution without a full catalog scan.
            _currentEngines = _currentEngines.Select(item => item.Id == card.Engine.Id ? item with { AssociationId = registration.Identifier } : item).ToArray();
            RebuildProjectCreationEngines();
            EngineAssociationConfirm = false;
            IsEngineAssociationEditing = false;
            EngineIdentityStatus = "Association utilisateur enregistrée. Les fichiers .uproject existants n’ont pas été modifiés.";
            try { await _engineCatalog.UpsertAsync(card.Engine).ConfigureAwait(true); }
            catch (Exception error)
            {
                EngineIdentityStatus += " Le cache local n’a pas pu être actualisé ; relancez une analyse des moteurs.";
                AppendRawLog("WARN", "Engines/Association", "Association enregistrée au registre, mise à jour du catalogue en échec : " + error.GetType().Name);
            }
        }
        finally { EngineIdentityBusy = false; }
    }, () => EngineAssociationConfirm && !EngineIdentityBusy, ShowError);
    public ICommand InspectEngineGitCommand => _inspectEngineGit ??= new AsyncRelayCommand(() => InspectSelectedEngineGitAsync(false), () => !EngineGitBusy, ShowError);
    public ICommand CheckEngineGitUpdatesCommand => _checkEngineGit ??= new AsyncRelayCommand(() => InspectSelectedEngineGitAsync(true), () => !EngineGitBusy, ShowError);

    private void ResetEngineIdentityEditor()
    {
        EngineNameDraft = SelectedEngineDetails?.Organization.Alias ?? SelectedLocalEngineDetails?.Organization.Alias ?? "";
        EngineAssociationDraft = SelectedEngineDetails?.Registration?.Identifier ?? "";
        EngineAssociationConfirm = false; EngineIdentityStatus = "";
        IsEngineNameEditing = false; IsEngineAssociationEditing = false;
    }
    private async Task InspectSelectedEngineGitAsync(bool fetch)
    {
        var card = SelectedEngineDetails;
        if (card is null || card.IsOfficialEpic || EngineGitBusy) return;
        EngineGitBusy = true;
        card.EngineGitMessage = fetch ? "Vérification distante en cours…" : "Lecture du dépôt local…";
        try
        {
            var result = await new EngineGitStatusService(_processRunner).InspectAsync(card.Path, fetch).ConfigureAwait(true);
            card.EngineGitStatus = result;
            card.EngineGitMessage = result is null ? "Aucun dépôt Git à la racine de ce moteur." : "";
        }
        catch (Exception error)
        {
            card.EngineGitMessage = error is OperationCanceledException ? "Vérification interrompue après le délai maximal ; état distant inconnu." : error.Message;
        }
        finally { EngineGitBusy = false; }
    }
}
