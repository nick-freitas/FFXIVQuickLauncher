using XIVLauncher.Common;
using XIVLauncher.Common.Game.OfficialMacApp;
using XIVLauncher.Mac.Services;
using XIVLauncher.Mac.Settings;
using XIVLauncher.Mac.ViewModels;

namespace XIVLauncher.Mac.Tests.ViewModels;

[TestClass]
public sealed class MainWindowViewModelTests
{
    [TestMethod]
    public async Task InitializeAsyncLoadsSettingsAndResolvesInstallFromOverride()
    {
        var install = CreateInstall("/Applications/Custom.app");
        var settings = new MacSettings
        {
            OfficialAppPathOverride = install.AppBundle.FullName,
            LastUsername = "saved-user",
            ClientLanguage = ClientLanguage.German,
            IsFreeTrial = true,
            IsSteam = true,
        };
        var settingsService = new FakeSettingsService(settings);
        var credentials = new FakeCredentialStore { Password = "saved-password" };
        var resolver = new FakeInstallResolver(MacInstallResolution.Found(install));
        var viewModel = new MainWindowViewModel(settingsService, credentials, resolver, new FakeLauncherService());

        await viewModel.InitializeAsync();

        Assert.AreEqual("saved-user", credentials.LastPasswordLookupUsername);
        Assert.AreEqual(settings.OfficialAppPathOverride, resolver.LastOverridePath);
        Assert.IsTrue(viewModel.IsInstallDetected);
        Assert.AreEqual(install.AppBundle.FullName, viewModel.ResolvedAppPath);
        Assert.AreEqual(install.GameRoot.FullName, viewModel.GameRootPath);
        Assert.AreEqual(install.GameRoot.FullName, viewModel.GameRootPathDisplay);
        Assert.AreEqual("saved-user", viewModel.Username);
        Assert.AreEqual("saved-password", viewModel.Password);
        Assert.AreEqual(ClientLanguage.German, viewModel.SelectedLanguage);
        Assert.IsTrue(viewModel.IsFreeTrial);
        Assert.IsTrue(viewModel.IsSteam);
    }

    [TestMethod]
    public async Task MissingInstallDisablesLaunchAndReportsActionableStatus()
    {
        var settingsService = new FakeSettingsService(new MacSettings());
        var resolver = new FakeInstallResolver(MacInstallResolution.NotFound(
            "/Applications/FINAL FANTASY XIV ONLINE.app",
            "Official app was not found. Set the app path override."));
        var viewModel = new MainWindowViewModel(settingsService, resolver, new FakeLauncherService())
        {
            Username = "user",
            Password = "password",
        };

        await viewModel.InitializeAsync();

        Assert.IsFalse(viewModel.IsInstallDetected);
        Assert.IsFalse(viewModel.CanLaunch);
        Assert.IsFalse(viewModel.LaunchCommand.CanExecute(null));
        Assert.AreEqual("Not resolved", viewModel.GameRootPathDisplay);
        StringAssert.Contains(viewModel.InstallStatus, "Set the app path override");
    }

    [TestMethod]
    public async Task LaunchAsyncPersistsNonPasswordSettingsAfterSuccessfulLaunch()
    {
        var install = CreateInstall("/Applications/FINAL FANTASY XIV ONLINE.app");
        var settingsService = new FakeSettingsService(new MacSettings());
        var launcher = new FakeLauncherService { Result = MacLaunchResult.Launched() };
        var viewModel = new MainWindowViewModel(settingsService, new FakeInstallResolver(MacInstallResolution.Found(install)), launcher);

        await viewModel.InitializeAsync();
        viewModel.Username = "saved-user";
        viewModel.Password = "do-not-save";
        viewModel.Otp = "123456";
        viewModel.SelectedLanguage = ClientLanguage.Japanese;
        viewModel.OfficialAppPathOverride = "/Applications/Override.app";
        viewModel.IsFreeTrial = true;
        viewModel.IsSteam = true;

        await viewModel.LaunchAsync();

        Assert.AreEqual(1, launcher.Requests.Count);
        Assert.IsNotNull(launcher.LastProgress);
        Assert.AreEqual("saved-user", launcher.Requests[0].Username);
        Assert.AreEqual("do-not-save", launcher.Requests[0].Password);
        Assert.AreEqual("123456", launcher.Requests[0].Otp);
        Assert.IsNotNull(settingsService.SavedSettings);
        Assert.AreEqual("saved-user", settingsService.SavedSettings.LastUsername);
        Assert.AreEqual(ClientLanguage.Japanese, settingsService.SavedSettings.ClientLanguage);
        Assert.AreEqual("/Applications/Override.app", settingsService.SavedSettings.OfficialAppPathOverride);
        Assert.IsTrue(settingsService.SavedSettings.IsFreeTrial);
        Assert.IsTrue(settingsService.SavedSettings.IsSteam);
    }

