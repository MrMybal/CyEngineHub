using System.Security.Cryptography;
using System.Text.Json;
using CyEngineHub.Core.Processes;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Infrastructure.Persistence;
using CyEngineHub.Projects.Creation;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

internal static class NativePackSmoke
{
    internal static async Task<int> RunAsync(string root, string source, UnrealEngineInstallation sourceEngine, UnrealEngineInstallation targetEngine, IProcessRunner runner)
    {
        Console.WriteLine("FULL_PACK_QA_ROOT " + root);
        var content=Path.Combine(source,"Content");
        string Fingerprint() => string.Join("\n", Directory.GetFiles(content,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(file =>
        { using var stream=File.OpenRead(file);return Path.GetRelativePath(content,file)+":"+Convert.ToHexString(SHA256.HashData(stream)); }));
        var before=Fingerprint();var checks=new List<string>();
        void Check(bool success,string message) {if(!success)throw new InvalidOperationException(message);checks.Add(message);Console.WriteLine("PASS "+message);}
        try
        {
            var creator=new ProjectCreationService();
            var target=await creator.CreateAsync(new(creator.Presets.Single(p=>p.Id=="unreal-blueprint"),"FullPackTarget",root,targetEngine.AssociationId,
                ["PythonScriptPlugin","EditorScriptingUtilities","CyHubNativeBridge"],creator.ConfigurationProfiles.Single(p=>p.Id=="unreal-ue5-lite")));
            var pluginRoot=Environment.GetEnvironmentVariable("CYHUB_NATIVE_ADAPTER_ROOT")??throw new InvalidOperationException("Explicit test adapter root required");
            foreach(var relative in new[]{"CyHubNativeBridge.uplugin","Binaries/Win64/UnrealEditor-CyHubNativeBridge.dll","Binaries/Win64/UnrealEditor.modules"})
            {var destination=Path.Combine(target.ProjectRoot,"Plugins","CyHubNativeBridge",relative);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(Path.Combine(pluginRoot,$"UE_5.{targetEngine.Version.Minor}",relative),destination);}
            var store=new SqliteCatalogStore(Path.Combine(root,"qa.db"));await store.InitializeAsync();
            await ((IEngineCatalog)store).UpsertAsync(sourceEngine);await ((IEngineCatalog)store).UpsertAsync(targetEngine);
            var project=new UnrealProject("qa-full","FullPackTarget",target.ProjectMarker,target.ProjectRoot,targetEngine.AssociationId,null,null,[],[],ProjectHealth.Healthy,[],DateTimeOffset.Now);
            var context=new FabInstallationScriptContext(new RemoteFabAsset("qa-complete-owned-pack","ue","Complete owned pack QA","","Fab","ASSET_PACK",null,null,[],[]),
                new RemoteFabProjectVersion("qa-57",[],["5.7"],["Windows"]),LibraryPackageContentKind.ContentPack,source,
                new("qa","QA full pack",FabInstallationTargetKind.Project,target.ProjectRoot,targetEngine.Version.ToString(),project,targetEngine),
                new Dictionary<string,string>{["sourceVersion"]="5.7",["conversionMode"]="native",["keepStaging"]="true"});
            var work=Path.Combine(root,"Bridge");
            var result=await new UnrealVersionBridgeInstallationScript(store,store,runner,new FabProjectContentImportService(),work,pluginRoot)
                .InstallAsync(context,new InlineProgress());
            Check(File.Exists(result.Outputs["manifest"]),"Complete native variant published after mandatory fresh target reload");
            var staging=Directory.GetDirectories(work,".staging",SearchOption.AllDirectories).Single();
            var inventoryFile=Directory.GetFiles(staging,"asset-inventory.json",SearchOption.AllDirectories).Single();
            using var inventory=JsonDocument.Parse(await File.ReadAllTextAsync(inventoryFile));
            var expected=inventory.RootElement.GetProperty("assets").EnumerateArray().Select(a=>a.GetProperty("path").GetString()!).ToArray();
            var reportFile=Directory.GetFiles(staging,"native-reopen-report.json",SearchOption.AllDirectories).Single();
            using var report=JsonDocument.Parse(await File.ReadAllTextAsync(reportFile));
            var actual=report.RootElement.GetProperty("imported").EnumerateArray().Select(a=>a.GetString()!).ToHashSet(StringComparer.Ordinal);
            Check(expected.Length>=54&&expected.All(actual.Contains)&&expected.Length==actual.Count,"Every original asset is present and verified; no subset/filtering");
            Check(expected.Count(p=>p.EndsWith("_C",StringComparison.Ordinal))>0,"Level Blueprint generated class is included");
            var exchange=Directory.GetDirectories(work,"Exchange",SearchOption.AllDirectories).Single();
            var config=Path.Combine(root,"verify-installed.json");var installedReport=Path.Combine(root,"verify-installed-report.json");
            await File.WriteAllTextAsync(config,JsonSerializer.Serialize(new {exchangeRoot=exchange,operation="verify",reportPath=installedReport}));
            var verified=await runner.RunAsync(new ProcessRequest {FileName=targetEngine.EditorPath,WorkingDirectory=target.ProjectRoot,
                Arguments=[target.ProjectMarker,"-run=pythonscript","-script="+Path.Combine(AppContext.BaseDirectory,"Extensions","cyenginehub.unreal-version-bridge","scripts","native-textures.py"),"-Unattended","-NullRHI","-NoP4","-abslog="+Path.Combine(root,"verify-installed.log")],
                EnvironmentVariables=new Dictionary<string,string?>{["CYENGINEHUB_VERSION_BRIDGE_CONFIG"]=config}});
            Check(verified.Succeeded,"Whole installed pack reopened and compared in a new target process");
            Check(before==Fingerprint(),"All original Content file hashes unchanged");
            await File.WriteAllTextAsync(Path.Combine(root,"full-pack-summary.json"),JsonSerializer.Serialize(new{sourceEngine=sourceEngine.Version.ToString(),targetEngine=targetEngine.Version.ToString(),assets=expected.Length,checks,result.Outputs},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception error)
        {Console.WriteLine(error);await File.WriteAllTextAsync(Path.Combine(root,"full-pack-error.txt"),error.ToString());return 1;}
        finally {if(before!=Fingerprint())throw new InvalidOperationException("Original pack changed during QA");}
    }
    private sealed class InlineProgress : IProgress<FabInstallationScriptProgress>
    {public void Report(FabInstallationScriptProgress value){if(!value.Message.StartsWith("[",StringComparison.Ordinal))Console.WriteLine(value.Message);}}
}
