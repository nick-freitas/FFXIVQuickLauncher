using XIVLauncher.Common;

namespace XIVLauncher.Mac.Settings;

public sealed class MacSettings
{
    public string? OfficialAppPathOverride { get; set; }

    public string? LastUsername { get; set; }

    public ClientLanguage ClientLanguage { get; set; } = ClientLanguage.English;

    public bool IsFreeTrial { get; set; }

    public bool IsSteam { get; set; }
}
