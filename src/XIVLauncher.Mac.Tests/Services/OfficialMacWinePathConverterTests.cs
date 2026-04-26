using XIVLauncher.Common.Unix;

namespace XIVLauncher.Mac.Tests.Services;

[TestClass]
public sealed class OfficialMacWinePathConverterTests
{
    [TestMethod]
    public void ToWinePathMapsDriveCInsidePrefix()
    {
        var converter = new OfficialMacWinePathConverter(
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app/prefix"));

        var result = converter.ToWinePath("/Applications/FINAL FANTASY XIV ONLINE.app/prefix/drive_c/Program Files/Game/ffxiv_dx11.exe");

        Assert.AreEqual(@"C:\Program Files\Game\ffxiv_dx11.exe", result);
    }

    [TestMethod]
    public void ToWinePathMapsDriveCRoot()
    {
        var converter = new OfficialMacWinePathConverter(new DirectoryInfo("/prefix"));

        var result = converter.ToWinePath("/prefix/drive_c");

        Assert.AreEqual(@"C:\", result);
    }

    [TestMethod]
    public void ToWinePathMapsExternalAbsolutePathThroughZDrive()
    {
        var converter = new OfficialMacWinePathConverter(
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app/prefix"));

        var result = converter.ToWinePath("/Users/test/Library/Application Support/XIVLauncherMac/dalamudConfig.json");

        Assert.AreEqual(@"Z:\Users\test\Library\Application Support\XIVLauncherMac\dalamudConfig.json", result);
    }

    [TestMethod]
    public void ToWinePathRejectsRelativePaths()
    {
        var converter = new OfficialMacWinePathConverter(new DirectoryInfo("/prefix"));

        Assert.ThrowsException<ArgumentException>(() => converter.ToWinePath("relative/path"));
    }

    [TestMethod]
    public void ToWinePathRejectsEmptyPaths()
    {
        var converter = new OfficialMacWinePathConverter(new DirectoryInfo("/prefix"));

        Assert.ThrowsException<ArgumentException>(() => converter.ToWinePath(""));
    }

    [TestMethod]
    public void ToWinePathRejectsWhitespacePaths()
    {
        var converter = new OfficialMacWinePathConverter(new DirectoryInfo("/prefix"));

        Assert.ThrowsException<ArgumentException>(() => converter.ToWinePath("   "));
    }
}
