using System.Text;
using System.Runtime.InteropServices;
using CyEngineHub.Core.Ai;
using CyEngineHub.VersionBridge.Tool;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(false);
if (args is ["--help" or "-h" or "help"]) return McpCommands.Help("CyEngineHub.VersionBridge.Tool", "--workspace PATH");
if (args is not ["--workspace", var workspace, ..])
    return McpCommands.Failure("usage", "Provide --workspace PATH. See --help.", 2);
var command = args[2..];
if (command is ["--help" or "-h" or "help"]) return McpCommands.Help("CyEngineHub.VersionBridge.Tool", "--workspace PATH");
if (command.Length > 0 && !McpCommands.IsCommand(command))
    return McpCommands.Failure("usage", McpCommands.Syntax, 2);
try
{
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
    var appDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
    var native = OperatingSystem.IsWindows() ? "cimgui.dll" : OperatingSystem.IsMacOS() ? "cimgui.dylib" : "cimgui.so";
    NativeLibrary.SetDllImportResolver(typeof(SdkMcpRegistry).Assembly, (name, _, _) =>
        name == "cimgui" ? NativeLibrary.Load(Path.Combine(appDirectory, native)) : 0);
    await using var jobs = new RepairJobs(workspace);
    using var registry = new SdkMcpRegistry(RepairJobs.Definitions(), jobs.CallAsync);
    using var session = registry.CreateSession();
    if (command.Length > 0)
        return await McpCommands.RunAsync(command, session.ExchangeAsync, stop.Token, waitForRepair: true).ConfigureAwait(false);
    using var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
    await session.ServeAsync(input, Console.Out, stop.Token).ConfigureAwait(false);
    return 0;
}
catch (Exception exception) when (exception is not OutOfMemoryException)
{
    if (command.Length > 0) return McpCommands.Failure("transport", exception.Message, 1);
    await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
    return 1;
}
