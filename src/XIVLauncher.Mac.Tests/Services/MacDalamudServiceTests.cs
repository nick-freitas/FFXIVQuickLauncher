using XIVLauncher.Common;
using XIVLauncher.Common.Dalamud;
using XIVLauncher.Common.Game.OfficialMacApp;
using XIVLauncher.Common.PlatformAbstractions;
using XIVLauncher.Mac.Services;

namespace XIVLauncher.Mac.Tests.Services;

[TestClass]
public sealed class MacDalamudServiceTests
{
    [TestMethod]
    public void CreatePathsUsesMacApplicationSupportDirectory()
    {
        var paths = MacDalamudPaths.Create("/Users/test/Library/Application Support/XIVLauncherMac");

        Assert.AreEqual("/Users/test/Library/Application Support/XIVLauncherMac/addon", paths.AddonDirectory.FullName);
        Assert.AreEqual("/Users/test/Library/Application Support/XIVLauncherMac/runtime", paths.RuntimeDirectory.FullName);
        Assert.AreEqual("/Users/test/Library/Application Support/XIVLauncherMac/dalamudAssets", paths.AssetRootDirectory.FullName);
        Assert.AreEqual("/Users/test/Library/Application Support/XIVLauncherMac/installedPlugins", paths.PluginDirectory.FullName);
        Assert.AreEqual("/Users/test/Library/Application Support/XIVLauncherMac", paths.ConfigDirectory.FullName);
        Assert.AreEqual("/Users/test/Library/Application Support/XIVLauncherMac/logs", paths.LogDirectory.FullName);
    }

    [TestMethod]
    public async Task PrepareAsyncReturnsFailedWhenUpdaterFactoryFails()
    {
        var service = new MacDalamudService(new FailingDalamudUpdaterFactory());

        var result = await service.PrepareAsync(CreateInstall(), new DirectoryInfo("/game"), ClientLanguage.English, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.ErrorMessage, "Could not prepare Dalamud");
        StringAssert.Contains(result.ErrorMessage, "network failed");
    }

