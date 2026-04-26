using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using XIVLauncher.Common;
using XIVLauncher.Common.Game.OfficialMacApp;
using XIVLauncher.Mac.Services;
using XIVLauncher.Mac.Settings;

namespace XIVLauncher.Mac.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IMacSettingsService settingsService;
    private readonly IMacInstallResolver installResolver;
    private readonly IMacLauncherService launcherService;
    private OfficialMacAppInstall? install;
    private string? officialAppPathOverride;
    private string? resolvedAppPath;
    private string? gameRootPath;
    private string installStatus = "Checking install...";
    private string statusMessage = "Enter your account details to launch.";
    private string username = string.Empty;
    private string password = string.Empty;
    private string otp = string.Empty;
    private ClientLanguage selectedLanguage = ClientLanguage.English;
    private bool isFreeTrial;
    private bool isSteam;
    private bool isBusy;

    public MainWindowViewModel(IMacSettingsService settingsService, IMacInstallResolver installResolver, IMacLauncherService launcherService)
    {
        this.settingsService = settingsService;
        this.installResolver = installResolver;
        this.launcherService = launcherService;
        this.LaunchCommand = new AsyncCommand(this.LaunchAsync, () => this.CanLaunch);
        this.RefreshInstallCommand = new AsyncCommand(this.ResolveAndSaveInstallAsync, () => !this.IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ClientLanguage> Languages { get; } =
    [
        ClientLanguage.English,
        ClientLanguage.Japanese,
        ClientLanguage.German,
        ClientLanguage.French,
    ];

    public AsyncCommand LaunchCommand { get; }

    public ICommand RefreshInstallCommand { get; }

    public string? OfficialAppPathOverride
    {
        get => this.officialAppPathOverride;
        set => this.SetProperty(ref this.officialAppPathOverride, value);
    }

    public string? ResolvedAppPath
    {
        get => this.resolvedAppPath;
        private set
        {
            if (this.SetProperty(ref this.resolvedAppPath, value))
                this.OnPropertyChanged(nameof(this.ResolvedAppPathDisplay));
        }
    }

    public string? GameRootPath
    {
        get => this.gameRootPath;
        private set
        {
            if (this.SetProperty(ref this.gameRootPath, value))
                this.OnPropertyChanged(nameof(this.GameRootPathDisplay));
        }
    }

    public string ResolvedAppPathDisplay => string.IsNullOrWhiteSpace(this.ResolvedAppPath) ? "Not resolved" : this.ResolvedAppPath;

    public string GameRootPathDisplay => string.IsNullOrWhiteSpace(this.GameRootPath) ? "Not resolved" : this.GameRootPath;

    public string InstallStatus
    {
        get => this.installStatus;
        private set => this.SetProperty(ref this.installStatus, value);
    }

    public string StatusMessage
    {
        get => this.statusMessage;
        private set => this.SetProperty(ref this.statusMessage, value);
    }

    public string Username
    {
        get => this.username;
        set
        {
            if (this.SetProperty(ref this.username, value))
                this.RaiseCanLaunchChanged();
        }
    }

    public string Password
    {
        get => this.password;
        set
        {
            if (this.SetProperty(ref this.password, value))
                this.RaiseCanLaunchChanged();
        }
    }

    public string Otp
    {
        get => this.otp;
        set => this.SetProperty(ref this.otp, value);
    }

    public ClientLanguage SelectedLanguage
    {
        get => this.selectedLanguage;
        set => this.SetProperty(ref this.selectedLanguage, value);
    }

    public bool IsFreeTrial
    {
        get => this.isFreeTrial;
        set => this.SetProperty(ref this.isFreeTrial, value);
    }

    public bool IsSteam
    {
        get => this.isSteam;
        set => this.SetProperty(ref this.isSteam, value);
    }

    public bool IsBusy
    {
        get => this.isBusy;
        private set
        {
            if (this.SetProperty(ref this.isBusy, value))
            {
                this.RaiseCanLaunchChanged();
                this.RefreshInstallCommandCanExecuteChanged();
            }
        }
    }

    public bool IsInstallDetected => this.install is not null;

    public bool CanLaunch
        => this.IsInstallDetected &&
           !this.IsBusy &&
           !string.IsNullOrWhiteSpace(this.Username) &&
           !string.IsNullOrWhiteSpace(this.Password);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await this.settingsService.LoadAsync(cancellationToken);
            this.OfficialAppPathOverride = settings.OfficialAppPathOverride;
            this.Username = settings.LastUsername ?? string.Empty;
            this.SelectedLanguage = settings.ClientLanguage;
            this.IsFreeTrial = settings.IsFreeTrial;
            this.IsSteam = settings.IsSteam;
            this.ResolveInstall();
        }
        catch (Exception ex)
        {
            this.install = null;
            this.ResolvedAppPath = null;
            this.GameRootPath = null;
            this.InstallStatus = "Could not load Mac settings.";
            this.StatusMessage = $"Could not load Mac settings: {ex.Message}";
            this.OnPropertyChanged(nameof(this.IsInstallDetected));
            this.RaiseCanLaunchChanged();
        }
    }

    public async Task LaunchAsync()
    {
        if (this.IsBusy ||
            string.IsNullOrWhiteSpace(this.Username) ||
            string.IsNullOrWhiteSpace(this.Password))
            return;

        this.ResolveInstall();

        if (this.install is null)
            return;

        this.IsBusy = true;
        this.StatusMessage = "Logging in and checking patches...";

        try
        {
            await this.SaveSettingsAsync();
            var request = new MacLaunchRequest(
                this.install,
                this.Username.Trim(),
                this.Password,
                this.Otp.Trim(),
                this.SelectedLanguage,
                this.IsSteam,
                this.IsFreeTrial);
            var result = await this.launcherService.LaunchAsync(request);

            this.StatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            this.StatusMessage = $"Launch failed: {ex.Message}";
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private async Task ResolveAndSaveInstallAsync()
    {
        this.ResolveInstall();
        await this.SaveSettingsAsync();
    }

    private void ResolveInstall()
    {
        var resolution = this.installResolver.Resolve(this.OfficialAppPathOverride);
        this.install = resolution.Install;
        this.ResolvedAppPath = resolution.ResolvedAppPath;
        this.GameRootPath = resolution.GameRootPath;
        this.InstallStatus = resolution.StatusMessage;
        this.OnPropertyChanged(nameof(this.IsInstallDetected));
        this.RaiseCanLaunchChanged();
    }

    private Task SaveSettingsAsync()
        => this.settingsService.SaveAsync(new MacSettings
        {
            OfficialAppPathOverride = string.IsNullOrWhiteSpace(this.OfficialAppPathOverride)
                ? null
                : this.OfficialAppPathOverride.Trim(),
            LastUsername = string.IsNullOrWhiteSpace(this.Username) ? null : this.Username.Trim(),
            ClientLanguage = this.SelectedLanguage,
            IsFreeTrial = this.IsFreeTrial,
            IsSteam = this.IsSteam,
        });

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        this.OnPropertyChanged(propertyName);
        return true;
    }

    private void RaiseCanLaunchChanged()
    {
        this.OnPropertyChanged(nameof(this.CanLaunch));
        this.LaunchCommand.RaiseCanExecuteChanged();
    }

    private void RefreshInstallCommandCanExecuteChanged()
    {
        if (this.RefreshInstallCommand is AsyncCommand command)
            command.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
