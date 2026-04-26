using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

            var viewModel = new MainWindowViewModel(
                new MacSettingsService(),
                new MacInstallResolver(),
                new MacLauncherService(launchOptions));

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
