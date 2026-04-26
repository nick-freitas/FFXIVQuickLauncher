using XIVLauncher.Mac.Services;

namespace XIVLauncher.Mac.Tests.Services;

[TestClass]
public sealed class MacLaunchOptionsTests
{
    [TestMethod]
    public void FromArgsEnablesExperimentalDalamudWhenFlagIsPresent()
    {
        var options = MacLaunchOptions.FromArgs(["--experimental-dalamud"]);

        Assert.IsTrue(options.ExperimentalDalamud);
    }

    [TestMethod]
    public void FromArgsLeavesExperimentalDalamudDisabledByDefault()
    {
        var options = MacLaunchOptions.FromArgs([]);

        Assert.IsFalse(options.ExperimentalDalamud);
    }

    [TestMethod]
    public void FromArgsAcceptsCaseInsensitiveFlag()
    {
        var options = MacLaunchOptions.FromArgs(["--EXPERIMENTAL-DALAMUD"]);

        Assert.IsTrue(options.ExperimentalDalamud);
    }
}
