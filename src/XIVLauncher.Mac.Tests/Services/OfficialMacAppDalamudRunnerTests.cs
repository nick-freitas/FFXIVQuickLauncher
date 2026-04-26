using System.Diagnostics;
using XIVLauncher.Common;
using XIVLauncher.Common.Dalamud;
using XIVLauncher.Common.Game.OfficialMacApp;
using XIVLauncher.Common.Unix;

namespace XIVLauncher.Mac.Tests.Services;

[TestClass]
public sealed class OfficialMacAppDalamudRunnerTests
{
    [TestMethod]
    public void BuildStartInfoUsesOfficialWineAndPrefix()
    {
        var install = CreateInstall();
        var runner = new FileInfo("/Users/test/Library/Application Support/XIVLauncherMac/addon/Hooks/1/Dalamud.Injector.exe");
        var startInfo = CreateDalamudStartInfo();

        var plan = OfficialMacAppDalamudRunner.BuildLaunchPlan(
            install,
            runner,
            fakeLogin: false,
            noPlugins: false,
            noThirdPlugins: false,
            gameExe: new FileInfo("/Applications/FINAL FANTASY XIV ONLINE.app/prefix/drive_c/game/ffxiv_dx11.exe"),
            gameArgs: "DEV.TestSID",
            environment: new Dictionary<string, string> { ["DALAMUD_RUNTIME"] = "/runtime" },
            loadMethod: DalamudLoadMethod.DllInject,
            startInfo);

        Assert.AreEqual(install.WineExecutable.FullName, plan.FileName);
        Assert.AreEqual("DEV.TestSID", plan.Arguments.Split("-- ").Last());
        Assert.AreEqual(install.WinePrefix.FullName, plan.Environment["WINEPREFIX"]);
        StringAssert.Contains(plan.Arguments, @"""Z:\Users\test\Library\Application Support\XIVLauncherMac\addon\Hooks\1\Dalamud.Injector.exe""");
        StringAssert.Contains(plan.Arguments, "--mode=inject");
        StringAssert.Contains(plan.Arguments, @"--game=""C:\game\ffxiv_dx11.exe""");
    }

    [TestMethod]
    public void BuildStartInfoAddsNoPluginFlags()
    {
        var plan = OfficialMacAppDalamudRunner.BuildLaunchPlan(
            CreateInstall(),
            new FileInfo("/runner/Dalamud.Injector.exe"),
            fakeLogin: true,
            noPlugins: true,
            noThirdPlugins: true,
            gameExe: new FileInfo("/game/ffxiv_dx11.exe"),
            gameArgs: "args",
            environment: new Dictionary<string, string>(),
            loadMethod: DalamudLoadMethod.EntryPoint,
            CreateDalamudStartInfo());

        StringAssert.Contains(plan.Arguments, "--fake-arguments");
        StringAssert.Contains(plan.Arguments, "--no-plugin");
        StringAssert.Contains(plan.Arguments, "--no-3rd-plugin");
        StringAssert.Contains(plan.Arguments, "--mode=entrypoint");
    }

    private static OfficialMacAppInstall CreateInstall()
        => new(
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app"),
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app/prefix/drive_c/game"),
            new FileInfo("/Applications/FINAL FANTASY XIV ONLINE.app/wine"),
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app/prefix"));

    private static DalamudStartInfo CreateDalamudStartInfo()
        => new()
        {
            Language = ClientLanguage.English,
            PluginDirectory = "/Users/test/Library/Application Support/XIVLauncherMac/installedPlugins",
            ConfigurationPath = "/Users/test/Library/Application Support/XIVLauncherMac/dalamudConfig.json",
            LoggingPath = "/Users/test/Library/Application Support/XIVLauncherMac/logs",
            AssetDirectory = "/Users/test/Library/Application Support/XIVLauncherMac/dalamudAssets/1",
            GameVersion = "2026.04.26.0000.0000",
            WorkingDirectory = "/Users/test/Library/Application Support/XIVLauncherMac/addon/Hooks/1",
            DelayInitializeMs = 0,
            TroubleshootingPackData = "{}",
        };
}
