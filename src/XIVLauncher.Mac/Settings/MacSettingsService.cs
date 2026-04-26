using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace XIVLauncher.Mac.Settings;

public interface IMacSettingsService
{
    Task<MacSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(MacSettings settings, CancellationToken cancellationToken = default);
}

public sealed class MacSettingsService : IMacSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string settingsPath;

    public MacSettingsService()
        : this(DefaultSettingsPath)
    {
    }

    public MacSettingsService(string settingsPath)
    {
        this.settingsPath = settingsPath;
    }

    public static string DefaultApplicationSupportDirectory
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support",
            "XIVLauncherMac");

    public static string DefaultSettingsPath
        => Path.Combine(
            DefaultApplicationSupportDirectory,
            "settings.json");

    public async Task<MacSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(this.settingsPath))
            return new MacSettings();

        await using var stream = File.OpenRead(this.settingsPath);
        var settings = await JsonSerializer.DeserializeAsync<MacSettings>(stream, SerializerOptions, cancellationToken);

        return settings ?? new MacSettings();
    }

    public async Task SaveAsync(MacSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(this.settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(this.settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
    }
}
