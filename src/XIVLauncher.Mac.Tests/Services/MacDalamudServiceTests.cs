using XIVLauncher.Common;
using XIVLauncher.Common.Dalamud;
using XIVLauncher.Common.Game.OfficialMacApp;
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
}
