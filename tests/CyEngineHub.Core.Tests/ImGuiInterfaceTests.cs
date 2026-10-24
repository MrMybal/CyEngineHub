using CyEngineHub.Desktop.ImGui;
using Xunit;

namespace CyEngineHub.Core.Tests;

public sealed class ImGuiInterfaceTests
{
    [Fact]
    public void FabProductExposesNativeGalleryAndAnEnlargedViewerWithAnEscapeCloseCommand()
    {
        var document = UiDocument.Load("Main");
        Assert.Contains(document.Root.Descendants(), node => node["Name"] == "FabProductGallery");
        var modal = Assert.Single(document.Modals, node => UiBinding.Path(node["IsVisible"]) == "IsFabImagePreviewVisible");
        Assert.Contains(modal.Descendants(), node => node["Name"] == "FabGalleryViewer");
        Assert.Contains(modal.Descendants(), node => UiBinding.Path(node["Command"]) == "CloseFabImagePreviewCommand");
    }
    [Fact]
    public void ProjectFicheHasNoMarketplaceSpecificConfigurationAndPluginTargetsHaveLabels()
    {
        var document = UiDocument.Load("Main");
        var project = document.Root.Descendants().Single(node => node["IsVisible"] == "{Binding IsProjectDetailsVisible}");
        Assert.DoesNotContain(project.Descendants(), node => (node["Text"] ?? "").Contains("COSMOS", StringComparison.Ordinal));
        Assert.DoesNotContain(project.Descendants(), node => UiBinding.Path(node["Command"]) == "SaveCosmosProjectSettingsCommand");
        var target = document.Root.Descendants().Single(node => UiBinding.Path(node["SelectedItem"]) == "SelectedPluginTarget");
        Assert.Equal("{Binding SelectionLabel}", target["DisplayMemberBinding"]);
        Assert.Equal("{Binding Path}", target["ItemToolTipBinding"]);
    }
    [Fact]
    public void ProjectBuildProgressAndBuildsReturnNavigationAreBoundToTheirProject()
    {
        var nodes = UiDocument.Load("Main").Root.Descendants().ToArray();
        var build = nodes.Single(node => node["Name"] == "ProjectBuildProgress");
        Assert.Contains(build.Descendants(), node => UiBinding.Path(node["Value"]) == "SelectedProjectBuildTask.ProgressPercent");
        Assert.Contains(build.Descendants(), node => UiBinding.Path(node["IsIndeterminate"]) == "SelectedProjectBuildTask.IsProgressIndeterminate");
        Assert.Contains(nodes, node => UiBinding.Path(node["Command"]) == "BackFromBuildsCommand");
    }
    [Fact]
    public void ProjectCreationFormSeparatesCodeKindFromEngineAndLabelsItsLocationFields()
    {
        var nodes = UiDocument.Load("Main").Root.Descendants().ToArray();
        var location = nodes.Single(node => node["Name"] == "ProjectCreationLocation");
        Assert.Contains(location.Descendants(), node => node["Text"] == "Nom du projet");
        Assert.Contains(location.Descendants(), node => node["Text"] == "Dossier parent");
        Assert.Contains(location.Descendants(), node => node["Click"] == "BrowseNewProjectParentDirectory");
        Assert.DoesNotContain(location.Descendants(), node => UiBinding.Path(node["Text"]) == "NewProjectEngineAssociation");
        var kind = nodes.Single(node => node["Name"] == "ProjectCreationCodeKind");
        Assert.Contains(kind.Descendants(), node => UiBinding.Path(node["SelectedItem"]) == "SelectedProjectCreationCodeKind");
        Assert.Contains(nodes, node => UiBinding.Path(node["Text"]) == "ProjectCreationDestinationPreview");
        var advanced = nodes.Single(node => node["Name"] == "ProjectCreationAdvanced");
        Assert.Contains(advanced.Descendants(), node => UiBinding.Path(node["Text"]) == "NewProjectEngineAssociation" && node.Kind == "TextBlock");
    }
    [Fact]
    public void ProjectEngineSelectorsUseTheAliasLabelAndBothSelectedAndPerOptionTooltips()
    {
        var combos = UiDocument.Load("Main").Root.Descendants().Where(node => node.Kind == "ComboBox" &&
            UiBinding.Path(node["ItemsSource"]) == "ProjectCreationEngines").ToArray();
        Assert.Equal(2, combos.Length);
        Assert.All(combos, node =>
        {
            Assert.Equal("Label", UiBinding.Path(node["DisplayMemberBinding"]));
            Assert.Equal("ToolTip", UiBinding.Path(node["ItemToolTipBinding"]));
            Assert.Equal("SelectedProjectCreationEngine.ToolTip", UiBinding.Path(node["ToolTip.Tip"]));
        });
    }
    [Fact]
    public void FabProductAndStoredVersionsExposeRevisionStatusWithStatusColors()
    {
        var nodes = UiDocument.Load("Main").Root.Descendants().ToArray();
        Assert.Contains(nodes, node => UiBinding.Path(node["Text"]) == "FabUpdateSummary");
        Assert.Contains(nodes, node => UiBinding.Path(node["Text"]) == "SelectedRemoteFabAssetCard.UpdateLabel" &&
            UiBinding.Path(node["Foreground"]) == "SelectedRemoteFabAssetCard.UpdateForeground");
        Assert.Contains(nodes, node => UiBinding.Path(node["Text"]) == "UpdateLabel" && UiBinding.Path(node["Foreground"]) == "UpdateForeground");
    }
    [Fact]
    public void ConnectorLibraryRowsUseTheSameColumnsAsFab()
    {
        var panel = UiDocument.Load("Main").Root.Descendants().Single(node => node.Kind == "StackPanel" &&
            UiBinding.Path(node["IsVisible"]) == "IsSettingsMarketplacesSelected");
        var connector = panel.Descendants().Single(node => node.Kind == "ItemsControl" && UiBinding.Path(node["ItemsSource"]) == "MarketplaceConnectors");
        var nativeRows = panel.Descendants().Single(node => node.Kind == "ItemsControl" && UiBinding.Path(node["ItemsSource"]) == "FabLibraryLocations");
        var connectorRows = connector.Descendants().Single(node => node.Kind == "ItemsControl" && UiBinding.Path(node["ItemsSource"]) == "LibraryLocations");
        Assert.Equal(nativeRows.Descendants().Single(node => node.Kind == "Grid")["ColumnDefinitions"],
            connectorRows.Descendants().Single(node => node.Kind == "Grid")["ColumnDefinitions"]);
        Assert.Contains(connectorRows.Descendants(), node => UiBinding.Path(node["Text"]) == "PriorityLabel");
        Assert.Contains(connector.Descendants(), node => UiBinding.Path(node["Command"]) == "AddLibraryCommand" && node["Content"] == "+ Ajouter un dossier");
        Assert.DoesNotContain(connector.Descendants(), node => UiBinding.Path(node["ItemsSource"]) == "CompressionModes");
    }

