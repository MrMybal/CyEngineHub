using Avalonia.Controls;
using Avalonia.Input;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;

static partial class Program
{
    static async Task TestManual(Window window, MainWindowViewModel vm)
    {
        Check(vm.DocumentationLanguage=="en","Documentation defaults to English");
        DocumentationRequest? request=null;
        vm.DocumentationRequested+=(_,value)=>request=value;
        await ImGuiDiagnostics.ClickAsync(window,"help/open");
        await ImGuiDiagnostics.FramesAsync(window,5);
        Check(request?.Page=="welcome","Sidebar opens the CyDoc welcome route");
        foreach(var owned in window.OwnedWindows.ToArray()) owned.Close();
        vm.ShowProjectsCommand.Execute(null); request=null;
        await ImGuiDiagnostics.FramesAsync(window,3);
        await ImGuiDiagnostics.KeyAsync(window,Key.F1);
        await ImGuiDiagnostics.FramesAsync(window,5);
        Check(request?.Page=="projects","F1 opens context-specific project help");
        foreach(var owned in window.OwnedWindows.ToArray()) owned.Close();
        Check(vm.IsProjectsVisible,"Closing help preserves the application screen");
        vm.ShowLibraryCommand.Execute(null);
        foreach(var item in new[]{("Fab","fab"),("Quixel","megascans"),("FabWeb","megascans"),("Unity","connectors")})
        { typeof(MainWindowViewModel).GetField("_librarySource",Private)!.SetValue(vm,item.Item1); Check(vm.ContextualDocumentationPage==item.Item2,"Library context maps "+item.Item1+" to its actual guide"); }
        vm.ShowSettingsCommand.Execute(null);
        vm.SelectedSettingsCategory=vm.SettingsCategories.Single(category=>category.Id=="appearance");
        vm.ShowContextualDocumentationCommand.Execute(null);
        Check(request is {Page:"settings",Anchor:"appearance-and-language"},"Appearance help opens its stable section");
        foreach(var owned in window.OwnedWindows.ToArray())owned.Close();
        vm.DocumentationLanguage="fr";
        await Task.Delay(100);
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadDocumentationLanguageAsync",Private)!.Invoke(vm,null)!;
        Check(vm.DocumentationLanguage=="fr","Help language remains persisted independently");
        _=ManualQuick; // Legacy automation flag remains accepted.
    }
}
