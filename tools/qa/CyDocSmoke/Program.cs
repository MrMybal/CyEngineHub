using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CyEngineHub.Desktop.Documentation;
using CyEngineHub.Desktop.Views;
using Microsoft.Web.WebView2.Core;

internal sealed class SmokeApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window=new Window {Title="CyDoc offline QA",Width=1280,Height=850,ShowActivated=false,ShowInTaskbar=false,Position=new(-3000,-3000)};
            desktop.MainWindow=window;
            window.Opened+=(_,_)=>Dispatcher.UIThread.Post(async()=>
            {
                try { await Program.Run(window); desktop.Shutdown(0); }
                catch(Exception error) { Console.Error.WriteLine(error); desktop.Shutdown(1); }
            });
        }
    }
}
internal static class Program
{
    static readonly string Output=Path.GetFullPath(Path.Combine("artifacts","cydoc-qa-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
    static int Checks;
    [STAThread] static int Main(string[] args) => AppBuilder.Configure<SmokeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    static void Check(bool value,string message) { if(!value)throw new InvalidOperationException(message);Console.WriteLine("PASS "+message);Checks++; }
    internal static async Task Run(Window window)
    {
        Directory.CreateDirectory(Output);
        var root=Path.Combine(AppContext.BaseDirectory,"Help");
        var location=new DocumentationLocation(root);
        using var view=new DocumentationWebView(location,location.CreateUri("welcome"),Path.Combine(Output,"Profile"));
        var ready=new TaskCompletionSource();
        view.Ready+=(_,_)=>ready.TrySetResult(); view.Failed+=(_,message)=>ready.TrySetException(new InvalidOperationException(message));
        window.Content=view;
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(40));
        var web=view.Browser!;
        async Task<string> Js(string script)=>await web.ExecuteScriptAsync(script);
        async Task Until(string predicate)
        {
            for(var i=0;i<100;i++){if(await Js(predicate)=="true")return;await Task.Delay(50);}
            throw new TimeoutException("Reader condition: "+predicate);
        }
        await Until("!!document.querySelector('main article')");
        Check(web.Source.StartsWith("file:///",StringComparison.OrdinalIgnoreCase),"Actual WebView2 opens file:// without a server");
        Check(await Js("document.querySelector('#app').dataset.theme === 'dark'")=="true","Application-coherent default dark theme");
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"manifest.json")));
        var chapters=manifest.RootElement.GetProperty("pages").EnumerateArray().Where(p=>p.GetProperty("order").GetInt32()<25&&p.GetProperty("section").GetString()!="SDK API").ToArray();
        foreach(var page in chapters)
        {
            var id=page.GetProperty("id").GetString()!;var locale=page.GetProperty("locale").GetString()!;
            await Js("location.hash="+JsonSerializer.Serialize("#/"+locale+"/"+id));
            await Until("document.querySelector('.article-title h1')?.textContent==="+JsonSerializer.Serialize(page.GetProperty("title").GetString()));
            Check(await Js("!!document.querySelector('main article') && !document.querySelector('main .callout')?.textContent.includes('Page not found')")=="true",locale+"/"+id+" renders locally");
        }
        Check(chapters.Length==50,"All 25 bilingual chapters exercised");
        Check(!typeof(DocumentationLocation).Assembly.GetManifestResourceNames().Any(name=>name.StartsWith("CyEngineHub.Manual.",StringComparison.Ordinal)),"Legacy embedded manual payload is removed");
        await Js("window.loadedScreenshots=0;for(const name of ['engines','project-creation','source-conversion']){const image=new Image();image.onload=()=>window.loadedScreenshots++;image.src='assets/screenshots/'+name+'.png';}");
        await Until("window.loadedScreenshots===3");Check(true,"All three bundled PNG resources decode locally");
        view.Navigate(location.CreateUri("engines","fr",media:"engines"));
        await Until("!!document.querySelector('.cy-image .image-layer img')?.complete && document.querySelector('.cy-image .image-layer img').naturalWidth>0");
        Check(await Js("document.querySelector('.cy-image').classList.contains('media-minimal')")=="true","Local screenshot uses the minimal image module presentation");
        var exportPath=Path.Combine(Output,"image-annotations.json");
        var exportStarted=new TaskCompletionSource<bool>();
        web.DownloadStarting+=(_,args)=>
        {
            args.ResultFilePath=exportPath;args.Handled=true;
            exportStarted.TrySetResult(!args.Cancel);
        };
        await Js("document.querySelector('[data-image-export]').click()");
        Check(await exportStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)),"Native host permits the reader's local annotation export");
        for(var attempt=0;attempt<100&&(!File.Exists(exportPath)||new FileInfo(exportPath).Length==0);attempt++)await Task.Delay(50);
        using(var exported=JsonDocument.Parse(await File.ReadAllTextAsync(exportPath)))Check(exported.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array,"Annotation export writes valid JSON only to the isolated test folder");
        async Task Key(string key,string code,int number,int modifiers=0)
        {
            foreach(var type in new[]{"keyDown","keyUp"})await web.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type,key,code,windowsVirtualKeyCode=number,modifiers}));
        }
        await Js("document.querySelector('.media-stage').focus()");
        await Key("+","Equal",187);
        Check(await Js("document.querySelector('.zoom-value').textContent!=='100%'")=="true","Keyboard zoom works in the actual browser");
        await Key("0","Digit0",48);
        await web.CallDevToolsProtocolMethodAsync("Emulation.setTouchEmulationEnabled","{\"enabled\":true}");
        var point=JsonDocument.Parse(await Js("(()=>{const r=document.querySelector('.image-pan').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()"));
        var tx=point.RootElement.GetProperty("x").GetDouble();var ty=point.RootElement.GetProperty("y").GetDouble();
        var before=await Js("document.querySelector('.image-layer img').style.transform");
        foreach(var step in new[]{("touchStart",0),("touchMove",40),("touchEnd",40)})
            await web.CallDevToolsProtocolMethodAsync("Input.dispatchTouchEvent",JsonSerializer.Serialize(new{type=step.Item1,touchPoints=step.Item1=="touchEnd"?Array.Empty<object>():new object[]{new{x=tx+step.Item2,y=ty,id=0}}}));
        Check(await Js("document.querySelector('.image-layer img').style.transform")!=before,"Emulated touch pans the local screenshot");
        await web.CallDevToolsProtocolMethodAsync("Emulation.setTouchEmulationEnabled","{\"enabled\":false}");
        await Js("document.querySelector('[data-action=expand]').click()");
        Check(await Js("!!document.querySelector('.media-expanded') && !document.fullscreenElement")=="true","Image enlargement stays inside the window");
        await Js("document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))");
        Check(await Js("!document.querySelector('.media-expanded')")=="true","Escape dismisses enlargement");
        await Js("document.querySelector('[data-search]').click(); const q=document.querySelector('.search-form input');q.value='plugin';q.dispatchEvent(new Event('input',{bubbles:true}));");
        Check(await Js("document.querySelector('dialog.search-dialog').open && document.querySelectorAll('.search-result').length>0")=="true","Local search returns results");
        await Js("document.querySelector('dialog.search-dialog').close()");
        await Key("k","KeyK",75,2);
        Check(await Js("document.querySelector('dialog.search-dialog').open")=="true","Ctrl+K opens accessible local search");
        await Key("Escape","Escape",27);
        view.Navigate(location.CreateUri("projects","en","required-choices", "project-creation"));
        await Until("!!document.querySelector('[data-cydoc-id=project-creation] img')?.complete && document.querySelector('[data-cydoc-id=project-creation] img').naturalWidth>0");
        Check(web.Source.Contains("/required-choices?media=project-creation",StringComparison.Ordinal),"Contextual section and media routes survive native URI navigation");
        await Js("location.hash='#/en/engine-updates'");
        await Until("!!document.querySelector('main svg[role=img]')");
        Check(await Js("!document.querySelector('[data-cydoc-module=graph]')")=="true","Accessible SVG flow needs no graph module");
        await using(var capture=File.Create(Path.Combine(Output,"reader-en.png")))await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,capture);
        await Js("document.querySelector('[data-set-theme=light]').click()");
        web.Reload();await Task.Delay(700);
        await Until("document.querySelector('#app').dataset.theme==='light'");
        Check(true,"Reader theme survives reload");
        view.Navigate(location.CreateUri("engines"));
        await Until("document.querySelector('.article-title h1')?.textContent==='Engines and associations'");
        await Task.Delay(600);
        var targetTop=double.Parse(await Js("Math.min(600,Math.max(0,document.documentElement.scrollHeight-innerHeight))"),System.Globalization.CultureInfo.InvariantCulture);
        Check(targetTop>100,"Long article has a useful reading position");
        await Js("window.scrollTo({top:"+targetTop.ToString(System.Globalization.CultureInfo.InvariantCulture)+",left:0,behavior:'instant'})");
        await Until("JSON.parse(localStorage.getItem('cyenginehub.help.position:'+location.hash)||'{}').top==="+targetTop.ToString(System.Globalization.CultureInfo.InvariantCulture));
        web.Reload();await Task.Delay(700);
        await Until("Math.abs(window.scrollY-"+targetTop.ToString(System.Globalization.CultureInfo.InvariantCulture)+")<3");
        Check(true,"Reading position survives reloading the document");
        window.Width=640;await Task.Delay(200);
        Check(await Js("document.documentElement.scrollWidth<=innerWidth+1")=="true","Narrow reader has no horizontal document overflow");
        await Js("document.querySelector('[data-menu]').click()");
        Check(await Js("document.querySelector('#app').classList.contains('menu-open')")=="true","Narrow-screen navigation remains available");
        window.Width=1280;await Task.Delay(100);
        view.Navigate(location.CreateUri("reference-docs-source-converters","fr"));
        await Until("document.querySelector('article')?.textContent.includes('Preserved reference')===true");
        Check(await Js("document.querySelector('main .callout')?.textContent.includes('Référence originale en français')===true && document.querySelector('article').lang==='fr'")=="true","Original-language fallback is explicit and accessible");
        await using(var capture=File.Create(Path.Combine(Output,"reader-reference-fr.png")))await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,capture);
        Console.WriteLine($"CyDoc QA: {Checks} checks, {Output}");
    }
}
