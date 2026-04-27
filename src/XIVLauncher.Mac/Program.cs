using Avalonia;
using System;
using System.IO;
using Serilog;
using XIVLauncher.Common.Support;
using XIVLauncher.Mac.Settings;

namespace XIVLauncher.Mac;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Directory.CreateDirectory(MacSettingsService.DefaultApplicationSupportDirectory);
        LogInit.Setup(Path.Combine(MacSettingsService.DefaultApplicationSupportDirectory, "output.log"), args);

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
