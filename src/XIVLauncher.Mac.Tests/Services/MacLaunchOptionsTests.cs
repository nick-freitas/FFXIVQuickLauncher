using XIVLauncher.Mac.Services;

namespace XIVLauncher.Mac.Tests.Services;

[TestClass]
public sealed class MacLaunchOptionsTests
{
    [TestMethod]
    public void FromArgsEnablesDalamudByDefault()
    {
        var options = MacLaunchOptions.FromArgs([]);

        Assert.IsTrue(options.UseDalamud);
    }

    [TestMethod]
    public void FromArgsDisablesDalamudWhenNoDalamudFlagIsPresent()
    {
        var options = MacLaunchOptions.FromArgs(["--no-dalamud"]);

        Assert.IsFalse(options.UseDalamud);
    }

    [TestMethod]
    public void FromArgsAcceptsCaseInsensitiveNoDalamudFlag()
    {
        var options = MacLaunchOptions.FromArgs(["--NO-DALAMUD"]);

        Assert.IsFalse(options.UseDalamud);
    }

}
