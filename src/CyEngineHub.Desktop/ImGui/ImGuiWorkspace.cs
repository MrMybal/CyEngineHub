using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace : IDisposable
{
    private readonly UiDocument _document;
    private readonly Dictionary<string, UiDocument> _panels = new(StringComparer.Ordinal);
    private readonly Func<Bitmap, nint> _image;
    private readonly Action<string>? _nativeAction;
    private readonly Queue<Action> _actions = new();
    private readonly HashSet<RemoteFabAssetRowViewModel> _realized = [];
    private readonly HashSet<RemoteFabAssetRowViewModel> _seen = [];
    private readonly HashSet<QuixelCollectionCardViewModel> _collections = [];
    private readonly HashSet<QuixelCollectionCardViewModel> _seenCollections = [];
    private readonly HashSet<string> _visited = [];
    private readonly Dictionary<string, UiListViewport> _lists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _tabSelections = new(StringComparer.Ordinal);
    private readonly bool _main;
    private Bitmap? _logo;
    private object? _root;
    private string _page = "";
    private string? _error;
    private bool _compact = true;
    private bool _previousCompact = true;
    private Vector2 _previousSize;
    private readonly HashSet<string> _scrollRequests = new(StringComparer.Ordinal);
    private readonly HashSet<string> _focusRequests = new(StringComparer.Ordinal);
    public Dictionary<string, int> VisibleRows { get; } = new(StringComparer.Ordinal);
    public void ScrollToEnd(string binding) => _scrollRequests.Add(binding);
    public void FocusField(string binding) => _focusRequests.Add(binding);

    public IReadOnlyCollection<string> VisitedNodes => _visited;
    internal float RenderedFontSize { get; private set; }

    public ImGuiWorkspace(string layout, Func<Bitmap, nint> image, Action<string>? nativeAction)
    {
        _document = UiDocument.Load(layout);
        _main = layout == "Main";
        _image = image;
        _nativeAction = nativeAction;
        if (_main)
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://CyEngineHub.Desktop/Assets/Branding/cyenginehub-logo.png"));
            _logo = new Bitmap(stream);
        }
    }

    public void Draw(object? model, Vector2 size)
    {
        _root = model;
        RenderedFontSize = Gui.GetFontSize();
        if (model is MainWindowViewModel preferences)
        {
            _compact = preferences.UseCompactInterface;
            // Other ImGui hosts have their own context; keep shared palette helpers
            // consistent with this host even after opening a sign-in/tool window.
            ImGuiTheme.ApplyPalette(preferences.InterfaceTheme);
        }
        VisibleRows.Clear();
        Elements.Clear();
        if (_previousSize != size || _previousCompact != _compact) _lists.Clear();
        _previousSize = size;
        _previousCompact = _compact;
        _seen.Clear();
        _seenCollections.Clear();
        var style = Gui.GetStyle();
        style.ItemSpacing = (_compact ? new Vector2(7, 4) : new Vector2(9, 8)) * UiCardGrid.InterfaceScale;
        style.FramePadding = (_compact ? new Vector2(7, 3) : new Vector2(10, 7)) * UiCardGrid.InterfaceScale;
        Gui.SetNextWindowPos(Vector2.Zero);
        Gui.SetNextWindowSize(size);
        Gui.Begin("CyEngineHub###hub", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus);
        if (_main) DrawStudioShell(size);
        else DrawChildren(_document.Root, model);
        Gui.End();
        DrawModals(size);
        foreach (var row in _realized.Where(row => !_seen.Contains(row)).ToArray())
        {
            row.Recycle();
            _realized.Remove(row);
        }
        foreach (var collection in _collections.Where(collection => !_seenCollections.Contains(collection)).ToArray())
        {
            collection.ReleaseThumbnail();
            _collections.Remove(collection);
        }
        while (_actions.TryDequeue(out var action))
        {
            try
            {
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
                else Avalonia.Threading.Dispatcher.UIThread.Post(action);
            }
            catch (Exception exception) { _error = exception.InnerException?.Message ?? exception.Message; }
        }
    }


    private void DrawModals(Vector2 size)
    {
        var active = _document.Modals.Where(node => Visible(node, _root)).ToArray();
        var activeIds = active.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var layers = active.Concat(_previousModals).DistinctBy(node => node.Id)
            .OrderBy(node => node.Number("Panel.ZIndex", 0)).ToArray();
        _previousModals = active;
        // Begin nested popups in order: an inner configuration really blocks its parent.
        DrawModalAt(0);
        void DrawModalAt(int index)
        {
            if (index >= layers.Length) return;
            var node = layers[index];
            var name = "###modal-" + node.Id;
            if (!activeIds.Contains(node.Id))
            {
                // A closed VM panel must also release ImGui's modal input capture.
                if (Gui.BeginPopupModal(name)) { Gui.CloseCurrentPopup(); Gui.EndPopup(); }
                DrawModalAt(index + 1);
                return;
            }
            if (!Gui.IsPopupOpen(name)) Gui.OpenPopup(name);
            Gui.SetNextWindowPos(size / 2, ImGuiCond.Always, new(.5f));
            var border = node.Children.LastOrDefault();
            var width = Math.Clamp(border?.Number("MaxWidth", border.Number("Width", 1000)) ?? 1000, 400, Math.Max(400, size.X - 64));
            Gui.SetNextWindowSize(new(width, Math.Min(size.Y - 64, border?.Number("MaxHeight", 700) ?? 700)), ImGuiCond.Always);
            if (Gui.BeginPopupModal(name, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                if (border is not null) DrawModalBody(border);
                if (index == layers.Length - 1 && Gui.IsKeyPressed(ImGuiKey.Escape))
                {
                    var cancel = node.Descendants().Select(child => child["Command"]).LastOrDefault(command =>
                        UiBinding.Path(command) is { } path && (path.StartsWith("Cancel", StringComparison.Ordinal) || path.StartsWith("Close", StringComparison.Ordinal)));
                    if (cancel is not null) Execute(_root, cancel);
                }
                DrawModalAt(index + 1);
                Gui.EndPopup();
            }
        }
    }

    private void DrawModalBody(UiNode border)
    {
        if (border["Name"] == "FabGalleryViewer" && _root is MainWindowViewModel gallery)
        { DrawFabGallery(gallery, enlarged: true); return; }
        var grid = border.Children.FirstOrDefault();
        var sections = grid?.Children.Where(node => !node.Kind.Contains('.', StringComparison.Ordinal)).ToArray() ?? [];
        var lastRow = sections.Length == 0 ? 0 : sections.Max(node => (int)node.Number("Grid.Row", 0));
        if (grid?.Kind == "Grid" && lastRow >= 2)
        {
            foreach (var node in sections.Where(node => node["Grid.Row"] is null or "0")) DrawNode(node, _root);
            Gui.Separator();
            Gui.BeginChild("modal-body-" + border.Id, new(0, -86), ImGuiChildFlags.None);
            foreach (var node in sections.Where(node => node.Number("Grid.Row", 0) > 0 && node.Number("Grid.Row", 0) < lastRow).OrderBy(node => node.Number("Grid.Row", 0))) DrawNode(node, _root);
            Gui.EndChild();
            Gui.Separator();
            foreach (var node in sections.Where(node => (int)node.Number("Grid.Row", 0) == lastRow)) DrawNode(node, _root);
        }
        else DrawChildren(border, _root);
    }

    private static bool Visible(UiNode node, object? context) => UiBinding.Boolean(context, node["IsVisible"]);
    private static string Text(object? context, string? expression) => UiBinding.Text(context, expression);
    private void Set(object? context, string? expression, object? value) => _actions.Enqueue(() => UiBinding.Write(context, expression, value));
    private void Execute(object? context, string? expression)
    {
        if (UiBinding.Read(context, expression) is ICommand command)
            _actions.Enqueue(() => { if (command.CanExecute(null)) command.Execute(null); });
    }

    private void DrawNode(UiNode node, object? context)
    {
        if (node.IsModal || !Visible(node, context)) return;
        if (_focusCompilation && node["Name"] == "EngineCompilationPanel")
        { Gui.Dummy(new(1, 1)); Gui.SetScrollHereY(0); _focusCompilation = false; }
        if (node["DataContext"] is { } dataContext) context = UiBinding.Read(context, dataContext);
        _visited.Add(node.Id);
        Gui.PushID(node.Id);
        Gui.BeginDisabled(!UiBinding.Boolean(context, node["IsEnabled"]));
        switch (node.Kind)
        {
            case "TextBlock": case "SelectableTextBlock":
                if (UiBinding.Path(node["Text"]) == "SelectedRemoteFabAssetCard.Description" && _root is MainWindowViewModel description)
                    DrawProductDescription(description);
                else DrawText(node, context);
                break;
            case "TextBox": DrawInput(node, context); break;
            case "NumericUpDown":
                var numeric = int.TryParse(Text(context, node["Value"]), out var number) ? number : 0;
                Gui.SetNextItemWidth(-1);
                if (Gui.InputInt("###number", ref numeric))
                    Set(context, node["Value"], Math.Clamp(numeric,
                        int.TryParse(node["Minimum"], out var minimum) ? minimum : int.MinValue,
                        int.TryParse(node["Maximum"], out var maximumValue) ? maximumValue : int.MaxValue));
                Record("field/" + (UiBinding.Path(node["Value"]) ?? node.Id));
                break;
            case "Button": DrawButton(node, context); break;
            case "ToggleSwitch":
            case "CheckBox":
                var value = UiBinding.Boolean(context, node["IsChecked"], false);
                if (Gui.Checkbox(Text(context, node["Content"]) + "###value", ref value)) Set(context, node["IsChecked"], value);
                Record("check/" + (UiBinding.Path(node["IsChecked"]) ?? node.Id));
                if (context is MarketplaceConnectorViewModel connector && UiBinding.Path(node["IsChecked"]) == "IsEnabled")
                    Record("connector-enabled/" + connector.Id);
                if (context is FabInstallationConfigurationOptionViewModel option) Record("script-option/" + option.Id);
                if (node.Children.Length > 0)
                {
                    Gui.SameLine(); Gui.BeginGroup();
                    DrawChildren(node, context);
                    Gui.EndGroup();
                    if (context is FabInstallVersionOptionViewModel variant) Record("variant-label/" + variant.Version.ArtifactId);
                    if (Gui.IsItemClicked()) Set(context, node["IsChecked"], !value);
                }
                break;
            case "ComboBox": DrawCombo(node, context); break;
            case "ListBox":
            case "ItemsControl": DrawItems(node, context); break;
            case "Grid":
                if (_root is MainWindowViewModel { IsLibraryVisible: true } toolbar && node["Name"] == "LibrarySearchToolbar") DrawLibrarySearchToolbar(toolbar, node);
                else if (_root is MainWindowViewModel { IsLibraryVisible: true } header && node["Name"] == "LibraryCatalogHeader") DrawLibraryCatalogHeader(header, node);
                else if (_root is MainWindowViewModel { IsLibraryVisible: true } display && node["Name"] == "LibraryDisplayToolbar") DrawLibraryDisplayToolbar(display);
                else DrawGrid(node, context);
                break;
            case "WrapPanel":
            case "StackPanel":
                if (_root is MainWindowViewModel gallery && node["Name"] == "FabProductGallery") DrawFabGallery(gallery, enlarged: false);
                else if (_root is MainWindowViewModel product && node["Name"] == "ProductActionPanel") DrawProductActions(product);
                else if (_root is MainWindowViewModel { IsLibraryVisible: true } library && node["Orientation"] == "Horizontal" &&
                    UiBinding.Path(node["IsVisible"]) == "IsLibraryQuixelSelected") DrawQuixelSubTabs(library);
                else DrawStack(node, context);
                break;
            case "Border": DrawPanel(node, context); break;
            case "TabControl": DrawTabs(node, context); break;
            case "Expander":
                var expanded = UiBinding.Boolean(context, node["IsExpanded"], false);
                if (expanded) Gui.SetNextItemOpen(true, ImGuiCond.Once);
                var open = Gui.CollapsingHeader(Text(context, node["Header"]) + "###expander");
                if (node["Name"] is not null) Record("expander/" + node["Name"]);
                if (node["IsExpanded"] is not null && open != expanded) Set(context, node["IsExpanded"], open);
                if (open) DrawChildren(node, context);
                break;
            case "ProgressBar":
                var progress = float.TryParse(Text(context, node["Value"]), out var parsed) ? parsed : 0;
                var maximum = node.Number("Maximum", 100);
                if (UiBinding.Path(node["Value"]) == "UsedPercent")
                    Gui.ProgressBar(Math.Clamp(progress / Math.Max(1, maximum), 0, 1), new(-1, 5), "");
                else OperationProgress(progress / Math.Max(1, maximum), new(-1, 5),
                    node["OperationStatus"] is { } status ? Text(context, status) : Value(context, "Status"),
                    UiBinding.Boolean(context, node["IsIndeterminate"], false), UiBinding.Path(node["Value"]));
                break;
            case "Image": DrawImage(node, context); break;
            case "ScrollViewer":
                if (node.Number("MaxHeight", node.Number("Height", 0)) is > 0 and var height)
                {
                    Gui.BeginChild("scroll", new(0, Math.Min(height, 320)), ImGuiChildFlags.None);
                    DrawChildren(node, context);
                    Gui.EndChild();
                }
                else DrawChildren(node, context);
                break;
            case "ConnectorLibraryPanel": Panel("Connector", node, context); break;
            case "QuixelCollectionsPanel": Panel("Collections", node, context); break;
            case "FabSignInPanel": Panel("FabSignIn", node, context); break;
            case "Ellipse": case "Path": case "Canvas": break;
            default:
                if (!node.Kind.Contains('.', StringComparison.Ordinal)) DrawChildren(node, context);
                break;
        }
        if (node.Kind != "ComboBox" && node["ToolTip.Tip"] is { } tip && Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) Gui.SetTooltip(Text(context, tip));
        Gui.EndDisabled();
        Gui.PopID();
    }

    private void Panel(string name, UiNode node, object? context)
    {
        if (!_panels.TryGetValue(name, out var panel)) _panels[name] = panel = UiDocument.Load(name);
        DrawChildren(panel.Root, context);
    }

    private void DrawChildren(UiNode node, object? context)
    {
        foreach (var child in node.Children) DrawNode(child, context);
    }

    private static void DrawText(UiNode node, object? context)
    {
        var text = Text(context, node["Text"]);
        text = NormalizeLabel(text);
        if (string.IsNullOrEmpty(text)) return;
        var size = node.Number("FontSize", 12);
        if (size >= 19) Gui.SetWindowFontScale(1.18f);
        var color = Color(context, node["Foreground"], size <= 10 ? ImGuiTheme.Muted : Gui.GetStyle().Colors[(int)ImGuiCol.Text]);
        Gui.PushStyleColor(ImGuiCol.Text, color);
        if (node["TextTrimming"] is not null) Gui.TextUnformatted(Elide(text, Gui.GetContentRegionAvail().X));
        else if (node["TextWrapping"] is not null) { Gui.PushTextWrapPos(0); Gui.TextUnformatted(text); Gui.PopTextWrapPos(); }
        else Gui.TextUnformatted(text);
        Gui.PopStyleColor();
        if (size >= 19) Gui.SetWindowFontScale(1);
        if (Gui.BeginPopupContextItem("copy"))
        {
            if (Gui.MenuItem("Copier")) Gui.SetClipboardText(text);
            Gui.EndPopup();
        }
    }

    private static string Elide(string text, float width)
    {
        if (Gui.CalcTextSize(text).X <= width) return text;
        var min = 0; var max = text.Length;
        while (min < max)
        {
            var mid = (min + max + 1) / 2;
            if (Gui.CalcTextSize(text[..mid] + "…").X <= width) min = mid;
            else max = mid - 1;
        }
        return text[..min] + "…";
    }

    private void DrawInput(UiNode node, object? context)
    {
        var text = Text(context, node["Text"]);
        Gui.SetNextItemWidth(-1);
        if (_focusRequests.Remove(UiBinding.Path(node["Text"]) ?? "")) Gui.SetKeyboardFocusHere();
        var flags = UiBinding.Boolean(context, node["IsReadOnly"], false) ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None;
        bool changed;
        if (UiBinding.Boolean(context, node["AcceptsReturn"], false))
            changed = Gui.InputTextMultiline("###text", ref text, (uint)Math.Max(65536, text.Length * 4 + 4096), new(-1, Math.Clamp(node.Number("Height", 260), 120, 400)), flags);
        else
            changed = Gui.InputTextWithHint("###text", Text(context, node["PlaceholderText"]), ref text, (uint)Math.Max(4096, text.Length * 4 + 4096), flags);
        if (changed) Set(context, node["Text"], text);
        Record("field/" + (UiBinding.Path(node["Text"]) ?? node.Id));
    }

    private void DrawButton(UiNode node, object? context)
    {
        if (node.Descendants().Any(child => child.Kind == "Image"))
        {
            Gui.BeginGroup();
            DrawChildren(node, context);
            Gui.EndGroup();
            if (Gui.IsItemHovered()) Gui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (Gui.IsItemClicked()) Execute(context, node["Command"]);
            return;
        }
        var label = ButtonLabel(node, context);
        var command = UiBinding.Read(context, node["Command"]) as ICommand;
        Gui.BeginDisabled(command is not null && !command.CanExecute(null));
        var accent = node["Classes"]?.Contains("accent", StringComparison.Ordinal) == true;
        var danger = node["Classes"]?.Contains("danger", StringComparison.Ordinal) == true;
        var selected = node["Classes"]?.Split(' ').Contains("active", StringComparer.Ordinal) == true ||
            UiBinding.Path(node["Command"]) switch
            {
                "ShowQuixelAssetsTabCommand" => Flag(_root, "IsQuixelAssetsTabSelected"),
                "ShowQuixelCollectionsTabCommand" => Flag(_root, "IsQuixelCollectionsTabSelected"),
                _ => false,
            };
        var colorCount = ImGuiTheme.PushActionColors(UiBinding.Path(node["Command"]), accent, danger);
        if (selected && !danger) { Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Selected); colorCount++; }
        if (Gui.Button(NormalizeLabel(label) + "###button"))
        {
            if (node["Click"] is { } action) _actions.Enqueue(() => _nativeAction?.Invoke(action));
            else Execute(context, node["Command"]);
        }
        Record("command/" + (UiBinding.Path(node["Command"]) ?? node["Click"] ?? node.Id) + "/" + node.Id);
        if (context is FabStoredDownloadViewModel stored)
            Record("stored-version-action/" + stored.ArtifactId + "/" + UiBinding.Path(node["Command"]));
        if (colorCount > 0) Gui.PopStyleColor(colorCount);
        Gui.EndDisabled();
    }

    private static string ButtonLabel(UiNode node, object? context)
    {
        var label = Text(context, node["Content"]);
        if (string.IsNullOrEmpty(label)) label = node.Descendants().Where(child => child.Kind == "TextBlock").Select(child => Text(context, child["Text"])).FirstOrDefault(text => !string.IsNullOrEmpty(text)) ?? "";
        if (string.IsNullOrEmpty(label))
        {
            var commandName = UiBinding.Path(node["Command"]) ?? "";
            label = commandName.Contains("Download", StringComparison.Ordinal) ? "DL" :
                commandName.Contains("Install", StringComparison.Ordinal) ? "Installer" :
                commandName.Contains("Listing", StringComparison.Ordinal) || commandName.Contains("Website", StringComparison.Ordinal) ? "Web" : "+";
        }
        return label;
    }

    private void DrawCombo(UiNode node, object? context)
    {
        var selected = UiBinding.Read(context, node["SelectedItem"]);
        var display = node["DisplayMemberBinding"];
        Gui.SetNextItemWidth(-1);
        var open = Gui.BeginCombo("###combo", display is null ? Label(selected) : Text(selected, display));
        Record("combo/" + (UiBinding.Path(node["SelectedItem"]) ?? node.Id));
        if (context is ProjectPostCreationOptionValueViewModel postOption) Record("post-creation-option/" + postOption.Id);
        if (node["ToolTip.Tip"] is { } selectedTip && Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            Gui.SetTooltip(Text(context, selectedTip));
            Record("combo-tooltip/" + UiBinding.Path(node["SelectedItem"]));
        }
        if (!open) return;
        var template = node.Children.FirstOrDefault(child => child.Kind.EndsWith(".ItemTemplate", StringComparison.Ordinal))?.Children.FirstOrDefault()?.Children.FirstOrDefault();
        if (UiBinding.Read(context, node["ItemsSource"]) is IEnumerable values)
        {
            var i = 0;
            foreach (var item in values)
            {
                Gui.PushID(i++);
                var label = display is not null ? Text(item, display) : Label(item);
                if (display is null && template is not null) label = template.Descendants().Prepend(template).Where(child => child.Kind == "TextBlock").Select(child => Text(item, child["Text"])).FirstOrDefault(text => !string.IsNullOrEmpty(text)) ?? label;
                if (Gui.Selectable(label, Equals(item, selected))) Set(context, node["SelectedItem"], item);
                if (node["ItemToolTipBinding"] is { } itemTip && Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    Gui.SetTooltip(Text(item, itemTip));
                    Record("combo-option-tooltip/" + UiBinding.Path(node["SelectedItem"]) + "/" + (i - 1));
                }
                Record("combo-option/" + UiBinding.Path(node["SelectedItem"]) + "/" + (i - 1));
                if (context is ProjectPostCreationOptionValueViewModel postChoice) Record("post-creation-choice/" + postChoice.Id + "/" + (i - 1));
                var compressionMode = item switch
                {
                    MarketplaceCompressionOptionViewModel compression => compression.StorageMode.ToString(),
                    MarketplaceCompressionChoice compression => compression.StorageMode,
                    _ => null,
                };
                if (compressionMode is not null)
                    Record("combo-option/" + UiBinding.Path(node["SelectedItem"]) + "/" + compressionMode);
                if (Equals(item, selected)) Gui.SetItemDefaultFocus();
                Gui.PopID();
            }
        }
        Gui.EndCombo();
    }

    private static string Label(object? item)
    {
        if (item is null) return "Choisir…";
        if (item is string text) return text;
        foreach (var property in new[] { "Label", "DisplayName", "Name", "Title", "FileName", "Version" })
            if (UiBinding.Read(item, "{Binding " + property + "}") is { } value && !string.IsNullOrEmpty(value.ToString())) return value.ToString()!;
        return item.ToString() ?? "";
    }

    private void DrawStack(UiNode node, object? context)
    {
        var horizontal = node.Kind == "WrapPanel" || node["Orientation"] == "Horizontal";
        if (!horizontal) { DrawChildren(node, context); return; }
        var first = true;
        var right = Gui.GetCursorScreenPos().X + Gui.GetContentRegionAvail().X;
        foreach (var child in node.Children.Where(child => !child.IsModal && Visible(child, context)))
        {
            if (!first && Gui.GetItemRectMax().X + Gui.GetStyle().ItemSpacing.X + NaturalWidth(child, context) <= right) Gui.SameLine();
            DrawNode(child, context);
            first = false;
        }
    }

    private void DrawGrid(UiNode node, object? context)
    {
        var children = node.Children.Where(child => !child.IsModal && Visible(child, context)).ToArray();
        var columns = node["ColumnDefinitions"]?.Split(',') ?? [];
        if (columns.Length <= 1) { foreach (var child in children.OrderBy(child => child.Number("Grid.Row", 0))) DrawNode(child, context); return; }
        float AutoWidth(int column)
        {
            var cells = children.Where(child => (int)child.Number("Grid.Column", 0) == column).ToArray();
            return cells.Length == 0 ? 20 : cells.Max(child => Math.Clamp(NaturalWidth(child, context), 25, 240));
        }
        var requiredWidth = columns.Select((spec, column) => spec.Trim() == "Auto" ? AutoWidth(column) : float.TryParse(spec, CultureInfo.InvariantCulture, out var fixedWidth) ? Math.Min(fixedWidth, 240) : node.Number("MinStretchColumnWidth", 30)).Sum() + columns.Length * 8;
        if (Gui.GetContentRegionAvail().X < requiredWidth)
        {
            foreach (var child in children) DrawNode(child, context);
            return;
        }
        if (!Gui.BeginTable("grid", columns.Length, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings)) return;
        for (var column = 0; column < columns.Length; column++)
        {
            var spec = columns[column].Trim();
            if (float.TryParse(spec, CultureInfo.InvariantCulture, out var fixedWidth))
                Gui.TableSetupColumn("###column" + column, ImGuiTableColumnFlags.WidthFixed, Math.Min(fixedWidth, 240));
            else if (spec == "Auto")
            {
                Gui.TableSetupColumn("###column" + column, ImGuiTableColumnFlags.WidthFixed, AutoWidth(column));
            }
            else Gui.TableSetupColumn("###column" + column, ImGuiTableColumnFlags.WidthStretch, 1);
        }
        foreach (var row in children.GroupBy(child => child.Number("Grid.Row", 0)).OrderBy(group => group.Key))
        {
            Gui.TableNextRow();
            foreach (var cell in row.GroupBy(child => (int)child.Number("Grid.Column", 0)))
            {
                Gui.TableSetColumnIndex(Math.Clamp(cell.Key, 0, columns.Length - 1));
                foreach (var child in cell) DrawNode(child, context);
            }
        }
        Gui.EndTable();
    }

    private static float NaturalWidth(UiNode node, object? context)
    {
        if (node["Name"] == "ProductActionPanel") return 240;
        if (node.Kind == "Button") return Gui.CalcTextSize(NormalizeLabel(ButtonLabel(node, context))).X + Gui.GetStyle().FramePadding.X * 2;
        if (node.Kind is "CheckBox" or "ToggleSwitch") return Gui.CalcTextSize(Text(context, node["Content"])).X + 35;
        if (node.Kind is "TextBlock" or "SelectableTextBlock") return Gui.CalcTextSize(Text(context, node["Text"])).X;
        if (node.Children.Length > 0)
        {
            var widths = node.Children.Where(child => Visible(child, context)).Select(child => NaturalWidth(child, context)).ToArray();
            return widths.Length == 0 ? 20 : node["Orientation"] == "Horizontal" ? widths.Sum() + Math.Max(0, widths.Length - 1) * 7 : widths.Max();
        }
        return 100;
    }

    private void DrawPanel(UiNode node, object? context)
    {
        if (node.Children.Length == 0) return;
        var card = node["Classes"]?.Contains("card", StringComparison.Ordinal) == true || node["Classes"] == "metric" || node["AttachedToVisualTree"] == "LoadRemoteFabThumbnail";
        if (card) Gui.BeginChild("card", new(0, 0), ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize | ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar);
        DrawChildren(node, context);
        if (node["PointerReleased"] == "OpenDetailsFromItem")
        {
            if (Gui.Button("Ouvrir la fiche")) Execute(context, "{Binding OpenDetailsCommand}");
        }
        if (card)
        {
            Gui.EndChild();
            if (node["Name"] == "DownloadedVersionCard" && context is FabStoredDownloadViewModel download)
                Record("stored-version/" + download.ArtifactId);
            Gui.Spacing();
        }
    }

    private void DrawTabs(UiNode node, object? context)
    {
        var tabs = node.Children.Where(tab => tab.Kind == "TabItem" && Visible(tab, context)).ToArray();
        if (tabs.Length == 0) return;
        var selected = UiBinding.Read(context, node["SelectedIndex"]) is int value ? value : _tabSelections.GetValueOrDefault(node.Id);
        selected = Math.Clamp(selected, 0, tabs.Length - 1);
        var first = true;
        for (var index = 0; index < tabs.Length; index++)
        {
            var tab = tabs[index]; var label = Text(context, tab["Header"]);
            if (!first && Gui.GetContentRegionAvail().X > Gui.CalcTextSize(label).X + 24) Gui.SameLine();
            var wasSelected = index == selected;
            if (wasSelected) Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Selected);
            if (Gui.Button(label + "###" + tab.Id))
            {
                selected = index; _tabSelections[node.Id] = index;
                if (node["SelectedIndex"] is not null) Set(context, node["SelectedIndex"], index);
            }
            Record("tab/" + label);
            if (wasSelected) Gui.PopStyleColor();
            first = false;
        }
        Gui.Separator();
        DrawChildren(tabs[selected], context);
    }

    private void DrawItems(UiNode node, object? context)
    {
        if (UiBinding.Read(context, node["ItemsSource"]) is not IEnumerable sequence) return;
        var values = sequence as IList ?? sequence.Cast<object>().ToArray();
        var template = node.Children.FirstOrDefault(child => child.Kind.EndsWith(".ItemTemplate", StringComparison.Ordinal))?.Children.FirstOrDefault()?.Children.FirstOrDefault();
        var selected = UiBinding.Read(context, node["SelectedItem"]);
        var scroll = node.Kind == "ListBox";
        var binding = UiBinding.Path(node["ItemsSource"]) ?? node.Id;
        if (_root is MainWindowViewModel favoriteVm && binding is "FabLocalContentPacks" or "QuixelLocalAssets" or "QuixelCollectionCards")
        {
            var filtered = values.Cast<object>().Where(item => !favoriteVm.FavoritesOnly || favoriteVm.IsResourceFavorite(item));
            values = (favoriteVm.FavoritesFirst ? filtered.OrderByDescending(favoriteVm.IsResourceFavorite) : filtered).ToArray();
        }
        if (binding == "PagedRemoteFabRows")
        {
            DrawAssetCatalog(values.Cast<RemoteFabAssetRowViewModel>().ToArray(), binding);
            return;
        }
        if (_root is MainWindowViewModel { IsLibraryVisible: true, IsLibraryRowsMode: true } rowVm &&
            (binding is "QuixelCollectionCards" or "QuixelLocalAssets" or "FabLocalContentPacks" || binding == "Assets" && context is ConnectorLibraryViewModel))
        {
            DrawLocalLibraryRows(values.Cast<object>().ToArray(), binding, rowVm.IsLibraryRowImages);
            return;
        }
        if (_root is MainWindowViewModel { IsLibraryVisible: true, IsLibraryImageOnlyCards: true } &&
            (binding is "QuixelCollectionCards" or "QuixelLocalAssets" or "FabLocalContentPacks" || binding == "Assets" && context is ConnectorLibraryViewModel))
        {
            DrawMinimalLibraryCatalog(values.Cast<object>().ToArray(), binding);
            return;
        }
        if (scroll)
        {
            var height = node.Number("MaxHeight", node.Number("Height", 0));
            if (height <= 0) height = node["ItemsSource"]?.Contains("PagedRemoteFabRows", StringComparison.Ordinal) == true ? Math.Max(300, Gui.GetContentRegionAvail().Y - 25) : 320;
            if (binding == "QuixelCollectionCards") height = Math.Max(180, Gui.GetContentRegionAvail().Y - 8);
            Gui.BeginChild("list", new(0, binding == "QuixelCollectionCards" ? height : Math.Clamp(height, 120, 650)), ImGuiChildFlags.Borders);
            if (_scrollRequests.Remove(binding)) Gui.SetScrollY(Gui.GetScrollMaxY());
        }
        if (binding is "FabLocalContentPacks" or "QuixelLocalAssets" or "QuixelCollectionCards" || binding == "Assets" && context is ConnectorLibraryViewModel)
            SynchronizeItemSelection(LibrarySelectionScope(binding), values.Cast<object>());
        void Item(int index)
        {
            if (index >= values.Count) return;
            VisibleRows[binding] = VisibleRows.GetValueOrDefault(binding) + 1;
            var startY = Gui.GetCursorPosY();
            var item = values[index];
            Gui.PushID(index);
            if (item is RemoteFabAssetRowViewModel row)
            {
                row.Realize();
                _seen.Add(row);
                _realized.Add(row);
            }
            if (item is RemoteFabAssetCardViewModel asset) asset.EnsureThumbnailLoaded();
            if (item is QuixelCollectionCardViewModel collection)
            {
                collection.EnsureThumbnailLoaded();
                _seenCollections.Add(collection);
                _collections.Add(collection);
            }
            if (node["SelectedItem"] is not null && template is null)
            {
                if (Gui.Selectable(Label(item) + "###select", Equals(item, selected))) Set(context, node["SelectedItem"], item);
            }
            if (template is not null)
            {
                Gui.BeginGroup();
                if (Equals(item, selected)) Gui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.Accent);
                DrawNode(template, item);
                if (Equals(item, selected)) Gui.PopStyleColor();
                Gui.EndGroup();
                if (item is FabLocalContentPackCardViewModel or QuixelLocalAssetCardViewModel or QuixelCollectionCardViewModel or ConnectorAssetViewModel)
                {
                    if (Gui.IsItemClicked()) SelectItem(item);
                    if (Gui.IsItemClicked(ImGuiMouseButton.Right)) { RequestItemContext(item,
                        item is FabLocalContentPackCardViewModel ? "UseForImportCommand" : item is QuixelLocalAssetCardViewModel ? "UseCommand" : "OpenCommand"); }
                    if (IsItemSelected(item)) DrawCardSelection(Gui.GetItemRectMin(), Gui.GetItemRectMax());
                }
                if (node["SelectedItem"] is not null && Gui.IsItemClicked()) Set(context, node["SelectedItem"], item);
                Record("list/" + binding + "/" + Label(item));
            }
            else if (node["SelectedItem"] is null) Gui.TextUnformatted(Label(item));
            if (Gui.GetCursorPosY() <= startY) Gui.Dummy(new(0, 1));
            Gui.PopID();
        }
        var wrap = node["Flow"] == "Wrap";
        if (wrap && template is not null && values.Count > 0)
        {
            var columns = Math.Clamp((int)(Gui.GetContentRegionAvail().X / 270), 1, 5);
            if (Gui.BeginTable("cards", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings))
            {
                for (var i = 0; i < values.Count; i++) { Gui.TableNextColumn(); Item(i); }
                Gui.EndTable();
            }
        }
        else if (scroll && values.Count > 0)
        {
            if (!_lists.TryGetValue(node.Id, out var list)) _lists[node.Id] = list = new UiListViewport(node["ItemsSource"]?.Contains("PagedRemoteFabRows", StringComparison.Ordinal) == true ? 270 : 90);
            var origin = Gui.GetCursorPosY();
            var range = list.Range(values.Count, Gui.GetScrollY(), Gui.GetWindowSize().Y);
            Gui.SetCursorPosY(origin + range.Offset);
            for (var index = range.Start; index < range.End; index++)
            {
                var y = Gui.GetCursorPosY();
                Item(index);
                list.Measure(index, Gui.GetCursorPosY() - y);
            }
            Gui.SetCursorPosY(Math.Max(Gui.GetCursorPosY(), origin + range.TotalHeight));
            Gui.Dummy(new(0, 1));
        }
        else for (var i = 0; i < values.Count; i++) Item(i);
        if (scroll)
        {
            if (node["ItemsSource"]?.Contains("PagedRemoteFabRows", StringComparison.Ordinal) == true && Gui.GetScrollMaxY() - Gui.GetScrollY() < 500 && _root is MainWindowViewModel viewModel) _actions.Enqueue(viewModel.LoadMoreLibraryItems);
            Gui.EndChild();
            Record("list-pane/" + binding);
        }
    }

    private void DrawImage(UiNode node, object? context)
    {
        if (UiBinding.Read(context, node["Source"]) is not Bitmap bitmap) return;
        var preview = node["Source"]?.Contains("PreviewImage", StringComparison.Ordinal) == true;
        var width = Math.Clamp(node.Number("Width", preview ? Gui.GetContentRegionAvail().X : 230), 32, Math.Max(32, Gui.GetContentRegionAvail().X));
        var height = width * bitmap.PixelSize.Height / Math.Max(1, bitmap.PixelSize.Width);
        if (preview && height > Gui.GetContentRegionAvail().Y)
        {
            var scale = Math.Max(32, Gui.GetContentRegionAvail().Y) / height;
            width *= scale;
            height *= scale;
        }
        if (!preview && node["Source"] == "{Binding Thumbnail}" && height > 145)
        {
            width *= 145 / height;
            height = 145;
        }
        var texture = _image(bitmap);
        var size = new Vector2(width, Math.Min(height, preview ? 900 : 450));
        if (texture != 0) Gui.Image(texture, size); else Gui.Dummy(size);
    }

    internal static string NormalizeLabel(string label) => label.Replace("✕", "Fermer", StringComparison.Ordinal)
        .Replace("←", "<", StringComparison.Ordinal).Replace("→", ">", StringComparison.Ordinal)
        .Replace("🌐", "Web", StringComparison.Ordinal).Replace("⚙", "", StringComparison.Ordinal)
        .Replace("↺", "Reset", StringComparison.Ordinal).Replace("＋", "+", StringComparison.Ordinal)
        .Replace("☷", "", StringComparison.Ordinal).Replace("⛶", "", StringComparison.Ordinal);

    internal static Vector4 Color(object? context, string? expression, Vector4 fallback)
    {
        var value = UiBinding.Read(context, expression);
        if (value is Avalonia.Media.ISolidColorBrush brush)
        {
            var color = brush.Color;
            return new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        }
        if (value is string { Length: 7 } text && text.StartsWith('#') && uint.TryParse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
        return fallback;
    }

    public void Dispose()
    {
        foreach (var row in _realized) row.Recycle();
        _realized.Clear();
        foreach (var collection in _collections) collection.ReleaseThumbnail();
        _collections.Clear();
        _logo?.Dispose();
        _logo = null;
    }
}
