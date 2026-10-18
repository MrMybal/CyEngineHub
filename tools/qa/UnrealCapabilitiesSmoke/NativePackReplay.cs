using System.Text.Json;
using CyEngineHub.Core.Processes;
using CyEngineHub.Projects.Creation;
using CyEngineHub.Unreal.Models;

internal static class NativePackReplay
{
    internal static async Task<int> ShadersAsync(string root,string project,UnrealEngineInstallation engine,IProcessRunner runner)
    {
        var report=Path.Combine(root,"shader-validation.json");Console.WriteLine("SHADER_QA_REPORT "+report);
        var result=await runner.RunAsync(new ProcessRequest {FileName=engine.EditorPath,WorkingDirectory=Path.GetDirectoryName(project),
            Arguments=[project,"-run=pythonscript","-script="+Path.Combine(AppContext.BaseDirectory,"scripts","native-pack-shaders.py"),"-Unattended","-NoP4","-AllowCommandletRendering","-RenderOffscreen","-NoSound","-D3D12","-sm6","-abslog="+Path.Combine(root,"shader-validation.log")],
            EnvironmentVariables=new Dictionary<string,string?>{["CYHUB_SHADER_REPORT"]=report}});
        if(!result.Succeeded||!File.Exists(report))return 1;
        Console.WriteLine("PASS Real target shader compilation and both maps loaded through rendering RHI");return 0;
    }
    internal static async Task<int> RunAsync(string root,string exchange,UnrealEngineInstallation engine,IProcessRunner runner)
    {
        Console.WriteLine("REPLAY_DIAGNOSTIC_ROOT "+root);
        var creator=new ProjectCreationService();var project=await creator.CreateAsync(new(creator.Presets.Single(p=>p.Id=="unreal-blueprint"),"ReplayTarget",root,engine.AssociationId,
            ["PythonScriptPlugin","EditorScriptingUtilities","CyHubNativeBridge"],creator.ConfigurationProfiles.Single(p=>p.Id=="unreal-ue5-lite")));
        var adapters=Environment.GetEnvironmentVariable("CYHUB_NATIVE_ADAPTER_ROOT")??throw new InvalidOperationException("Explicit QA adapter root required");
        foreach(var relative in new[]{"CyHubNativeBridge.uplugin","Binaries/Win64/UnrealEditor-CyHubNativeBridge.dll","Binaries/Win64/UnrealEditor.modules"})
        {var destination=Path.Combine(project.ProjectRoot,"Plugins","CyHubNativeBridge",relative);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(Path.Combine(adapters,$"UE_5.{engine.Version.Minor}",relative),destination);}
        foreach(var operation in new[]{"import","verify"})
        {
            var config=Path.Combine(root,operation+".json");var report=Path.Combine(root,operation+"-report.json");
            await File.WriteAllTextAsync(config,JsonSerializer.Serialize(new{exchangeRoot=exchange,operation,reportPath=report}));
            var result=await runner.RunAsync(new ProcessRequest{FileName=engine.EditorPath,WorkingDirectory=project.ProjectRoot,
                Arguments=[project.ProjectMarker,"-run=pythonscript","-script="+Path.Combine(AppContext.BaseDirectory,"Extensions","cyenginehub.unreal-version-bridge","scripts","native-textures.py"),"-Unattended","-NullRHI","-NoP4","-abslog="+Path.Combine(root,operation+".log")],
                EnvironmentVariables=new Dictionary<string,string?>{["CYENGINEHUB_VERSION_BRIDGE_CONFIG"]=config}});
            if(!result.Succeeded){Console.WriteLine("FAILED "+operation+" · "+report);if(File.Exists(report)){using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(report));Console.WriteLine(doc.RootElement.GetProperty("errors").ToString());}return 1;}
            Console.WriteLine("PASS Diagnostic "+operation+" completed; not a published/installed full-pack validation");
        }
        return 0;
    }
}
