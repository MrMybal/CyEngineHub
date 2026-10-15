using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Desktop.Views;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Engines;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Infrastructure.AppData;
using CyEngineHub.Infrastructure.Persistence;
using CyEngineHub.Unreal.Integration;
using CyEngineHub.Unreal.Registration;

sealed class TestApp : Application
{
    public override void Initialize() { RequestedThemeVariant = ThemeVariant.Dark; Styles.Add(new FluentTheme()); }
}
sealed class Registration : IUnrealEngineRegistrationService
{
    public UnrealEngineRegistration? FindByRoot(string p) => null;
    public UnrealEngineRegistration Register(string p) => throw new NotSupportedException();
    public bool Unregister(string p) => throw new NotSupportedException();
}
sealed class NoSession : IQuixelSessionStore, IDirectFabSessionStore
{
    Task<QuixelSession?> IQuixelSessionStore.LoadAsync(CancellationToken c) => throw new InvalidOperationException("No account access allowed.");
    Task<DirectFabSession?> IDirectFabSessionStore.LoadAsync(CancellationToken c) => throw new InvalidOperationException("No account access allowed.");
    public Task SaveAsync(QuixelSession s, CancellationToken c) => throw new NotSupportedException();
    public Task SaveAsync(DirectFabSession s, CancellationToken c) => throw new NotSupportedException();
    public Task ClearAsync(CancellationToken c) => throw new NotSupportedException();
}
sealed class FixtureHttp : HttpMessageHandler
{
    public bool Offline;
    public TaskCompletionSource? DetailGate;
    public int DetailCalls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (Offline) throw new HttpRequestException("Offline fixture");
        object payload;
        if (request.RequestUri!.AbsolutePath.EndsWith("/collections/query", StringComparison.Ordinal))
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var page = body.RootElement.GetProperty("page").GetInt32();
            payload = new { nbPages = 3, hits = Enumerable.Range(page * 100, Math.Min(100, 208 - page * 100)).Select(i => new
            {
                objectID = "collection-" + i,
                name = i % 2 == 0 ? "Urban collection " + i : "Forest collection " + i,
                collectionPath = i % 2 == 0 ? "Environment | Urban" : "Environment | Natural"
            }).ToArray() };
        }
        else
        {
            DetailCalls++;
            if (DetailGate is not null) await DetailGate.Task.WaitAsync(token);
            payload = new { id = request.RequestUri.Segments[^1], name = "Urban collection", collectionPath = "Environment | Urban",
                assets = new[] { "owned0001", "owned0002", "notowned" }, presentation = new { subtitle = "Fixture: two owned assets out of three." } };
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
    }
}
static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly string Output = Path.GetFullPath("artifacts/quixel-collections-qa-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
    static Task Call(object vm, string method, params object[] args) => (Task)vm.GetType().GetMethod(method, Private)!.Invoke(vm, args)!;
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
    [STAThread] static void Main(string[] args)
    {
        Directory.CreateDirectory(Output);
        AppBuilder.Configure<TestApp>().UsePlatformDetect().SetupWithoutStarting();
        using var stop = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Run(args.Contains("--live", StringComparer.Ordinal)); }
            catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
            finally { stop.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(stop.Token);
    }

    static async Task Run(bool live)
    {
        var paths = new AppDataPaths(Output, Path.Combine(Output, "qa.db"), Path.Combine(Output, "Logs"), true);
        var store = new SqliteCatalogStore(paths.DatabasePath);
        await store.InitializeAsync();
        await using var scheduler = new HubTaskScheduler(1);
        using var fixture = new FixtureHttp();
        using var http = live ? new HttpClient() : new HttpClient(fixture);
        http.Timeout = TimeSpan.FromSeconds(30);
        var client = new QuixelMegascansApiClient(http, new NoSession(), new NoSession(),
            algoliaSearchKey: live ? null : "fixture-search-only");
        var ctor = typeof(MainWindowViewModel).GetConstructors().Single();
        MainWindowViewModel MakeVm()
        {
            var vm = (MainWindowViewModel)ctor.Invoke(ctor.GetParameters().Select(p => p.Name switch
            {
                "database" or "settings" or "engineCatalog" => (object)store,
                "engineModules" => new EngineModuleRegistry([new UnrealEngineModule()]),
                "engineRegistration" => new Registration(), "taskScheduler" => scheduler,
                "appData" => paths, "quixelClient" => client, _ => null
            }).ToArray());
            var assets = Enumerable.Range(0, 18800).Select(i => new RemoteFabAsset("owned" + i.ToString("D4"), "megascans",
                "Owned asset " + i, "Demo", "quixel", "quixel-download", null, null, ["Megascans"], [])).Append(
                new RemoteFabAsset("fab-only", "fab", "Fab asset", "Demo", "fab", "assetPack", null, null, [], [])).ToArray();
            typeof(MainWindowViewModel).GetField("_remoteFabCatalog", Private)!.SetValue(vm, assets);
            typeof(MainWindowViewModel).GetMethod("ApplyQuixelStatus", Private)!.Invoke(vm, [new QuixelStatus(QuixelConnectionState.Connected, "Fixture catalog")]);
            vm.ShowLibraryQuixelCommand.Execute(null);
            return vm;
        }
        var vm = MakeVm();
        var window = new MainWindow { DataContext = vm, Width = 1380, Height = 870, ShowActivated = false,
            ShowInTaskbar = false, Position = new PixelPoint(-3000, -3000) };
        window.Show();
        try
        {
            vm.ShowQuixelCollectionsTabCommand.Execute(null);
            await WaitFor(() => !vm.IsQuixelCollectionsBusy && vm.QuixelCollectionCards.Count > 0);
            Check(vm.IsQuixelCollectionsListVisible && !vm.IsRemoteMarketplaceContentVisible, "Separate collections tab");
            Console.WriteLine("CATALOG " + vm.QuixelCollectionsStatus);
            Check(live ? vm.QuixelCollectionCards.Count > 0 : vm.QuixelCollectionCards.Count == 208, "Paginated collections catalog loaded");
            await Task.Delay(live ? 3000 : 250);
            await Capture(window, "collections");
            ListBox? list = null;
            if (ImGuiDiagnostics.IsHosted(window))
            {
                await ImGuiDiagnostics.FramesAsync(window);
                Check(ImGuiDiagnostics.VisibleRows(window, "QuixelCollectionCards") is > 0 and < 25, "Only visible ImGui collection rows are built");
            }
            else
            {
                var panel = window.GetVisualDescendants().OfType<QuixelCollectionsPanel>().Single();
                list = panel.GetVisualDescendants().OfType<ListBox>().Single();
                Check(list.GetVisualDescendants().OfType<ListBoxItem>().Count() < 25, "Only visible collection rows are built");
            }
            if (live)
            {
                Check(vm.QuixelCollectionCards.Any(c => c.HasThumbnail), "Live publisher cover images loaded asynchronously");
                var detail = await client.GetCollectionAsync("xeric-shrublands");
                Check(detail.AssetIds?.Count > 100, "Live official collection membership accessible without session");
                Console.WriteLine("LIVE collections=" + vm.QuixelCollectionCards.Count + "; Xeric assets=" + detail.AssetIds!.Count);
                return;
            }
            vm.QuixelCollectionsSearch = "URBAN";
            Check(vm.QuixelCollectionCards.Count == 104, "Case-insensitive contains search");
            vm.QuixelCollectionsSearch = "no-such-collection";
            Check(vm.HasNoQuixelCollections, "Empty search result state");
            vm.QuixelCollectionsSearch = "";
            if (ImGuiDiagnostics.IsHosted(window)) ImGuiDiagnostics.ScrollListToEnd(window, "QuixelCollectionCards");
            else list!.ScrollIntoView(vm.QuixelCollectionCards[^1]);
            await Task.Delay(250);
            Check(ImGuiDiagnostics.IsHosted(window) ? ImGuiDiagnostics.VisibleRows(window, "QuixelCollectionCards") < 25
                : list!.GetVisualDescendants().OfType<ListBoxItem>().Count() < 25, "Scrolling keeps realization bounded");
            await Call(vm, "OpenQuixelCollectionAsync", "collection-0");
            Check(vm.IsQuixelCollectionScopeVisible && vm.IsRemoteMarketplaceContentVisible, "Collection opens regular asset catalog");
            Check(SourceAssets(vm).Select(a => a.AssetId).SequenceEqual(["owned0001", "owned0002"]), "Only owned collection assets shown");
            Check(vm.QuixelCollectionScopeSummary.StartsWith("2 / 3", StringComparison.Ordinal), "Membership versus owned count is explicit");
            await Capture(window, "collection-assets");
            vm.ShowQuixelAssetsTabCommand.Execute(null);
            Check(SourceAssets(vm).Count() == 18800, "Mes assets restores full library");
            vm.ShowLibraryFabCommand.Execute(null);
            Check(SourceAssets(vm).Single().AssetId == "fab-only" && !vm.IsQuixelCollectionsListVisible, "Fab source is unaffected");
            vm.ShowLibraryQuixelCommand.Execute(null);
            vm.ShowQuixelCollectionsTabCommand.Execute(null);
            await WaitFor(() => !vm.IsQuixelCollectionsBusy);
            fixture.DetailGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = Call(vm, "OpenQuixelCollectionAsync", "collection-1");
            vm.ShowQuixelAssetsTabCommand.Execute(null);
            fixture.DetailGate.SetResult();
            await pending;
            Check(!vm.IsQuixelCollectionScopeVisible && vm.IsQuixelAssetsTabSelected, "Late network result cannot hijack navigation");
            fixture.DetailGate = null;
            fixture.Offline = true;
            var restored = MakeVm();
            restored.ShowQuixelCollectionsTabCommand.Execute(null);
            await WaitFor(() => !restored.IsQuixelCollectionsBusy && restored.QuixelCollectionCards.Count > 0);
            Check(restored.QuixelCollectionCards.Count == 208, "Catalog restored from disk offline");
            await Call(restored, "OpenQuixelCollectionAsync", "collection-0");
            Check(SourceAssets(restored).Count() == 2, "Visited collection content works offline");
            await Call(restored, "LoadQuixelCollectionsAsync", true);
            Check(restored.QuixelCollectionCards.Count == 208 && !restored.IsQuixelCollectionsBusy, "Failed refresh preserves cache and releases spinner");
        }
        finally { window.Close(); Console.WriteLine("OUTPUT " + Output); }
    }
    static IEnumerable<RemoteFabAsset> SourceAssets(MainWindowViewModel vm) =>
        (IEnumerable<RemoteFabAsset>)typeof(MainWindowViewModel).GetMethod("GetLibrarySourceAssets", Private)!.Invoke(vm, null)!;
    static async Task WaitFor(Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!predicate()) { if (DateTime.UtcNow >= until) throw new TimeoutException(); await Task.Delay(30); }
    }
    static async Task Capture(Window window, string name)
    {
        if (ImGuiDiagnostics.IsHosted(window)) { await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, name + ".png")); return; }
        await Task.Delay(250);
        using var bitmap = new RenderTargetBitmap(new PixelSize(1380, 870));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(Output, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