    [TestMethod]
    public async Task LaunchAsyncSavesPasswordToCredentialStoreWhenLaunchStarts()
    {
        var install = CreateInstall("/Applications/FINAL FANTASY XIV ONLINE.app");
        var credentials = new FakeCredentialStore();
        var launcher = new FakeLauncherService { Result = MacLaunchResult.Launched() };
        var viewModel = new MainWindowViewModel(
            new FakeSettingsService(new MacSettings()),
            credentials,
            new FakeInstallResolver(MacInstallResolution.Found(install)),
            launcher);

        await viewModel.InitializeAsync();
        viewModel.Username = " saved-user ";
        viewModel.Password = "save-me";

        await viewModel.LaunchAsync();

        Assert.AreEqual("saved-user", credentials.SavedUsername);
        Assert.AreEqual("save-me", credentials.SavedPassword);
    }

    [TestMethod]
    public async Task LaunchAsyncSavesPasswordToCredentialStoreEvenWhenLaunchFails()
    {
        var install = CreateInstall("/Applications/FINAL FANTASY XIV ONLINE.app");
        var credentials = new FakeCredentialStore();
        var launcher = new FakeLauncherService { Result = MacLaunchResult.Failed("login failed") };
        var viewModel = new MainWindowViewModel(
            new FakeSettingsService(new MacSettings()),
            credentials,
            new FakeInstallResolver(MacInstallResolution.Found(install)),
            launcher);

        await viewModel.InitializeAsync();
        viewModel.Username = "saved-user";
        viewModel.Password = "do-not-save";

        await viewModel.LaunchAsync();

        Assert.AreEqual("saved-user", credentials.SavedUsername);
        Assert.AreEqual("do-not-save", credentials.SavedPassword);
    }

    [TestMethod]
    public async Task CopyStatusCommandCopiesCurrentStatusMessage()
    {
        var clipboard = new FakeClipboardService();
        var viewModel = new MainWindowViewModel(
            new FakeSettingsService(new MacSettings()),
            new FakeCredentialStore(),
            new FakeInstallResolver(MacInstallResolution.NotFound("/Applications/FINAL FANTASY XIV ONLINE.app", "not resolved")),
            new FakeLauncherService(),
            clipboard);

        await viewModel.InitializeAsync();
        await viewModel.CopyStatusCommand.ExecuteAsync();

        Assert.AreEqual(viewModel.StatusMessage, clipboard.Text);
    }

    [TestMethod]
    public async Task LaunchAsyncDisablesLaunchAndIgnoresSecondLaunchWhileInProgress()
    {
        var install = CreateInstall("/Applications/FINAL FANTASY XIV ONLINE.app");
        var launcher = new FakeLauncherService { DelayCompletion = true };
        var viewModel = new MainWindowViewModel(
            new FakeSettingsService(new MacSettings()),
            new FakeInstallResolver(MacInstallResolution.Found(install)),
            launcher);

        await viewModel.InitializeAsync();
        viewModel.Username = "saved-user";
        viewModel.Password = "password";

        var launchTask = viewModel.LaunchAsync();
        await launcher.WaitUntilStartedAsync();

        Assert.IsTrue(viewModel.IsBusy);
        Assert.IsFalse(viewModel.CanLaunch);
        Assert.IsFalse(viewModel.LaunchCommand.CanExecute(null));

        await viewModel.LaunchAsync();

        Assert.AreEqual(1, launcher.Requests.Count);
        launcher.CompleteDelayedLaunch();
        await launchTask;
    }

    [TestMethod]
    public async Task LaunchAsyncReResolvesEditedOverrideBeforeLaunching()
    {
        var defaultInstall = CreateInstall("/Applications/FINAL FANTASY XIV ONLINE.app");
        var overrideInstall = CreateInstall("/Applications/Custom.app");
        var settingsService = new FakeSettingsService(new MacSettings());
        var resolver = new FakeInstallResolver(path =>
            path == overrideInstall.AppBundle.FullName
                ? MacInstallResolution.Found(overrideInstall)
                : MacInstallResolution.Found(defaultInstall));
        var launcher = new FakeLauncherService { Result = MacLaunchResult.Launched() };
        var viewModel = new MainWindowViewModel(settingsService, resolver, launcher);

        await viewModel.InitializeAsync();
        viewModel.Username = "saved-user";
        viewModel.Password = "do-not-save";
        viewModel.OfficialAppPathOverride = overrideInstall.AppBundle.FullName;

        await viewModel.LaunchAsync();

        Assert.AreEqual(overrideInstall.AppBundle.FullName, launcher.Requests[0].Install.AppBundle.FullName);
        Assert.AreEqual(overrideInstall.GameRoot.FullName, launcher.Requests[0].Install.GameRoot.FullName);
        Assert.AreEqual(overrideInstall.AppBundle.FullName, viewModel.ResolvedAppPath);
        Assert.IsNotNull(settingsService.SavedSettings);
        Assert.AreEqual(overrideInstall.AppBundle.FullName, settingsService.SavedSettings.OfficialAppPathOverride);
    }

