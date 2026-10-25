using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using CyEngineHub.Core.Ai;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Unreal.Models;

static partial class Program
{
    static async Task TestEditorBridge(Window window, MainWindowViewModel vm)
    {
        var arguments = Environment.GetCommandLineArgs();
        string? Argument(string name) { var index = Array.IndexOf(arguments, name); return index < 0 ? null : arguments[index + 1]; }
        var nativeEngine = Argument("--editor-native-engine"); var nativePlugin = Argument("--editor-native-plugin");
        var engine = vm.Engines.Single().Engine;
        if (nativeEngine is not null)
        {
            using var version = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(nativeEngine, "Engine/Build/Build.version")));
            var major = version.RootElement.GetProperty("MajorVersion").GetInt32(); var minor = version.RootElement.GetProperty("MinorVersion").GetInt32();
            engine = engine with { RootPath = nativeEngine, Version = new(major, minor, 0), EditorPath = Path.Combine(nativeEngine, "Engine/Binaries/Win64", major == 4 ? "UE4Editor.exe" : "UnrealEditor.exe") };
            Call(vm, "ReplaceCatalog", new[] { engine }, vm.Projects.Select(p => p.Project).ToArray());
        }
        var branch = $"{engine.Version.Major}.{engine.Version.Minor}";
        var project = vm.Projects.Single().Project;
        var fixtureSource = Path.Combine(Output, "EditorFixturePack");
        var sourceContent = Path.Combine(fixtureSource, "Content/CyHubQA"); Directory.CreateDirectory(sourceContent);
        var sourceFile = Path.Combine(sourceContent, "DefaultDiffuse.uasset");
        if (nativeEngine is null) await File.WriteAllTextAsync(sourceFile, "synthetic test payload, not a valid Unreal asset");
        else File.Copy(Path.Combine(nativeEngine, "Engine/Content/EngineMaterials/DefaultDiffuse.uasset"), sourceFile);
        var expected = await File.ReadAllBytesAsync(sourceFile);
        var asset = new RemoteFabAsset("editor-bridge-fixture", "fixture", "editor-bridge-fixture", "QA only", "Fab", "assetPack", null, null,
            ["QA"], [new RemoteFabProjectVersion("fixture-variant", [], [branch], ["Windows"])]);
        var client = (FixtureFab)typeof(MainWindowViewModel).GetField("_directFabClient", Private)!.GetValue(vm)!;
        client.Library = [asset]; client.EditorFixtureSource = fixtureSource;
        Call(vm, "ApplyDirectFabStatus", new DirectFabStatus(DirectFabConnectionState.Connected, "QA only"));
        Call(vm, "ReplaceRemoteAssets", (Func<RemoteFabAsset, bool>)(_ => true), client.Library);
        vm.Engines.Single().OpenDetailsCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 5);
        await ImGuiDiagnostics.ClickAsync(window, "action/PrepareEditorPluginCommand");
        Check(vm.InstallEditorPluginConfirmed, "Engine plugin installation has a separate explicit confirmation");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "editor-plugin-install-confirmation.png"));
        await ImGuiDiagnostics.ClickAsync(window, "action/CancelEditorPluginCommand");
        Check(!vm.InstallEditorPluginConfirmed, "Cancel does not compile or write to a real engine");
        var pipe = "CyEngineHub.Editor.Test." + Guid.NewGuid().ToString("N");
        Call(vm, "StartEditorBridge", pipe);
        try
        {
            Check(vm.EditorBridgeEnabled && !vm.McpEnabled, "Editor bridge works with AI/MCP disabled");
            async Task<JsonElement> Send(string method, string? id = null, bool confirmed = true, string? projectFile = null)
            {
                await using var connection = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await connection.ConnectAsync(5000);
                using var reader = new StreamReader(connection, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(connection, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { protocol = 1, engineVersion = branch, projectFile = projectFile ?? project.FilePath,
                    method, assetId = asset.AssetId, source = "Fab", variantId = "fixture-variant", requestId = id ?? Guid.NewGuid().ToString("N"), confirmed }));
                using var response = JsonDocument.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!);
                return response.RootElement.Clone();
            }
            Check(!(await Send("connect", projectFile: Path.Combine(Output, "missing.uproject"))).GetProperty("ok").GetBoolean(), "Unknown editor projects are refused");
            var search = await Send("search"); Check(search.GetProperty("ok").GetBoolean() && search.GetProperty("result").GetProperty("total").GetInt32() == 1, "Owned catalog crosses the real same-user pipe");
            Check(!(await Send("install", confirmed: false)).GetProperty("ok").GetBoolean(), "No installation without explicit confirmation");
            if (nativeEngine is not null)
            {
                if (nativePlugin is null) throw new InvalidOperationException("Provide --editor-native-plugin with matching compiled plugin.");
                var plugins = Path.Combine(project.RootPath, "Plugins/CyEngineHub"); CopyDirectoryForEditorQa(nativePlugin, plugins);
                await File.WriteAllTextAsync(project.FilePath, "{\"FileVersion\":3,\"Plugins\":[{\"Name\":\"CyEngineHub\",\"Enabled\":true}]}");
                var start = new ProcessStartInfo(engine.EditorPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                foreach (var arg in new[] { project.FilePath, engine.Version.Major == 4 ? "-RenderOffscreen" : "-NullRHI", "-Unattended", "-NoSplash", "-NoSound", "-NoCompile", "-NoLiveCoding", "-CyHubBridgeQAPipe=" + pipe,
                    "-CyHubExpectedPipe=\\\\.\\pipe\\" + CyEngineHub.Core.Editor.HubEditorTransport.PipeName,
                    "-ExecCmds=Automation RunTests CyEngineHub.Editor.PanelAndBridge", "-TestExit=Automation Test Queue Empty", "-AbsLog=" + Path.Combine(Output, "unreal-editor-bridge.log") }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!;
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(140));
                try { await process.WaitForExitAsync(deadline.Token); }
                catch (OperationCanceledException) { process.Kill(true); throw; }
                var log = await File.ReadAllTextAsync(Path.Combine(Output, "unreal-editor-bridge.log"));
                Check(process.ExitCode == 0 && (log.Contains("Result={Success}", StringComparison.Ordinal) || log.Contains("Result={Passed}", StringComparison.Ordinal)) && log.Contains("PanelAndBridge", StringComparison.Ordinal), "Real Unreal Slate panel, C++ IPC, confirmation, queue, replay and import completion pass");
            }
            else
            {
                var id = Guid.NewGuid().ToString("N"); var install = await Send("install", id);
                Check(install.GetProperty("ok").GetBoolean(), "Confirmed request queues the ordinary installation workflow: " + install);
                var taskId = install.GetProperty("result").GetProperty("taskId").GetString();
                var replay = await Send("install", id);
                Check(replay.GetProperty("result").GetProperty("taskId").GetString() == taskId, "Replaying a confirmed request cannot duplicate an installation");
                for (var i = 0; i < 200 && vm.IsEpicBusy; i++) await Task.Delay(50);
                var tasks = await Send("tasks");
                Check(tasks.GetProperty("result").EnumerateArray().Any(t => t.GetProperty("id").GetString() == taskId && t.GetProperty("status").GetString() == "Completed"), "Editor task history reports actual workflow completion");
            }
            var imported = Path.Combine(project.RootPath, "Content/CyHubQA/DefaultDiffuse.uasset");
            Check(File.Exists(imported) && (await File.ReadAllBytesAsync(imported)).SequenceEqual(expected), "Pack content arrives byte-identically only inside the open QA project");
            await ImGuiDiagnostics.FramesAsync(window, 5);
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "editor-bridge-installed.png"));
        }
        finally { vm.StopEditorBridge(); }
        Check(!vm.EditorBridgeEnabled && !vm.McpEnabled, "Disconnect does not change AI permissions");
    }
    static void CopyDirectoryForEditorQa(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        { var target = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); }
    }
}
