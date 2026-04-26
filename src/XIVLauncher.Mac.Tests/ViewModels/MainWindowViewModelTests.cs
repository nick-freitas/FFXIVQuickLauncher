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
        var resolver = new FakeInstallResolver(MacInstallResolution.Found(install));
        var viewModel = new MainWindowViewModel(settingsService, resolver, new FakeLauncherService());

        await viewModel.InitializeAsync();

        Assert.AreEqual(settings.OfficialAppPathOverride, resolver.LastOverridePath);
        Assert.IsTrue(viewModel.IsInstallDetected);
        Assert.AreEqual(install.AppBundle.FullName, viewModel.ResolvedAppPath);
        Assert.AreEqual(install.GameRoot.FullName, viewModel.GameRootPath);
        Assert.AreEqual(install.GameRoot.FullName, viewModel.GameRootPathDisplay);
        Assert.AreEqual("saved-user", viewModel.Username);
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

    private sealed class FakeInstallResolver : IMacInstallResolver
    {
        private readonly MacInstallResolution resolution;

        public FakeInstallResolver(MacInstallResolution resolution)
        {
            this.resolution = resolution;
        }

        public string? LastOverridePath { get; private set; }

        public MacInstallResolution Resolve(string? officialAppPathOverride)
        {
            this.LastOverridePath = officialAppPathOverride;
            return this.resolution;
        }
    }

    private sealed class FakeLauncherService : IMacLauncherService
    {
        public List<MacLaunchRequest> Requests { get; } = [];

        public MacLaunchResult Result { get; set; } = MacLaunchResult.Launched();

        public Task<MacLaunchResult> LaunchAsync(MacLaunchRequest request, CancellationToken cancellationToken = default)
        {
            this.Requests.Add(request);
            return Task.FromResult(this.Result);
        }
    }
}
