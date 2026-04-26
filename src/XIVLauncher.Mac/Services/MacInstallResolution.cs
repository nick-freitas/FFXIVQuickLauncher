using XIVLauncher.Common.Game.OfficialMacApp;

namespace XIVLauncher.Mac.Services;

public sealed class MacInstallResolution
{
    private MacInstallResolution(OfficialMacAppInstall? install, string resolvedAppPath, string statusMessage)
    {
        this.Install = install;
        this.ResolvedAppPath = resolvedAppPath;
        this.StatusMessage = statusMessage;
    }

    public OfficialMacAppInstall? Install { get; }

    public string ResolvedAppPath { get; }

    public string? GameRootPath => this.Install?.GameRoot.FullName;

    public string StatusMessage { get; }

    public bool IsInstalled => this.Install is not null;

    public static MacInstallResolution Found(OfficialMacAppInstall install)
        => new(install, install.AppBundle.FullName, "Official app detected.");

    public static MacInstallResolution NotFound(string resolvedAppPath, string statusMessage)
        => new(null, resolvedAppPath, statusMessage);
}
