using Avalonia;
using Avalonia.Fonts.Inter;
using CyEngineHub.Core.Processes;

namespace CyEngineHub.Desktop;

internal static class Program
{
    internal static SingleInstanceHost? InstanceHost { get; private set; }
    [STAThread]
    public static void Main(string[] args)
    {
        using var instance = new SingleInstanceHost();
        if (!instance.IsPrimary)
        {
            instance.ActivatePrimaryAsync().GetAwaiter().GetResult();
            return;
        }
        InstanceHost = instance;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
