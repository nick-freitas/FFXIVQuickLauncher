using System.Text.Json;
using XIVLauncher.Common;
using XIVLauncher.Mac.Settings;

namespace XIVLauncher.Mac.Tests.Settings;

[TestClass]
public sealed class MacSettingsServiceTests
{
    [TestMethod]
    public void DefaultSettingsPathUsesMacApplicationSupport()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support",
            "XIVLauncherMac",
            "settings.json");

        Assert.AreEqual(expected, MacSettingsService.DefaultSettingsPath);
    }

    [TestMethod]
    public async Task LoadAsyncReturnsDefaultsWhenSettingsFileDoesNotExist()
    {
        var settingsPath = CreateSettingsPath();
        var service = new MacSettingsService(settingsPath);

        var settings = await service.LoadAsync();

        Assert.IsNull(settings.OfficialAppPathOverride);
        Assert.IsNull(settings.LastUsername);
        Assert.AreEqual(ClientLanguage.English, settings.ClientLanguage);
        Assert.IsFalse(settings.IsFreeTrial);
        Assert.IsFalse(settings.IsSteam);
    }

    [TestMethod]
    public async Task SaveAsyncPersistsSettingsThatLoadAsyncReadsBack()
    {
        var settingsPath = CreateSettingsPath();
        var service = new MacSettingsService(settingsPath);
        var expected = new MacSettings
        {
            OfficialAppPathOverride = "/Applications/FINAL FANTASY XIV ONLINE.app",
            LastUsername = "example-user",
            ClientLanguage = ClientLanguage.French,
            IsFreeTrial = true,
            IsSteam = true,
        };

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();

        Assert.AreEqual(expected.OfficialAppPathOverride, actual.OfficialAppPathOverride);
        Assert.AreEqual(expected.LastUsername, actual.LastUsername);
        Assert.AreEqual(expected.ClientLanguage, actual.ClientLanguage);
        Assert.AreEqual(expected.IsFreeTrial, actual.IsFreeTrial);
        Assert.AreEqual(expected.IsSteam, actual.IsSteam);
    }

    [TestMethod]
    public async Task SaveAsyncDoesNotPersistPasswordField()
    {
        var settingsPath = CreateSettingsPath();
        var service = new MacSettingsService(settingsPath);

        await service.SaveAsync(new MacSettings { LastUsername = "example-user" });
        var json = await File.ReadAllTextAsync(settingsPath);
        using var document = JsonDocument.Parse(json);

        Assert.IsFalse(document.RootElement.TryGetProperty("Password", out _));
        Assert.IsFalse(document.RootElement.TryGetProperty("password", out _));
    }

    private static string CreateSettingsPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "xivlauncher-mac-settings-tests", Guid.NewGuid().ToString("N"));

        return Path.Combine(directory, "settings.json");
    }
}
