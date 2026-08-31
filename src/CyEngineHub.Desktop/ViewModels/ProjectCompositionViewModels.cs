using System.Windows.Input;
using CyEngineHub.Projects.Creation;

namespace CyEngineHub.Desktop.ViewModels;

public sealed class ProjectFeaturePackOptionViewModel : ObservableObject
{
    private bool _isSelected;
    private int _order;
    public ProjectFeaturePackOptionViewModel(ProjectFeaturePack pack, Action changed,
        Action<ProjectFeaturePackOptionViewModel> edit, Action<ProjectFeaturePackOptionViewModel, int> move)
    {
        Pack = pack;
        EditCommand = new RelayCommand(() => edit(this));
        MoveUpCommand = new RelayCommand(() => move(this, -1));
        MoveDownCommand = new RelayCommand(() => move(this, 1));
        _changed = changed;
    }
    private readonly Action _changed;
    public ProjectFeaturePack Pack { get; }
    public string Name => Pack.DisplayName;
    public string Description => Pack.Description;
    public string Detail => $"{(Pack.IsBuiltIn ? "Intégré · duplicable" : "Personnalisé")} · +{Pack.EnabledPlugins.Count} / −{Pack.DisabledPlugins.Count} plugins · {Pack.IniSettings.Count} réglages INI";
    public bool IsSelected { get => _isSelected; set { if (SetProperty(ref _isSelected, value)) _changed(); } }
    public int Order { get => _order; set => SetProperty(ref _order, value); }
    public ICommand EditCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
}

public sealed class ProjectIniDocumentViewModel : ObservableObject
{
    private string _content;
    private bool _isEdited;
    private string _error = string.Empty;
    public ProjectIniDocumentViewModel(string fileName, string content, Action changed, bool isEdited = false)
    {
        FileName = fileName;
        _content = content;
        _changed = changed;
        _isEdited = isEdited;
        Validate();
    }
    private readonly Action _changed;
    public string FileName { get; }
    public string Content
    {
        get => _content;
        set
        {
            if (!SetProperty(ref _content, value)) return;
            IsEdited = true;
            Validate();
            _changed();
        }
    }
    public bool IsEdited { get => _isEdited; private set { if (SetProperty(ref _isEdited, value)) OnPropertyChanged(nameof(Origin)); } }
    public string Origin => IsEdited ? "Modification manuelle · prioritaire sur profil et packs" : "Généré depuis le profil et les packs";
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public void Validate()
    {
        try { _ = UnrealProjectIni.Parse(FileName, Content); Error = string.Empty; }
        catch (ArgumentException exception) { Error = exception.Message; }
    }
    public void UpdateGenerated(string content)
    {
        if (IsEdited) return;
        _content = content;
        OnPropertyChanged(nameof(Content));
        Validate();
    }
}
