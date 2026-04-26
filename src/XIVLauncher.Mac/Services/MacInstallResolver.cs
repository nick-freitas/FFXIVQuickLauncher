using System.IO;
using XIVLauncher.Common.Game.OfficialMacApp;

namespace XIVLauncher.Mac.Services;

public interface IMacInstallResolver
{
    MacInstallResolution Resolve(string? officialAppPathOverride);
}

public sealed class MacInstallResolver : IMacInstallResolver
{
    public MacInstallResolution Resolve(string? officialAppPathOverride)
    {
        if (!string.IsNullOrWhiteSpace(officialAppPathOverride))
        {
            var overridePath = officialAppPathOverride.Trim();
            var install = OfficialMacAppLocator.TryResolve(new DirectoryInfo(overridePath));

            return install is not null
                ? MacInstallResolution.Found(install)
                : MacInstallResolution.NotFound(
                    overridePath,
                    "Official app was not found at the override path. Check the path or clear the override.");
        }

        var defaultInstall = OfficialMacAppLocator.TryResolveDefault();

        return defaultInstall is not null
            ? MacInstallResolution.Found(defaultInstall)
            : MacInstallResolution.NotFound(
                OfficialMacAppLocator.DefaultAppPath,
                "Official app was not found. Set the app path override if it is installed somewhere else.");
    }
}
