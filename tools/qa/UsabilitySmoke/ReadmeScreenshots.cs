using Avalonia.Controls;
using CyEngineHub.Core.Marketplaces;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Conversion;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

static partial class Program
{
    // Render the real UI with public demo metadata only. Never launch/download/convert assets.
    static async Task CaptureReadmeScreenshots(Window window, MainWindowViewModel vm)
    {
        window.Width = 1280; window.Height = 820;
        vm.InterfaceTheme = "mid-grey"; vm.UseCompactInterface = true;
        var template = vm.Engines.Single().Engine;
        var engines = new[]
        {
            DemoEngine("studio", "Unreal Engine 5.7 · Studio", "5.7", new(5, 7, 0), EngineInstallationType.SourceFork, "studio/main"),
            DemoEngine("official", "Unreal Engine 5.5", "5.5", new(5, 5, 4), EngineInstallationType.EpicBinary, null),
            DemoEngine("legacy", "Unreal Engine 5.3 · Legacy", "5.3", new(5, 3, 2), EngineInstallationType.EpicBinary, null),
        };
        Call(vm, "ReplaceCatalog", engines, Array.Empty<UnrealProject>());
        vm.ShowEnginesCommand.Execute(null); vm.ShowEngineCardsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 12);
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "readme-engines.png"));

        vm.ShowProjectsCommand.Execute(null); vm.ToggleProjectCreatorCommand.Execute(null);
        vm.NewProjectName = "AtlasDemo"; vm.NewProjectParentDirectory = @"D:\Projects";
        vm.SelectedProjectCreationEngine = vm.ProjectCreationEngines.First(item => item.Engine.Id == "demo:official");
        await ImGuiDiagnostics.FramesAsync(window, 12);
        Check(vm.IsProjectCreatorVisible, "Public project creation capture uses the real dialog");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "readme-project-creation.png"));
        vm.CloseProjectCreatorCommand.Execute(null);

        // Only this tiny host-owned manifest is read. No conversion is executed.
        var demoPackage = Path.Combine(Output, "DemoPackage"); Directory.CreateDirectory(demoPackage);
        await File.WriteAllTextAsync(Path.Combine(demoPackage, "cyenginehub-package.json"), "{\"fileExtensions\":{\".blend\":1}}");
        vm.ShowLibraryCommand.Execute(null);
        var asset = new MarketplaceLocalAsset("demo:source", "demo:market", demoPackage,
            "Atlas props · Blender source", "BLEND", "Assets RAW", 0, DateTimeOffset.UtcNow, "Demo");
        await (Task)typeof(MainWindowViewModel).GetMethod("RequestSourceConversionAsync", Private)!.Invoke(vm, new object[] { asset })!;
        vm.SourceConversionToUnreal = true; vm.SourceConversionEngineId = "demo:official";
        await ImGuiDiagnostics.FramesAsync(window, 8);
        Check(vm.IsSourceConversionVisible && vm.SourceConversionConverterId == BlenderSourceConverter.ConverterId,
            "Public converter capture shows actual options without executing a converter");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "readme-source-conversion.png"));
        vm.IsSourceConversionVisible = false;

        UnrealEngineInstallation DemoEngine(string id, string name, string association, EngineVersion version, EngineInstallationType kind, string? branch)
        {
            var root = @"D:\Engines\UE_" + association.Replace('.', '_');
            return template with { Id = "demo:" + id, DisplayName = name, RootPath = root,
                EditorPath = Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor.exe"),
                Version = version, AssociationId = association, Type = kind, Branch = branch };
        }
    }
}