    [Fact]
    public void DownloadedSupplierVersionsExposeTheSameLocalInstallActionAsDerivedVersions()
    {
        var nodes = UiDocument.Load("Main").Root.Descendants().ToArray();
        var install = nodes.Single(node => node.Kind == "Button" && UiBinding.Path(node["Command"]) == "InstallCommand");
        Assert.Equal("Installer", install["Content"]);
        Assert.Null(install["IsVisible"]);
    }
    [Fact]
    public void ProductActionsHaveExplicitLabelsAndKeepTheirOriginalCommands()
    {
        var panel = UiDocument.Load("Main").Root.Descendants().Single(node => node["Name"] == "ProductActionPanel");
        var buttons = panel.Descendants().Where(node => node.Kind == "Button").ToArray();
        Assert.Equal(5, buttons.Length);
        Assert.All(buttons, node => Assert.False(string.IsNullOrWhiteSpace(node["Content"])));
        Assert.Contains(buttons, node => UiBinding.Path(node["Command"]) == "SelectedRemoteFabAssetCard.DownloadCommand");
        Assert.Contains(buttons, node => UiBinding.Path(node["Command"]) == "SelectedRemoteFabAssetCard.OpenCollectionsCommand");
        Assert.Contains(buttons, node => UiBinding.Path(node["Command"]) == "HideSelectedFabAssetCommand");
    }
    [Fact]
    public void EngineChecksBindTheirStatusColorInsteadOfAConstantWarningColor()
    {
        var panel = UiDocument.Load("Main").Root.Descendants().Single(node => node["Name"] == "EngineCompilationPanel");
        var checks = panel.Descendants().Where(node => UiBinding.Path(node["Text"]) == "Health").ToArray();
        Assert.NotEmpty(checks);
        Assert.All(checks, node => Assert.Equal("HealthColor", UiBinding.Path(node["Foreground"])));
    }
    [Fact]
    public void DownloadControlsHaveTheirOwnSettingsSection()
    {
        var nodes = UiDocument.Load("Main").Root.Descendants().ToArray();
        var downloads = nodes.Single(node => node.Kind == "StackPanel" && UiBinding.Path(node["IsVisible"]) == "IsSettingsDownloadsSelected");
        Assert.Contains(downloads.Descendants(), node => UiBinding.Path(node["Value"]) == "DownloadConcurrency");
        Assert.Contains(downloads.Descendants(), node => UiBinding.Path(node["Value"]) == "EngineChunkConcurrency" && node["Maximum"] == "32");
        var library = nodes.Single(node => node.Kind == "StackPanel" && UiBinding.Path(node["IsVisible"]) == "IsSettingsLibrarySelected");
        Assert.DoesNotContain(library.Descendants(), node => UiBinding.Path(node["Value"]) == "EngineChunkConcurrency");
    }

    [Fact]
    public void ProductBackIsScopedToDetailsRatherThanRootCatalogs()
    {
        var product = UiDocument.Load("Main").Root.Descendants().Single(node => node.Kind == "ScrollViewer" &&
            UiBinding.Path(node["IsVisible"]) == "IsRemoteFabProductVisible");
        Assert.Contains(product.Descendants(), node => UiBinding.Path(node["Command"]) == "CloseRemoteFabProductCommand");
    }

    [Fact]
    public void StoredVariantsUseSeparateCardsWithIndependentActionsAndConfirmation()
    {
        var row = UiDocument.Load("Main").Root.Descendants().Single(node => node["Name"] == "DownloadedVersionCard");
        Assert.Contains("card", row["Classes"], StringComparison.Ordinal);
        Assert.Contains(row.Descendants(), node => node.Kind == "WrapPanel");
        Assert.Contains(row.Descendants(), node => UiBinding.Path(node["IsVisible"]) == "IsDeleteConfirmationVisible");
        Assert.Contains(row.Descendants(), node => UiBinding.Path(node["Command"]) == "CancelDeleteCommand");
    }

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
        foreach (var page in new[] { "IsEnginesVisible", "IsEngineDetailsVisible", "IsProjectsVisible", "IsProjectDetailsVisible", "IsLibraryVisible", "IsPluginsVisible", "IsHubExtensionsVisible", "IsToolchainsVisible", "IsStorageVisible", "IsBuildsVisible", "IsTasksVisible", "IsActionsVisible", "IsLogsVisible", "IsSettingsVisible" }) Assert.Contains(page, pages);
        Assert.DoesNotContain("IsHomeVisible", pages);
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