    [TestMethod]
    public async Task PrepareAsyncCreatesDirectoriesRunsUpdaterAndReturnsRunner()
    {
        var supportDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var launcherAdapter = new TestDalamudLauncherAdapter(DalamudLauncher.DalamudInstallState.Ok);
        var launcherFactory = new CapturingDalamudLauncherAdapterFactory(launcherAdapter);
        var service = new MacDalamudService(
            new CapturingDalamudUpdaterFactory(),
            launcherFactory,
            supportDirectory);

        try
        {
            var result = await service.PrepareAsync(CreateInstall(), new DirectoryInfo("/game"), ClientLanguage.English, CancellationToken.None);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.GameRunner);
            Assert.IsTrue(Directory.Exists(supportDirectory));
            Assert.IsTrue(Directory.Exists(Path.Combine(supportDirectory, "installedPlugins")));
            Assert.IsTrue(Directory.Exists(Path.Combine(supportDirectory, "logs")));
            Assert.IsTrue(launcherAdapter.RunUpdaterCalled);
            Assert.IsTrue(launcherAdapter.HoldForUpdateCalled);
            Assert.AreEqual("/game", launcherAdapter.HoldForUpdateGamePath?.FullName);
            Assert.AreEqual(supportDirectory, launcherFactory.Paths?.ConfigDirectory.FullName);
            Assert.AreEqual(ClientLanguage.English, launcherFactory.Language);
        }
        finally
        {
            if (Directory.Exists(supportDirectory))
                Directory.Delete(supportDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareAsyncReturnsFailedWhenDalamudIsUnavailableForGameVersion()
    {
        var supportDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var launcherAdapter = new TestDalamudLauncherAdapter(DalamudLauncher.DalamudInstallState.OutOfDate);
        var service = new MacDalamudService(
            new CapturingDalamudUpdaterFactory(),
            new CapturingDalamudLauncherAdapterFactory(launcherAdapter),
            supportDirectory);

        try
        {
            var result = await service.PrepareAsync(CreateInstall(), new DirectoryInfo("/game"), ClientLanguage.English, CancellationToken.None);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNull(result.GameRunner);
            StringAssert.Contains(result.ErrorMessage, "Dalamud is unavailable for the game version");
            Assert.IsTrue(launcherAdapter.RunUpdaterCalled);
            Assert.IsTrue(launcherAdapter.HoldForUpdateCalled);
        }
        finally
        {
            if (Directory.Exists(supportDirectory))
                Directory.Delete(supportDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareAsyncPropagatesPreCanceledToken()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        var service = new MacDalamudService(
            new CapturingDalamudUpdaterFactory(),
            new CapturingDalamudLauncherAdapterFactory(new TestDalamudLauncherAdapter(DalamudLauncher.DalamudInstallState.Ok)),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        try
        {
            await service.PrepareAsync(CreateInstall(), new DirectoryInfo("/game"), ClientLanguage.English, cancellationTokenSource.Token);
            Assert.Fail("PrepareAsync should propagate cancellation.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    [TestMethod]
    public async Task PrepareAsyncPropagatesCancellationAfterUpdaterRuns()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var launcherAdapter = new TestDalamudLauncherAdapter(
            DalamudLauncher.DalamudInstallState.Ok,
            cancellationTokenSource);
        var service = new MacDalamudService(
            new CapturingDalamudUpdaterFactory(),
            new CapturingDalamudLauncherAdapterFactory(launcherAdapter),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        try
        {
            await service.PrepareAsync(CreateInstall(), new DirectoryInfo("/game"), ClientLanguage.English, cancellationTokenSource.Token);
            Assert.Fail("PrepareAsync should propagate cancellation after the updater starts.");
        }
        catch (OperationCanceledException)
        {
        }

        Assert.IsTrue(launcherAdapter.RunUpdaterCalled);
        Assert.IsTrue(launcherAdapter.HoldForUpdateCalled);
    }

    private static OfficialMacAppInstall CreateInstall()
        => new(
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app"),
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app/prefix/drive_c/game"),
            new FileInfo("/Applications/FINAL FANTASY XIV ONLINE.app/wine"),
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app/prefix"));

    private sealed class FailingDalamudUpdaterFactory : IMacDalamudUpdaterFactory
    {
        public DalamudUpdater Create(MacDalamudPaths paths)
            => throw new InvalidOperationException("network failed");
    }

    private sealed class CapturingDalamudUpdaterFactory : IMacDalamudUpdaterFactory
    {
        public DalamudUpdater Create(MacDalamudPaths paths)
            => throw new NotSupportedException("Test launcher adapter should run the update.");
    }

    private sealed class CapturingDalamudLauncherAdapterFactory : IMacDalamudLauncherAdapterFactory
    {
        private readonly IMacDalamudLauncherAdapter launcherAdapter;

        public CapturingDalamudLauncherAdapterFactory(IMacDalamudLauncherAdapter launcherAdapter)
        {
            this.launcherAdapter = launcherAdapter;
        }

        public MacDalamudPaths? Paths { get; private set; }

        public ClientLanguage? Language { get; private set; }

        public IMacDalamudLauncherAdapter Create(
            OfficialMacAppInstall install,
            IMacDalamudUpdaterFactory updaterFactory,
            DirectoryInfo gamePath,
            MacDalamudPaths paths,
            ClientLanguage language)
        {
            this.Paths = paths;
            this.Language = language;

            return this.launcherAdapter;
        }
    }

    private sealed class TestDalamudLauncherAdapter : IMacDalamudLauncherAdapter
    {
        private readonly DalamudLauncher.DalamudInstallState installState;
        private readonly IGameRunner gameRunner = new TestGameRunner();

        public TestDalamudLauncherAdapter(DalamudLauncher.DalamudInstallState installState)
        {
            this.installState = installState;
        }

        public bool RunUpdaterCalled { get; private set; }

        public bool HoldForUpdateCalled { get; private set; }

        public DirectoryInfo? HoldForUpdateGamePath { get; private set; }

        private CancellationTokenSource? CancellationTokenSource { get; }

        public TestDalamudLauncherAdapter(
            DalamudLauncher.DalamudInstallState installState,
            CancellationTokenSource cancellationTokenSource)
            : this(installState)
        {
            this.CancellationTokenSource = cancellationTokenSource;
        }

        public void RunUpdater(string? betaKind, string? betaKey, CancellationToken cancellationToken)
        {
            Assert.IsNull(betaKind);
            Assert.IsNull(betaKey);
            Assert.IsFalse(cancellationToken.IsCancellationRequested);

            this.RunUpdaterCalled = true;
            this.CancellationTokenSource?.Cancel();
        }

        public DalamudLauncher.DalamudInstallState HoldForUpdate(DirectoryInfo gamePath, CancellationToken cancellationToken)
        {
            this.HoldForUpdateCalled = true;
            this.HoldForUpdateGamePath = gamePath;
            cancellationToken.ThrowIfCancellationRequested();

            return this.installState;
        }

        public IGameRunner CreateGameRunner()
            => this.gameRunner;
    }

    private sealed class TestGameRunner : IGameRunner
    {
        public System.Diagnostics.Process? Start(
            string path,
            string workingDirectory,
            string arguments,
            IDictionary<string, string> environment,
            DpiAwareness dpiAwareness)
            => null;
    }
}
