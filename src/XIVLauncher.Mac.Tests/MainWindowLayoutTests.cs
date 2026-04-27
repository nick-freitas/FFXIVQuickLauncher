using System.Xml.Linq;

namespace XIVLauncher.Mac.Tests;

[TestClass]
public sealed class MainWindowLayoutTests
{
    [TestMethod]
    public void AppUsesLightThemeBecauseMainWindowPaintsLightSurfaces()
    {
        var document = XDocument.Load(FindRepoFile("src/XIVLauncher.Mac/App.axaml"));

        Assert.AreEqual("Light", document.Root?.Attribute("RequestedThemeVariant")?.Value);
    }

    [TestMethod]
    public void MainWindowKeepsLaunchButtonInFixedFooterBelowScrollableContent()
    {
        var document = XDocument.Load(FindRepoFile("src/XIVLauncher.Mac/MainWindow.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        var rootGrid = document.Root?.Elements(avalonia + "Grid").Single();

        Assert.IsNotNull(rootGrid);
        Assert.AreEqual("*,Auto", rootGrid.Attribute("RowDefinitions")?.Value);
        Assert.IsTrue(rootGrid.Elements(avalonia + "ScrollViewer").Any(x => x.Attribute("Grid.Row")?.Value == "0"));

        var footer = rootGrid.Elements(avalonia + "Border")
            .SingleOrDefault(x => x.Attribute("Grid.Row")?.Value == "1");
        Assert.IsNotNull(footer);
        Assert.IsTrue(footer.Descendants(avalonia + "Button").Any(x => x.Attribute("Content")?.Value == "Launch"));
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}.");
    }
}