    [TestMethod]
    public async Task InitializeAsyncReportsSettingsLoadFailure()
    {
        var settingsService = new FakeSettingsService(new MacSettings())
        {
            LoadException = new InvalidOperationException("settings json is invalid"),
        };
        var viewModel = new MainWindowViewModel(
            settingsService,
            new FakeInstallResolver(MacInstallResolution.NotFound("/Applications/FINAL FANTASY XIV ONLINE.app", "not resolved")),
            new FakeLauncherService());

        await viewModel.InitializeAsync();

        StringAssert.Contains(viewModel.StatusMessage, "Could not load Mac settings");
        StringAssert.Contains(viewModel.StatusMessage, "settings json is invalid");
        Assert.IsFalse(viewModel.IsInstallDetected);
        Assert.AreEqual("Not resolved", viewModel.GameRootPathDisplay);
    }

    private static OfficialMacAppInstall CreateInstall(string appPath)
    {
        var appBundle = new DirectoryInfo(appPath);
        var gameRoot = new DirectoryInfo(Path.Combine(appPath, "Contents", "SharedSupport", "GameRoot"));
        var wineExecutable = new FileInfo(Path.Combine(appPath, "Contents", "SharedSupport", "wine"));
        var winePrefix = new DirectoryInfo(Path.Combine(appPath, "Contents", "SharedSupport", "prefix"));

        return new OfficialMacAppInstall(appBundle, gameRoot, wineExecutable, winePrefix);
    }

    private sealed class FakeSettingsService : IMacSettingsService
    {
        private readonly MacSettings settings;

        public FakeSettingsService(MacSettings settings)
        {
            this.settings = settings;
        }

        public MacSettings? SavedSettings { get; private set; }

        public Exception? LoadException { get; set; }

        public Task<MacSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (this.LoadException is not null)
                throw this.LoadException;

            return Task.FromResult(this.settings);
        }

        public Task SaveAsync(MacSettings settings, CancellationToken cancellationToken = default)
        {
            this.SavedSettings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCredentialStore : IMacCredentialStore
    {
        public string? Password { get; set; }

        public string? LastPasswordLookupUsername { get; private set; }

        public string? SavedUsername { get; private set; }

        public string? SavedPassword { get; private set; }

        public Task<string?> GetPasswordAsync(string username, CancellationToken cancellationToken = default)
        {
            this.LastPasswordLookupUsername = username;
            return Task.FromResult(this.Password);
        }

        public Task SavePasswordAsync(string username, string password, CancellationToken cancellationToken = default)
        {
            this.SavedUsername = username;
            this.SavedPassword = password;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public string? Text { get; private set; }

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            this.Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInstallResolver : IMacInstallResolver
    {
        private readonly Func<string?, MacInstallResolution> resolve;

        public FakeInstallResolver(MacInstallResolution resolution)
            : this(_ => resolution)
        {
        }

        public FakeInstallResolver(Func<string?, MacInstallResolution> resolve)
        {
            this.resolve = resolve;
        }

        public string? LastOverridePath { get; private set; }

        public MacInstallResolution Resolve(string? officialAppPathOverride)
        {
            this.LastOverridePath = officialAppPathOverride;
            return this.resolve(officialAppPathOverride);
        }
    }

    private sealed class FakeLauncherService : IMacLauncherService
    {
        public List<MacLaunchRequest> Requests { get; } = [];

        public MacLaunchResult Result { get; set; } = MacLaunchResult.Launched();

        public IProgress<MacLaunchProgress>? LastProgress { get; private set; }

        public bool DelayCompletion { get; set; }

        private readonly TaskCompletionSource started = new();

        private readonly TaskCompletionSource complete = new();

        public Task<MacLaunchResult> LaunchAsync(
            MacLaunchRequest request,
            IProgress<MacLaunchProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            this.Requests.Add(request);
            this.LastProgress = progress;
            progress?.Report(new MacLaunchProgress(MacLaunchStage.Patching, "Boot patching: Downloading 50%", 50, TimeSpan.FromSeconds(10)));
            this.started.TrySetResult();

            return this.DelayCompletion ? this.WaitForCompletionAsync() : Task.FromResult(this.Result);
        }

        public Task WaitUntilStartedAsync()
            => this.started.Task;

        public void CompleteDelayedLaunch()
            => this.complete.TrySetResult();

        private async Task<MacLaunchResult> WaitForCompletionAsync()
        {
            await this.complete.Task;
            return this.Result;
        }
    }
}
