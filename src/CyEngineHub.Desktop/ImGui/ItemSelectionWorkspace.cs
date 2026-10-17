using CyEngineHub.Desktop.ViewModels;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private UiMultiSelection _itemSelection = new();
    private readonly Dictionary<string, UiMultiSelection> _itemSelections = new(StringComparer.Ordinal);
    private Dictionary<string, object> _selectionModels = new(StringComparer.OrdinalIgnoreCase);
    private void SynchronizeItemSelection(string scope, IEnumerable<object> items)
    {
        if (!_itemSelections.TryGetValue(scope, out var selection)) _itemSelections.Add(scope, selection = new UiMultiSelection());
        _itemSelection = selection;
        _selectionModels = items.DistinctBy(MainWindowViewModel.ResourceIdentity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(MainWindowViewModel.ResourceIdentity, item => item, StringComparer.OrdinalIgnoreCase);
        _itemSelection.Synchronize(scope, _selectionModels.Keys);
        Record("selection/count/" + _itemSelection.Keys.Count);
    }
    private string LibrarySelectionScope(string binding) => "library/" + (_root as MainWindowViewModel)?.LibrarySource + "/" + binding;
    private bool IsItemSelected(object item) => _itemSelection.Contains(MainWindowViewModel.ResourceIdentity(item));
    private void SelectItem(object item, bool rightClick = false)
    {
        var io = Gui.GetIO();
        _itemSelection.Select(MainWindowViewModel.ResourceIdentity(item), io.KeyCtrl, io.KeyShift, rightClick);
    }
    private object[] SelectedContextItems() => _itemSelection.Keys.Where(_selectionModels.ContainsKey).Select(key => _selectionModels[key]).ToArray();
}
