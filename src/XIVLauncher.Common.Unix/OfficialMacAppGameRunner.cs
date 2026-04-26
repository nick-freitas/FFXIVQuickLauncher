using System.Collections.Generic;
using System.Diagnostics;
using XIVLauncher.Common.Game.OfficialMacApp;
using XIVLauncher.Common.PlatformAbstractions;

namespace XIVLauncher.Common.Unix;

public sealed class OfficialMacAppGameRunner : IGameRunner
{
    private readonly OfficialMacAppInstall install;

    public OfficialMacAppGameRunner(OfficialMacAppInstall install)
    {
        this.install = install;
    }

    public Process? Start(string path, string workingDirectory, string arguments, IDictionary<string, string> environment, DpiAwareness dpiAwareness)
    {
        var psi = new ProcessStartInfo(install.WineExecutable.FullName)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };

        psi.ArgumentList.Add(path);
        psi.ArgumentList.Add(arguments);

        foreach (var pair in environment)
        {
            psi.EnvironmentVariables[pair.Key] = pair.Value;
        }

        psi.EnvironmentVariables["WINEPREFIX"] = install.WinePrefix.FullName;

        return Process.Start(psi);
    }
}
