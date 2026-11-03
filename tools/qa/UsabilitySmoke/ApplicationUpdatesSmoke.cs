using Avalonia.Controls;
using CyEngineHub.Core.Settings;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Updates;

static partial class Program
{
    static async Task TestApplicationUpdates(Window window, MainWindowViewModel vm,
        ApplicationUpdateFixtureSettings settings, ApplicationUpdateFixtureService service, RecordingLauncher launcher)
    {
        // Exercise InitializeAsync itself; hold unrelated initialization pending.
        settings.GateTheme = true;
        service.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var initialize = vm.InitializeAsync();
        try
        {
            await Wait(() => settings.ThemeReached && service.Checks == 1);
            Check(!initialize.IsCompleted && vm.IsUpdateCheckActive,
                "Startup begins update metadata lookup before unrelated initialization can complete");
            Check(service.Repository == MainWindowViewModel.OfficialUpdateRepository && service.CurrentVersion == vm.ApplicationVersion && service.IncludePrereleases && service.HasCancellation,
                "Fresh alpha startup uses the official repository, semantic app version, alpha channel and bounded cancellation");
            await ImGuiDiagnostics.FramesAsync(window, 4);
            Check(ImGuiDiagnostics.Elements(window).Contains("updates/checking"), "Startup lookup has a visible sidebar indicator");
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "app-update-checking.png"));
            var release = new ApplicationRelease(new(99, 0, 0), "v99.0.0-alpha.1", "CyEngineHub · Fixture update",
                "Fixture release notes. No network access or real installer in this test.",
                new("https://github.com/MrMybal/CyEngineHub/releases/tag/v99.0.0-alpha.1"), DateTimeOffset.UtcNow,
                new("Fixture-Setup.exe", new("https://example.invalid/Fixture-Setup.exe"), 42), null, new string('a', 64));
            service.Release = release; service.Gate.SetResult(release); service.Gate = null;
            await Wait(() => vm.IsUpdateAvailablePopupVisible && !vm.IsUpdateCheckActive);
            Check(!initialize.IsCompleted && vm.AvailableUpdate == release && service.Downloads == 0 && launcher.Launches.Count == 0,
                "A newer release is proposed even while startup is pending, without automatic download or execution");
            await ImGuiDiagnostics.FramesAsync(window, 4);
            var elements = ImGuiDiagnostics.Elements(window);
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "app-update-proposal.png"));
            Check(new[] { "command/PostponeUpdateCommand/", "command/OpenAvailableUpdateReleaseCommand/", "command/DownloadAndInstallUpdateCommand/" }
                .All(prefix => elements.Any(element => element.StartsWith(prefix, StringComparison.Ordinal))),
                "The actual popup renders Later, release details and explicit installation controls");
            window.Width = 900; window.Height = 700;
            await ImGuiDiagnostics.FramesAsync(window, 4);
            foreach (var prefix in new[] { "command/PostponeUpdateCommand/", "command/OpenAvailableUpdateReleaseCommand/", "command/DownloadAndInstallUpdateCommand/" })
            {
                var element = ImGuiDiagnostics.Elements(window).Single(key => key.StartsWith(prefix, StringComparison.Ordinal));
                var bounds = ImGuiDiagnostics.ElementBounds(window, element);
                Check(bounds.X >= 0 && bounds.Z <= window.Width && bounds.Y >= 0 && bounds.W <= window.Height,
                    "Update dialog action remains visible at 900 × 700: " + prefix);
            }
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "app-update-proposal-narrow.png"));
            window.Width = 1280; window.Height = 820;
            await ImGuiDiagnostics.FramesAsync(window, 4);
            await ImGuiDiagnostics.ClickAsync(window, "command/OpenAvailableUpdateReleaseCommand");
            Check(launcher.OpenedUris.Last() == release.ReleaseUri && service.Downloads == 0, "Release details do not download an installer");
            await ImGuiDiagnostics.ClickAsync(window, "command/PostponeUpdateCommand");
            Check(!vm.IsUpdateAvailablePopupVisible && service.Downloads == 0, "Later dismisses the proposal without downloading");
            await ImGuiDiagnostics.FramesAsync(window, 4);
            Check(ImGuiDiagnostics.Elements(window).Contains("updates/available"), "A postponed release remains accessible in the sidebar");
            await ImGuiDiagnostics.ClickAsync(window, "action/ShowAvailableUpdateCommand");
            Check(vm.IsUpdateAvailablePopupVisible && service.Checks == 1, "The sidebar reopens the same proposal without another lookup");
            await ImGuiDiagnostics.EscapeAsync(window);
            Check(!vm.IsUpdateAvailablePopupVisible && service.Downloads == 0, "Escape postpones the update and releases modal input capture");
            await Invoke("CheckForUpdatesOnStartupAsync");
            Check(service.Checks == 1, "Repeated startup requests do not duplicate the automatic lookup");

            service.Release = release with { Installer = null, Checksum = null, ExpectedSha256 = null };
            await Invoke("CheckForUpdatesAsync", true);
            Check(vm.IsUpdateAvailablePopupVisible && !vm.CanInstallAvailableUpdate && service.Downloads == 0,
                "A release without a verifiable installer is still proposed with notes, but cannot auto-install");
            await ImGuiDiagnostics.FramesAsync(window, 4);
            await ImGuiDiagnostics.ClickAsync(window, "command/DownloadAndInstallUpdateCommand");
            Check(service.Downloads == 0, "The rendered installation button is disabled when release files are incomplete");
            vm.PostponeUpdateCommand.Execute(null);

            service.Release = null;
            await Invoke("CheckForUpdatesAsync", true);
            Check(vm.HasCheckedForUpdates && !vm.IsUpdateCheckFailed && vm.AvailableUpdate is null && !vm.IsUpdateAvailablePopupVisible,
                "An up-to-date result clears any stale proposal");
            await ImGuiDiagnostics.FramesAsync(window, 4);
            Check(ImGuiDiagnostics.Elements(window).Contains("updates/current"), "Successful lookup is visibly distinguished from no check");

            ResetStartup(); service.Failure = new HttpRequestException("Fixture offline");
            var priorStatus = vm.StatusMessage;
            await Invoke("CheckForUpdatesOnStartupAsync");
            Check(vm.IsUpdateCheckFailed && !vm.IsUpdateCheckActive && !vm.IsUpdateAvailablePopupVisible && vm.StatusMessage == priorStatus && vm.UpdateStatus.Contains("impossible"),
                "Offline startup logs a non-blocking failure, never claims current or opens an application error");
            await ImGuiDiagnostics.FramesAsync(window, 4);
            Check(ImGuiDiagnostics.Elements(window).Contains("updates/retry") && !ImGuiDiagnostics.Elements(window).Contains("updates/current"),
                "Offline status offers a visible retry instead of an up-to-date claim");
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "app-update-offline.png"));
            service.Failure = null;
            var beforeRetry = service.Checks;
            await ImGuiDiagnostics.ClickAsync(window, "action/CheckForUpdatesCommand");
            await Wait(() => service.Checks > beforeRetry && !vm.IsUpdateCheckActive);
            Check(!vm.IsUpdateCheckFailed && vm.UpdateStatus.Contains("à jour"), "Manual retry recovers after an offline startup");

            await settings.SetAsync(MainWindowViewModel.AutoCheckStableUpdatesSetting, "False");
            await settings.SetAsync(MainWindowViewModel.IncludePrereleaseUpdatesSetting, "False");
            await Invoke("LoadUpdateSettingsAsync"); ResetStartup();
            var beforeDisabled = service.Checks;
            await Invoke("CheckForUpdatesOnStartupAsync");
            Check(!vm.AutoCheckStableUpdates && !vm.IncludePrereleaseUpdates && service.Checks == beforeDisabled,
                "Persisted opt-out and stable-channel preferences remain respected");
            await Invoke("CheckForUpdatesAsync", true);
            Check(service.Checks == beforeDisabled + 1 && !service.IncludePrereleases,
                "Manual lookup remains possible with startup checks disabled and uses the saved channel");

            service.Release = release;
            await Invoke("CheckForUpdatesAsync", true);
            var ready = 0; vm.UpdateInstallerReady += (_, _) => ready++;
            await ImGuiDiagnostics.FramesAsync(window, 4);
            await ImGuiDiagnostics.ClickAsync(window, "command/DownloadAndInstallUpdateCommand");
            await Wait(() => ready == 1 && !vm.IsUpdateDownloadActive);
            Check(service.Downloads == 1 && launcher.Launches.Count == 0,
                "Only an explicit install click invokes the download service and verified-updater handoff");
        }
        finally
        {
            settings.AbortInitialization();
            await initialize;
        }

        Task Invoke(string name, params object[] args) => (Task)typeof(MainWindowViewModel).GetMethod(name, Private)!.Invoke(vm, args)!;
        void ResetStartup() => typeof(MainWindowViewModel).GetField("_startupUpdateCheckStarted", Private)!.SetValue(vm, false);
        static async Task Wait(Func<bool> condition)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!condition() && DateTime.UtcNow < until) await Task.Delay(20);
            Check(condition(), "Application update fixture completed its pending transition");
        }
    }

    sealed class ApplicationUpdateFixtureSettings(ISettingsStore inner) : ISettingsStore
    {
        readonly TaskCompletionSource<string?> _theme = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool GateTheme { get; set; }
        public bool ThemeReached { get; private set; }
        public Task<string?> GetAsync(string key, CancellationToken token = default)
        {
            if (GateTheme && key == "interface.theme.v1") { ThemeReached = true; return _theme.Task.WaitAsync(token); }
            return inner.GetAsync(key, token);
        }
        public Task SetAsync(string key, string value, CancellationToken token = default) => inner.SetAsync(key, value, token);
        public void AbortInitialization() => _theme.TrySetException(new OperationCanceledException("Fixture: stop unrelated initialization after update tests."));
    }

    sealed class ApplicationUpdateFixtureService : IApplicationUpdateService
    {
        public int Checks { get; private set; }
        public int Downloads { get; private set; }
        public string Repository { get; private set; } = "";
        public string CurrentVersion { get; private set; } = "";
        public bool IncludePrereleases { get; private set; }
        public bool HasCancellation { get; private set; }
        public ApplicationRelease? Release { get; set; }
        public Exception? Failure { get; set; }
        public TaskCompletionSource<ApplicationRelease?>? Gate { get; set; }
        public Task<ApplicationRelease?> CheckForUpdateAsync(string repository, Version version, CancellationToken token = default) => CheckForUpdateAsync(repository, version.ToString(), false, token);
        public async Task<ApplicationRelease?> CheckForUpdateAsync(string repository, string version, bool prereleases, CancellationToken token = default)
        {
            Checks++; Repository = repository; CurrentVersion = version; IncludePrereleases = prereleases; HasCancellation = token.CanBeCanceled;
            if (Failure is not null) throw Failure;
            return Gate is { } gate ? await gate.Task.WaitAsync(token) : Release;
        }
        public Task<VerifiedApplicationUpdate> DownloadAndVerifyAsync(ApplicationRelease release, string directory,
            IProgress<ApplicationUpdateProgress>? progress = null, CancellationToken token = default)
        {
            Downloads++;
            // Deliberately absent file: App's real updater is not wired into this fixture.
            return Task.FromResult(new VerifiedApplicationUpdate(Path.Combine(Output, "NOT-AN-INSTALLER.exe"), new string('a', 64)));
        }
    }
}
