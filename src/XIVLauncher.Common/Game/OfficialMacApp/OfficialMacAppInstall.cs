using System.IO;

namespace XIVLauncher.Common.Game.OfficialMacApp;

public sealed class OfficialMacAppInstall
{
    public OfficialMacAppInstall(DirectoryInfo appBundle, DirectoryInfo gameRoot, FileInfo wineExecutable, DirectoryInfo winePrefix)
    {
        AppBundle = appBundle;
        GameRoot = gameRoot;
        WineExecutable = wineExecutable;
        WinePrefix = winePrefix;
    }

    public DirectoryInfo AppBundle { get; }

    public DirectoryInfo GameRoot { get; }

    public FileInfo WineExecutable { get; }

    public DirectoryInfo WinePrefix { get; }
}
