using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using XIVLauncher.Mac.Services;
using XIVLauncher.Mac.Settings;
using XIVLauncher.Mac.ViewModels;

namespace XIVLauncher.Mac;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var launchOptions = MacLaunchOptions.FromArgs(desktop.Args ?? []);
            var mainWindow = new MainWindow();
            var viewModel = new MainWindowViewModel(
                new MacSettingsService(),
                new MacKeychainCredentialStore(),
                new MacInstallResolver(),
                new MacLauncherService(launchOptions),
                new AvaloniaClipboardService(() => TopLevel.GetTopLevel(mainWindow)?.Clipboard));

            mainWindow.DataContext = viewModel;
            desktop.MainWindow = mainWindow;

            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
