using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using CyEngineHub.Core.Ai;
using CyEngineHub.Core.Settings;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Remote;

static partial class Program
{
    [DllImport("cimgui", EntryPoint = "cyhub_chat_state")]
    private static extern nint AssistantState(nint chat);
    [DllImport("cimgui", EntryPoint = "cyhub_chat_send")]
    private static extern nint SendToAssistant(nint chat, [MarshalAs(UnmanagedType.LPUTF8Str)] string prompt);
    [DllImport("cimgui", EntryPoint = "cyhub_chat_cancel")]
    private static extern void CancelAssistant(nint chat);
    [DllImport("cimgui", EntryPoint = "cyhub_mcp_runtime")]
    private static extern nint AssistantRuntime();

    static async Task TestAssistant(Window window, MainWindowViewModel vm, ISettingsStore settings)
    {
        await using var provider = new AssistantFixture();
        using var runtime = JsonDocument.Parse(Marshal.PtrToStringUTF8(AssistantRuntime())!);
        using var dependency = JsonDocument.Parse(File.ReadAllText("native/CyEngineHub.CyUI/dependencies.lock.json"));
        var pinned = dependency.RootElement.GetProperty("cymcp");
        Check(runtime.RootElement.GetProperty("revision").GetString() == pinned.GetProperty("revision").GetString() &&
              runtime.RootElement.GetProperty("version").GetString() == pinned.GetProperty("version").GetString(),
            "The running desktop loads the SDK version and revision pinned by the build");
        await settings.SetAsync("ai.assistant.preferences.v1", JsonSerializer.Serialize(new
        {
            Provider = "ollama", Model = "fixture-model", Endpoint = provider.Address, RemoteUrl = provider.Address + "/mcp", RemoteTools = "echo",
        }));
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadAssistantSettingsAsync", Private)!.Invoke(vm, null)!;
        Check(vm.AssistantContextKiB == 0 && vm.AssistantRecentContextTurns == 8 && vm.AssistantEndpoint == provider.Address,
            "0.5.6 preferences load with full context and preserve the provider and endpoints");
        var asset = new RemoteFabAsset("assistant-fixture", "ue", "Assisted trees", "Local fixture",
            "Fab", "assetPack", null, null, [], [new("fixture-variant", [], ["5.5"], ["Windows"])]);
        Call(vm, "ReplaceRemoteAssets", (Func<RemoteFabAsset, bool>)(_ => true), new[] { asset });
        await settings.SetAsync("ai.mcp.options.v1", """{"LocalPort":0,"AutoCategorize":false,"MaximumPageSize":42}""");
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadAiSettingsAsync", Private)!.Invoke(vm, null)!;
        Check(vm.McpSelectedAccessProfile == "Free" && vm.McpMaximumPageSize == 42,
            "A saved profile without an access choice loads as Free and retains its limits");
        var pipe = "CyEngineHub.Mcp.Smoke." + Guid.NewGuid().ToString("N");
        Call(vm, "StartMcp", pipe);
        try
        {
            var proxy = Path.GetFullPath("src/CyEngineHub.Desktop/bin/Release/net8.0/Mcp/CyEngineHub.Mcp.exe");
            using var client = Process.Start(new ProcessStartInfo(proxy, "--pipe " + pipe)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            })!;
            try
            {
                async Task<JsonElement> Request(string method, object parameters)
                {
                    await client.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }));
                    await client.StandardInput.FlushAsync();
                    using var json = JsonDocument.Parse((await client.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!);
                    return json.RootElement.GetProperty("result").Clone();
                }
                await Request("initialize", new { protocolVersion = "2025-06-18", capabilities = new { },
                    clientInfo = new { name = "independent-qa-client", version = "1" } });
                var tools = await Request("tools/list", new { });
                Check(tools.GetProperty("tools").GetArrayLength() == 10 &&
                    tools.GetProperty("tools").EnumerateArray().All(tool => tool.TryGetProperty("outputSchema", out _)),
                    "External stdio client discovers ten CyMCPSDK tools with output schemas");
                var read = await Request("tools/call", new { name = "hub_targets", arguments = new { } });
                Check(read.GetProperty("structuredContent").GetProperty("items").GetArrayLength() == 2, "External MCP reads the live hub catalog");
                var change = await Request("tools/call", new { name = "hub_propose", arguments = new
                { action = "categorize", assetId = asset.AssetId, source = "Fab", categories = new[] { "External MCP" }, reason = "QA permissions" } });
                if (change.GetProperty("isError").GetBoolean()) throw new InvalidOperationException(change.GetRawText());
                Check(change.GetProperty("structuredContent").GetProperty("automatic").GetBoolean() &&
                    change.GetProperty("structuredContent").GetProperty("status").GetString() == "Collections appliquées",
                    "Legacy stdio uses the Free profile for a live mutation");
                var denied = await Request("tools/call", new { name = "hub_propose", arguments = new
                { action = "install", assetId = asset.AssetId, source = "Fab", variantId = "fixture-variant", targetId = "unregistered-target", reason = "QA refusal" } });
                Check(denied.GetProperty("isError").GetBoolean(), "External MCP refuses installation into an unregistered target");
            }
            finally
            {
                client.StandardInput.Close();
                await client.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            }

            string? cliToken = null;
            async Task<JsonElement> Command(int expectedExit, params string[] arguments)
            {
                var start = new ProcessStartInfo(proxy) { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 };
                foreach (var argument in arguments) start.ArgumentList.Add(argument);
                if (cliToken is not null) start.Environment["CYHUB_SMOKE_MCP_TOKEN"] = cliToken;
                using var command = Process.Start(start)!;
                var text = await command.StandardOutput.ReadToEndAsync();
                await command.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                if (command.ExitCode != expectedExit) throw new InvalidOperationException(text);
                using var document = JsonDocument.Parse(text);
                return document.RootElement.Clone();
            }
            Check((await Command(0, "--help")).GetProperty("usage").GetString()!.Contains("--pipe"), "CLI help is executable without an open instance");
            Check((await Command(0, "--pipe", pipe, "list")).GetProperty("tools").GetArrayLength() == 10,
                "CLI discovery preserves the full ten-tool catalogue");
            Check((await Command(0, "--pipe", pipe, "describe", "hub_targets")).TryGetProperty("outputSchema", out _),
                "CLI describes the registered operation schema");
            var targets = await Command(0, "--pipe", pipe, "call", "hub_targets");
            Check(targets.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetString() == "project:fixture:project"),
                $"CLI command actually executed: CyEngineHub.Mcp.exe --pipe {pipe} call hub_targets");
            var argumentsFile = Path.Combine(Output, "assistant-cli-arguments.json");
            await File.WriteAllTextAsync(argumentsFile, """{"action":"categorize","assetId":"assistant-fixture","source":"Fab","categories":["CLI SDK"],"reason":"QA permissions"}""");
            await Command(0, "--pipe", pipe, "call", "hub_propose", "--json-file", argumentsFile);
            Check(vm.AiProposals.Any(item => item.Proposal.Categories.Contains("CLI SDK") && item.Status == "Collections appliquées"),
                "CLI JSON-file mutation uses the same automatic categorization permission");
            await Command(3, "--pipe", pipe, "call", "hub_propose", "--json", """{"action":"install","assetId":"assistant-fixture","source":"Fab","variantId":"fixture-variant","targetId":"unregistered-target","reason":"QA refusal"}""");
            await Command(2, "--pipe", pipe, "call", "hub_targets", "--json", "[]");
            await Command(1, "--pipe", pipe + ".absent", "call", "hub_targets");
            Check(true, "CLI publishes JSON and distinct operation, usage and transport exit codes");

            using (var http = new LocalMcpTestClient(vm.McpHttpUrl))
            {
                Check((await http.InitializeAsync(origin: "https://example.test")).Status == HttpStatusCode.Forbidden,
                    "Free rejects cross-origin browser requests");
                Check((await http.InitializeAsync()).Status == HttpStatusCode.OK && vm.McpAccessToken.Length == 0,
                    "Free opens Streamable HTTP with the loopback URL alone");
                var (discoveryStatus, discovery) = await http.SendAsync("tools/list", new { });
                Check(discoveryStatus == HttpStatusCode.OK && discovery.GetProperty("result").GetProperty("tools").GetArrayLength() == 10,
                    "An HTTP client discovers all ten tools without a token");
                var read = await http.CallAsync("hub_targets", new { });
                Check(read.GetProperty("structuredContent").GetProperty("items").GetArrayLength() == 2,
                    "Free reads the live project and engine without a token");
                var change = await http.CallAsync("hub_propose", new
                { action = "categorize", assetId = asset.AssetId, source = "Fab", categories = new[] { "Free HTTP" }, reason = "local QA" });
                Check(change.GetProperty("structuredContent").GetProperty("automatic").GetBoolean() &&
                    (await settings.GetAsync(MainWindowViewModel.FabCollectionsSetting))!.Contains("Free HTTP", StringComparison.Ordinal),
                    "Free persists a real collection change without a token or approval");
                Check((await http.CallAsync("hub_targets", new { unexpected = true })).GetProperty("isError").GetBoolean(),
                    "Free keeps SDK schema validation enabled");
            }
            Check((await Command(0, "--url", vm.McpHttpUrl, "describe", "hub_targets")).TryGetProperty("outputSchema", out _),
                "CLI also discovers schemas through the Free HTTP URL");

            vm.ShowSettingsCommand.Execute(null);
            vm.SelectedSettingsCategory = vm.SettingsCategories.Single(category => category.Id == "ai");
            vm.AssistantProvider = "ollama";
            vm.AssistantModel = "fixture-model";
            vm.AssistantEndpoint = provider.Address;
            vm.AssistantRemoteUrl = provider.Address + "/mcp";
            vm.AssistantRemoteTools = "echo";
            var open = typeof(MainWindowViewModel).GetMethod("OpenAssistantAsync", Private)!;
            var previous = Environment.GetCommandLineArgs();
            var previousIndex = Array.IndexOf(previous, "--assistant-previous-history");
            if (previousIndex >= 0)
            {
                var history = Path.Combine(vm.DataDirectory, "Assistant");
                Directory.CreateDirectory(history);
                foreach (var file in Directory.EnumerateFiles(previous[previousIndex + 1]))
                    File.Copy(file, Path.Combine(history, Path.GetFileName(file)));
            }
            await (Task)open.Invoke(vm, [previousIndex >= 0])!;
            var chat = (nint)typeof(MainWindowViewModel).GetField("_assistant", Private)!.GetValue(vm)!;
            JsonElement State()
            {
                using var json = JsonDocument.Parse(Marshal.PtrToStringUTF8(AssistantState(chat))!);
                return json.RootElement.Clone();
            }
            async Task<JsonElement> Wait(Func<JsonElement, bool> check)
            {
                var timeout = Stopwatch.StartNew();
                while (true)
                {
                    var state = State();
                    if (check(state)) return state;
                    if (timeout.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException(state.GetRawText());
                    await ImGuiDiagnostics.FramesAsync(window, 2);
                }
            }
            void Send(string prompt)
            {
                var error = Marshal.PtrToStringUTF8(SendToAssistant(chat, prompt));
                if (error is not null) throw new InvalidOperationException(error);
            }
            static bool Busy(JsonElement state) => state.GetProperty("busy").GetBoolean();
            static string Last(JsonElement state) => state.GetProperty("messages").EnumerateArray().LastOrDefault() is var message &&
                message.ValueKind == JsonValueKind.Object ? message.GetProperty("text").GetString()! : "";
            await Wait(state => !Busy(state) && state.GetProperty("connected").GetBoolean());
            if (previousIndex >= 0)
            {
                Check(State().GetProperty("messages").EnumerateArray().Any(message =>
                    message.GetProperty("text").GetString()!.Contains("Fragment visible", StringComparison.Ordinal)),
                    "A real 0.5.6 conversation is restored from a copied profile");
                await (Task)open.Invoke(vm, [false])!;
                await Wait(state => !Busy(state) && state.GetProperty("connected").GetBoolean());
            }
            Send("mutate");
            await Wait(state => Busy(state) && Last(state) == "Fragment visible");
            Check(vm.AiProposals.Any(item => item.Proposal.Categories.Contains("Chat SDK") && item.Status == "Collections appliquées"),
                "Native chat streams a partial answer while a real registered business mutation has taken effect");
            provider.ReleasePartial.TrySetResult();
            await Wait(state => !Busy(state) && Last(state).Contains("complet", StringComparison.Ordinal));
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "assistant-cymcp.png"));

            Send("remote");
            await Wait(state => !Busy(state) && Last(state) == "Résultat distant reçu");
            Send("remote");
            await Wait(state => !Busy(state) && Last(state) == "Résultat distant reçu");
            Send("remote-error");
            await Wait(state => !Busy(state) && Last(state) == "Erreur distante reçue");
            Check(provider.RemoteCalls == 3 && !provider.HiddenAdvertised,
                "Chat forwards repeated calls and errors without replay or tools outside the remote allowlist");

            Send("cancel");
            await Wait(state => Busy(state) && Last(state) == "À annuler");
            CancelAssistant(chat);
            var cancelled = await Wait(state => !Busy(state));
            Check(cancelled.GetProperty("messages").EnumerateArray().Last().GetProperty("interrupted").GetBoolean(),
                "Cancel interrupts a live, partially streamed generation");
            provider.ReleaseCancel.TrySetResult();
            await (Task)open.Invoke(vm, [true])!;
            Check((await Wait(state => !Busy(state) && state.GetProperty("connected").GetBoolean()))
                .GetProperty("messages").GetArrayLength() >= 6, "The native panel restores persisted conversation history");

            vm.AssistantContextKiB = 4;
            vm.AssistantRecentContextTurns = 1;
            await (Task)open.Invoke(vm, [true])!;
            await Wait(state => !Busy(state) && state.GetProperty("connected").GetBoolean());
            Send("context-proof");
            await Wait(state => !Busy(state) && Last(state) == "Contexte reçu");
            Check(provider.ContextMessages is { } projected && projected[0].GetProperty("role").GetString() == "system" &&
                projected.EnumerateArray().All(message => message.GetProperty("content").ToString() != "mutate") &&
                State().GetProperty("messages").EnumerateArray().Any(message => message.GetProperty("text").GetString() == "mutate"),
                "Opt-in context projection keeps system instructions and the active turn, and retains the full UI history");
            var requests = provider.ChatRequests;
            Send(new string('x', 5000));
            var overBudget = await Wait(state => !Busy(state));
            Check(provider.ChatRequests == requests && overBudget.GetProperty("messages").EnumerateArray().Last().GetProperty("error").GetString()!.Length > 0,
                "An oversized active turn fails locally without forwarding or replaying an operation");

            async Task ChangeProfile(HubMcpAccessProfile profile)
            {
                await ImGuiDiagnostics.FramesAsync(window, 3);
                await ImGuiDiagnostics.ClickAsync(window, "combo/McpSelectedAccessProfile");
                await ImGuiDiagnostics.ClickAsync(window, "combo-option/McpSelectedAccessProfile/" + (int)profile);
                Check(vm.McpSelectedAccessProfile == vm.McpAccessProfiles[(int)profile], $"The settings picker selects {profile}");
                await (Task)typeof(MainWindowViewModel).GetMethod("SaveMcpOptionsAsync", Private)!.Invoke(vm, null)!;
                var saved = JsonSerializer.Deserialize<HubMcpOptions>((await settings.GetAsync("ai.mcp.options.v1"))!)!;
                Check(saved.AccessProfile == profile && vm.McpEnabled, $"Saving {profile} persists the choice and restarts active MCP");
            }

            await ChangeProfile(HubMcpAccessProfile.Protected);
            using (var noToken = new LocalMcpTestClient(vm.McpHttpUrl))
            using (var wrongToken = new LocalMcpTestClient(vm.McpHttpUrl, "wrong"))
            using (var authorized = new LocalMcpTestClient(vm.McpHttpUrl, vm.McpAccessToken))
            {
                Check((await noToken.InitializeAsync()).Status == HttpStatusCode.Unauthorized &&
                    (await wrongToken.InitializeAsync()).Status == HttpStatusCode.Unauthorized &&
                    (await authorized.InitializeAsync()).Status == HttpStatusCode.OK,
                    "Protected requires the correct Bearer token");
                var pending = await authorized.CallAsync("hub_propose", new
                { action = "categorize", assetId = asset.AssetId, source = "Fab", categories = new[] { "Needs review" }, reason = "local QA" });
                Check(pending.GetProperty("structuredContent").GetProperty("status").GetString() == "pending_human_approval" &&
                    !(await settings.GetAsync(MainWindowViewModel.FabCollectionsSetting))!.Contains("Needs review", StringComparison.Ordinal),
                    "Protected retains the app's action confirmations after authentication");
            }
            await Command(1, "--pipe", pipe, "list");
            await Command(1, "--url", vm.McpHttpUrl, "list");
            Check(true, "The legacy pipe and tokenless HTTP CLI cannot bypass Protected");
            cliToken = vm.McpAccessToken;
            Check((await Command(0, "--url", vm.McpHttpUrl, "--token-env", "CYHUB_SMOKE_MCP_TOKEN", "list"))
                .GetProperty("tools").GetArrayLength() == 10, "The HTTP CLI reads Protected tools with a Bearer token from its environment");
            cliToken = null;

            await ChangeProfile(HubMcpAccessProfile.TokenOnly);
            using (var authorized = new LocalMcpTestClient(vm.McpHttpUrl, vm.McpAccessToken))
            {
                Check((await authorized.InitializeAsync()).Status == HttpStatusCode.OK, "Token only accepts its Bearer token");
                var change = await authorized.CallAsync("hub_propose", new
                { action = "categorize", assetId = asset.AssetId, source = "Fab", categories = new[] { "Token only" }, reason = "local QA" });
                Check(change.GetProperty("structuredContent").GetProperty("automatic").GetBoolean(), "Token only executes the action after authentication");
            }

            await ChangeProfile(HubMcpAccessProfile.LocalControlled);
            using (var local = new LocalMcpTestClient(vm.McpHttpUrl))
            {
                Check((await local.InitializeAsync()).Status == HttpStatusCode.OK, "Local controlled connects without a token");
                var pending = await local.CallAsync("hub_propose", new
                { action = "categorize", assetId = asset.AssetId, source = "Fab", categories = new[] { "Local review" }, reason = "local QA" });
                Check(pending.GetProperty("structuredContent").GetProperty("status").GetString() == "pending_human_approval",
                    "Local controlled keeps the configured confirmation policy");
            }
        }
        finally
        {
            vm.StopMcp();
            await (Task)typeof(MainWindowViewModel).GetMethod("StopAssistantAsync", Private)!.Invoke(vm, null)!;
        }
    }

    private sealed class LocalMcpTestClient(string url, string? token = null) : IDisposable
    {
        private readonly HttpClient _client = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
        private string? _session;
        private int _id;

        public Task<(HttpStatusCode Status, JsonElement Payload)> InitializeAsync(string? origin = null) =>
            SendAsync("initialize", new { protocolVersion = "2025-06-18", capabilities = new { },
                clientInfo = new { name = "independent-http-qa", version = "1" } }, origin);

        public async Task<(HttpStatusCode Status, JsonElement Payload)> SendAsync(string method, object parameters, string? origin = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = ++_id, method, @params = parameters }),
                    Encoding.UTF8, "application/json"),
            };
            request.Headers.Accept.ParseAdd("application/json, text/event-stream");
            if (token is not null) request.Headers.Authorization = new("Bearer", token);
            if (origin is not null) request.Headers.Add("Origin", origin);
            if (_session is not null)
            {
                request.Headers.Add("Mcp-Session-Id", _session);
                request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
            }
            using var response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return (response.StatusCode, default);
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessions)) _session = sessions.Single();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return (response.StatusCode, document.RootElement.Clone());
        }

        public async Task<JsonElement> CallAsync(string name, object arguments)
        {
            var response = await SendAsync("tools/call", new { name, arguments });
            if (response.Status != HttpStatusCode.OK) throw new InvalidOperationException("MCP tool HTTP status: " + response.Status);
            return response.Payload.GetProperty("result");
        }

        public void Dispose() => _client.Dispose();
    }

    private sealed class AssistantFixture : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;
        public string Address { get; }
        public int RemoteCalls;
        public int ChatRequests;
        public bool HiddenAdvertised;
        private readonly ConcurrentQueue<JsonElement> _contexts = new();
        public JsonElement? ContextMessages => _contexts.TryPeek(out var value) ? value : null;
        public TaskCompletionSource ReleasePartial { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCancel { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AssistantFixture()
        {
            using var reserve = new TcpListener(IPAddress.Loopback, 0);
            reserve.Start();
            Address = "http://127.0.0.1:" + ((IPEndPoint)reserve.LocalEndpoint).Port;
            reserve.Stop();
            _listener.Prefixes.Add(Address + "/");
            _listener.Start();
            _loop = Listen();
        }
        private async Task Listen()
        {
            try
            {
                while (_listener.IsListening)
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => Reply(context));
                }
            }
            catch (HttpListenerException) when (!_listener.IsListening) { }
            catch (ObjectDisposedException) { }
        }
        private async Task Reply(HttpListenerContext context)
        {
            using var response = context.Response;
            try
            {
                async Task Write(object item) => await response.OutputStream.WriteAsync(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(item) + "\n")).ConfigureAwait(false);
                if (context.Request.Url!.AbsolutePath == "/api/tags")
                {
                    response.ContentType = "application/json";
                    await Write(new { models = new[] { new { name = "fixture-model", model = "fixture-model" } } });
                    return;
                }
                using var body = await JsonDocument.ParseAsync(context.Request.InputStream).ConfigureAwait(false);
                if (context.Request.Url.AbsolutePath == "/mcp")
                {
                    response.ContentType = "application/json";
                    var method = body.RootElement.GetProperty("method").GetString();
                    if (!body.RootElement.TryGetProperty("id", out var id))
                    {
                        response.StatusCode = 202;
                        return;
                    }
                    if (method == "server/discover")
                    {
                        await Write(new { jsonrpc = "2.0", id, error = new { code = -32601, message = "unsupported" } });
                        return;
                    }
                    object CallRemote()
                    {
                        Interlocked.Increment(ref RemoteCalls);
                        var fail = body.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("value").GetString() == "fail";
                        return new { isError = fail, content = new[] { new { type = "text", text = fail ? "fixture-failure" : "fixture-result" } } };
                    }
                    object result = method switch
                    {
                        "initialize" => new { protocolVersion = "2025-06-18", capabilities = new { tools = new { } },
                            serverInfo = new { name = "external-fixture", version = "1" } },
                        "tools/list" => new { tools = new[] { "echo", "echo_hidden" }.Select(name => new { name, description = "Echo a value.",
                            inputSchema = new { type = "object", properties = new { value = new { type = "string" } }, required = new[] { "value" } } }) },
                        "tools/call" => CallRemote(),
                        _ => new { },
                    };
                    await Write(new { jsonrpc = "2.0", id, result });
                    return;
                }
                response.ContentType = "application/x-ndjson";
                response.SendChunked = true;
                Interlocked.Increment(ref ChatRequests);
                if (body.RootElement.GetProperty("tools").GetRawText().Contains("echo_hidden", StringComparison.Ordinal))
                    HiddenAdvertised = true;
                var messages = body.RootElement.GetProperty("messages").EnumerateArray().ToArray();
                var prompt = messages.Last(message => message.GetProperty("role").GetString() == "user").GetProperty("content").GetString();
                var returned = messages.Last().GetProperty("role").GetString() == "tool";
                Task Stream(string text) => Write(new { message = new { role = "assistant", content = text }, done = false });
                if (!returned && prompt is "mutate" or "remote" or "remote-error")
                {
                    object arguments = prompt == "mutate" ? new { assetId = "assistant-fixture", source = "Fab", action = "categorize",
                        categories = new[] { "Chat SDK" }, reason = "QA fixture" } : new { value = prompt == "remote-error" ? "fail" : "ok" };
                    var name = prompt == "mutate" ? "hub_propose" : "remote_echo";
                    await Write(new { message = new { role = "assistant", content = "", tool_calls = new[] { new { function = new { name, arguments } } } }, done = false });
                }
                else if (prompt == "mutate")
                {
                    await Stream("Fragment visible");
                    await response.OutputStream.FlushAsync();
                    await ReleasePartial.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    await Stream(" puis complet");
                }
                else if (prompt == "cancel")
                {
                    await Stream("À annuler");
                    await response.OutputStream.FlushAsync();
                    await ReleaseCancel.Task.WaitAsync(TimeSpan.FromSeconds(20));
                }
                else if (prompt == "context-proof")
                {
                    _contexts.Enqueue(body.RootElement.GetProperty("messages").Clone());
                    await Stream("Contexte reçu");
                }
                else
                {
                    var wire = messages.Last().GetProperty("content").GetString() ?? "";
                    await Stream(wire.Contains("fixture-failure", StringComparison.Ordinal) ? "Erreur distante reçue" :
                        wire.Contains("fixture-result", StringComparison.Ordinal) ? "Résultat distant reçu" : "Résultat inattendu");
                }
                await Write(new { message = new { content = "" }, done = true, done_reason = "stop" });
            }
            catch (Exception error) when (error is HttpListenerException or IOException or ObjectDisposedException) { }
        }
        public async ValueTask DisposeAsync()
        {
            ReleasePartial.TrySetResult();
            ReleaseCancel.TrySetResult();
            _listener.Close();
            await _loop.ConfigureAwait(false);
        }
    }
}
