using Avalonia.Controls;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;

static partial class Program
{
    sealed class FailingForceScript : IFabInstallationScript
    {
        public FabInstallationScriptDescriptor Descriptor { get; } = new("qa.deliberate-failure", "QA · échec explicite", "QA", "QA",
            [LibraryPackageContentKind.ContentPack], [FabInstallationTargetKind.Project], [], true);
        public Task<FabInstallationScriptResult> InstallAsync(FabInstallationScriptContext context,
            IProgress<FabInstallationScriptProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromException<FabInstallationScriptResult>(new IOException("Fixture: deliberate script failure"));
    }
    sealed class ChosenForceScript : IFabInstallationScript
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FabInstallationScriptContext? Seen { get; private set; }
        // Deliberately restrictive metadata: the checked override must still call the chosen method.
        public FabInstallationScriptDescriptor Descriptor { get; } = new("qa.chosen-force", "QA · méthode choisie", "Synthetic chosen method", "QA",
            [LibraryPackageContentKind.RawAsset], [FabInstallationTargetKind.Engine],
            [new("chosenValue", "Option choisie", "Fixture", FabInstallationOptionKind.Text, "initial", [])], true);
        public async Task<FabInstallationScriptResult> InstallAsync(FabInstallationScriptContext context,
            IProgress<FabInstallationScriptProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Seen = context;
            await Release.Task.WaitAsync(cancellationToken);
            var result = await new UnrealContentPackInstallationScript(new FabProjectContentImportService()).InstallAsync(context, progress, cancellationToken);
            return result with { ScriptId = Descriptor.Id };
        }
    }

    static async Task TestForcedInstallChoice(Window window, MainWindowViewModel vm, FixtureFab client, string fixtureSource)
    {
        async Task Wait(Func<bool> predicate)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!predicate()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Forced install fixture did not settle: " + vm.FabInstallActionError); await ImGuiDiagnostics.FramesAsync(window, 2); }
            await ImGuiDiagnostics.FramesAsync(window, 3);
        }
        vm.OpenFabInstallOptionsCommand.Execute(null);
        var target = vm.FabInstallTargets.Single();
        target.IsSelected = true;
        var registry = (IFabInstallationScriptRegistry)typeof(MainWindowViewModel).GetField("_fabInstallationScripts", Private)!.GetValue(vm)!;
        var chosen = new ChosenForceScript(); registry.Register(chosen);
        var selectedMethod = new FabInstallationScriptOptionViewModel(chosen.Descriptor);
        target.AvailableInstallationScripts.Add(selectedMethod);
        target.SelectedInstallationScript = selectedMethod;
        target.InstallationConfiguration.Single(option => option.Id == "chosenValue").Value = "user-selected-A";
        foreach (var version in vm.FabInstallVersions) version.IsSelected = false;
        vm.FabInstallVersions.Add(new(new("force-other-platform", [], ["UE_5.7"], [OperatingSystem.IsWindows() ? "Mac" : "Windows"]), true));
        typeof(MainWindowViewModel).GetProperty("IsEpicBusy")!.SetValue(vm, true);
        await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(!vm.InstallSelectedFabVersionsCommand.CanExecute(null), "Fixture reproduces the grayed normal button during Epic refresh");
        await ImGuiDiagnostics.ClickAsync(window, "check/BypassFabInstallChecks");
        Check(vm.BypassFabInstallChecks && vm.InstallSelectedFabVersionsCommand.CanExecute(null) && vm.FabInstallActionLabel == "Installer quand même",
            "The actual override checkbox makes installation clickable despite background busy, version, platform and descriptor metadata");
        foreach (var dimensions in new[] { (1024, 650), (1440, 900) })
        {
            window.Width = dimensions.Item1; window.Height = dimensions.Item2;
            await ImGuiDiagnostics.FramesAsync(window, 5);
            var button = ImGuiDiagnostics.Elements(window).First(key => key.StartsWith("command/InstallSelectedFabVersionsCommand/", StringComparison.Ordinal));
            var bounds = ImGuiDiagnostics.ElementBounds(window, button);
            Check(bounds.Y > 0 && bounds.W < dimensions.Item2 && bounds.X > 0 && bounds.Z < dimensions.Item1,
                "The actual override action remains in the visible fixed footer at " + dimensions);
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "fab-force-option-" + dimensions.Item1 + ".png"));
        }
        var downloadCopy = Path.Combine(Output, "ForceDownloadTemp");
        Directory.CreateDirectory(Path.Combine(downloadCopy, "Content", "ManualChoice"));
        File.Copy(Path.Combine(fixtureSource, "Content", "ManualChoice", "Fixture.uasset"), Path.Combine(downloadCopy, "Content", "ManualChoice", "Fixture.uasset"));
        client.EditorFixtureSource = downloadCopy;
        await ImGuiDiagnostics.ClickAsync(window, "command/InstallSelectedFabVersionsCommand");
        await Wait(() => chosen.Seen is not null);
        Check(chosen.Seen!.Version.ArtifactId == "force-other-platform" && chosen.Seen.Configuration["chosenValue"] == "user-selected-A",
            "Clicking Installer quand même executes the exact chosen method and options with the selected mismatched version/platform");
        vm.OpenFabInstallOptionsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(ReferenceEquals(vm.FabInstallTargets.Single(), target) && target.SelectedInstallationScript!.Id == chosen.Descriptor.Id,
            "Reopening the window does not replace the selected method or choices");
        target.InstallationConfiguration.Single(option => option.Id == "chosenValue").Value = "edited-after-queue";
        var failing = new FailingForceScript(); registry.Register(failing);
        var failingOption = new FabInstallationScriptOptionViewModel(failing.Descriptor);
        target.AvailableInstallationScripts.Add(failingOption); target.SelectedInstallationScript = failingOption;
        client.FailNextDownload = false;
        Check(vm.InstallSelectedFabVersionsCommand.CanExecute(null), "Override stays clickable even with an installation in progress");
        await ImGuiDiagnostics.ClickAsync(window, "command/InstallSelectedFabVersionsCommand");
        chosen.Release.TrySetResult();
        await Wait(() => !vm.IsFabTargetInstallationActive);
        Check(chosen.Seen.Configuration["chosenValue"] == "user-selected-A", "Queued work keeps the options that were selected when clicked");
        Check(vm.IsEpicBusy && vm.InstallSelectedFabVersionsCommand.CanExecute(null), "Completed or failed work cannot leave the override grayed; background busy is untouched");
        Check(vm.FabInstallActionError.Contains("Échec", StringComparison.Ordinal), "An actual script/transfer failure is reported instead of silently disabling the override");
        Check(File.Exists(Path.Combine(target.Path, "Content", "ManualChoice", "Fixture.uasset")), "The chosen method performed the real isolated import");
        vm.OpenFabInstallOptionsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 4);
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "fab-force-retry-after-failure.png"));
        await ImGuiDiagnostics.ClickAsync(window, "command/CloseFabInstallOptionsCommand");
        typeof(MainWindowViewModel).GetProperty("IsEpicBusy")!.SetValue(vm, false);
    }
}
