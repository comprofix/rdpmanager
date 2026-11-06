using Avalonia;
using Velopack;

namespace RDPManager.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack's install, update and uninstall hooks run first and may exit the process. It makes no
        // network request; update checks happen only when asked (DESIGN: Milestone 9 decisions).
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
