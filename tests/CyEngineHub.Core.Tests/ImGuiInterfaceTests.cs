using CyEngineHub.Desktop.ImGui;
using Xunit;

namespace CyEngineHub.Core.Tests;

public sealed class ImGuiInterfaceTests
{
    [Theory]
    [InlineData("IsLibraryVisible")]
    [InlineData("IsSettingsVisible")]
    public void PersistentSectionsExposeContentIndependentlyOfTheirNavigation(string section)
    {
        var page = UiDocument.Load("Main").Pages.Single(node => UiBinding.Path(node["IsVisible"]) == section);
        var body = section == "IsSettingsVisible" ? page.Children.Single(node => node["Grid.Row"] == "1") : page;
        Assert.Single(body.Children, node => node["Grid.Column"] == "1");
        Assert.Contains(body.Children, node => node["Grid.Column"] is null);
    }

    [Theory]
    [InlineData(300, 1)]
    [InlineData(619, 1)]
    [InlineData(630, 2)]
    [InlineData(1400, 4)]
    public void CardDimensionsStayConstantWhileColumnsReflow(float available, int columns)
    {
        var grid = UiCardGrid.Create(available, 310, 350);
        Assert.Equal(columns, grid.Columns);
        Assert.Equal(310, grid.Width);
        Assert.Equal(350, grid.Height);
        Assert.Equal(360, grid.RowStride);
    }

    [Fact]
    public void CardGridOnlyRealizesVisibleRowsAndHandlesEmptyCatalogs()
    {
        var grid = UiCardGrid.Create(1000, 310, 350);
        var range = grid.VisibleRows(20000, 45000, 600);
        Assert.InRange(range.End - range.Start, 1, 5);
        Assert.Equal((0, 0), grid.VisibleRows(0, 45000, 600));
        Assert.Equal(1, grid.Rows(1));
        Assert.Equal(1.15f, UiCardGrid.InterfaceScale);
    }

    private sealed class Model
    {
        public string Name { get; set; } = "Projet";
        public bool Selected { get; set; }
        public int Count { get; set; }
        public Model? Child { get; set; }
    }

    [Theory]
    [InlineData("{Binding Name}", "Name")]
    [InlineData("{Binding Child.Name, UpdateSourceTrigger=PropertyChanged}", "Child.Name")]
    [InlineData("{Binding !Selected}", "!Selected")]
    public void BindingsKeepPathsAndIgnoreMarkupOptions(string expression, string expected) => Assert.Equal(expected, UiBinding.Path(expression));

    [Fact]
    public void LiveBindingsReadWriteNestedProperties()
    {
        var model = new Model { Child = new Model() };
        UiBinding.Write(model, "{Binding Child.Name}", "Atelier");
        UiBinding.Write(model, "{Binding Count}", "45");
        Assert.Equal("Atelier", UiBinding.Read(model, "{Binding Child.Name}"));
        Assert.Equal(45, model.Count);
        Assert.True(UiBinding.Boolean(model, "{Binding !Selected}"));
        UiBinding.Write(model, "{Binding Selected}", true);
        Assert.False(UiBinding.Boolean(model, "{Binding !Selected}"));
    }

    [Fact]
    public void MissingOrNullPathsDoNotBreakTheFrame()
    {
        var model = new Model();
        Assert.Null(UiBinding.Read(model, "{Binding Child.Name}"));
        Assert.Null(UiBinding.Read(model, "{Binding Missing}"));
        UiBinding.Write(model, "{Binding Child.Name}", "ignored");
        Assert.Equal("Texte", UiBinding.Read(model, "Texte"));
    }

    [Theory]
    [InlineData("{Binding Count, StringFormat='{}{0} REÇU(S)'}", "45 REÇU(S)")]
    [InlineData("{Binding Name, StringFormat='Projet · {0}'}", "Projet · Projet")]
    [InlineData("{Binding Name}", "Projet")]
    public void TextBindingsRetainCompositeFormats(string expression, string expected) => Assert.Equal(expected, UiBinding.Text(new Model { Count = 45 }, expression));

    [Theory]
    [InlineData("Main")]
    [InlineData("Extensions")]
    [InlineData("Connector")]
    [InlineData("Collections")]
    [InlineData("FabSignIn")]
    public void AllInterfacesAreIndependentXmlResources(string name)
    {
        var document = UiDocument.Load(name);
        Assert.Equal("Interface", document.Root.Kind);
        Assert.NotEmpty(document.Root.Children);
        Assert.All(document.Root.Descendants(), node => Assert.StartsWith(name + "/", node.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(document.Root.Descendants(), node => node.Kind.EndsWith(".Styles", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryMainSectionAndModalHasBeenPorted()
    {
        var document = UiDocument.Load("Main");
        var pages = document.Pages.Select(node => UiBinding.Path(node["IsVisible"])).ToArray();
        foreach (var page in new[] { "IsHomeVisible", "IsEnginesVisible", "IsEngineDetailsVisible", "IsProjectsVisible", "IsProjectDetailsVisible", "IsLibraryVisible", "IsPluginsVisible", "IsHubExtensionsVisible", "IsToolchainsVisible", "IsStorageVisible", "IsBuildsVisible", "IsTasksVisible", "IsLogsVisible", "IsSettingsVisible" }) Assert.Contains(page, pages);
        Assert.True(document.Modals.Length >= 18);
        Assert.Contains(document.Modals, node => UiBinding.Path(node["IsVisible"]) == "IsProjectCreatorVisible");
        Assert.Contains(document.Modals, node => UiBinding.Path(node["IsVisible"]) == "IsFabInstallationScriptConfigurationVisible");
        var layers = document.Modals.Select(node => node.Number("Panel.ZIndex", 0)).ToArray();
        Assert.Equal(layers.Order(), layers);
    }

    [Fact]
    public void InstallationDownloadAndEditingCommandsStayBoundToExistingServices()
    {
        var commands = UiDocument.Load("Main").Root.Descendants().Select(node => UiBinding.Path(node["Command"])).ToHashSet(StringComparer.Ordinal);
        foreach (var command in new[] { "DownloadSelectedFabVersionsCommand", "InstallSelectedFabVersionsCommand", "CreateProjectCommand", "SaveProjectProfileCommand", "CheckForUpdatesCommand" }) Assert.Contains(command, commands);
        Assert.Contains(UiDocument.Load("FabSignIn").Root.Descendants(), node => UiBinding.Path(node["Command"]) == "OpenEmbeddedFabAuthorizationCommand");
        Assert.Contains(UiDocument.Load("FabSignIn").Root.Descendants(), node => UiBinding.Path(node["Command"]) == "ConnectDirectFabCommand");
    }

    [Fact]
    public void LargeListsOnlyRealizeTheViewport()
    {
        var layout = new UiListViewport(90);
        var range = layout.Range(20000, 45000, 600);
        Assert.InRange(range.End - range.Start, 1, 12);
        Assert.InRange(range.Start, 490, 500);
        Assert.Equal(1800000, range.TotalHeight);
    }

    [Fact]
    public void MeasuredWrappedRowsUpdateTheScrollOffsets()
    {
        var layout = new UiListViewport(10);
        _ = layout.Range(10, 0, 20);
        layout.Measure(0, 100);
        layout.Measure(1, 0);
        var range = layout.Range(10, 120, 20);
        Assert.Equal(181, range.TotalHeight);
        Assert.Equal(2, range.Start);
        Assert.Equal(101, range.Offset);
    }

    [Fact]
    public void FilteredEmptyAndShorterListsCannotProduceInvalidRanges()
    {
        var layout = new UiListViewport();
        _ = layout.Range(45000, 50000, 400);
        Assert.Equal(new UiVisibleRange(0, 0, 0, 0), layout.Range(0, 50000, 400));
        Assert.Equal(new UiVisibleRange(1, 1, 90, 90), layout.Range(1, 50000, 400));
        Assert.Equal(0, layout.Range(1, 0, 400).Start);
    }
}
