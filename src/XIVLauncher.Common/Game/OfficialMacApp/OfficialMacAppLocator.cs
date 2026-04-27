using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using XIVLauncher.Common.Util;

namespace XIVLauncher.Common.Game.OfficialMacApp;

public static class OfficialMacAppLocator
{
    public const string DefaultAppPath = "/Applications/FINAL FANTASY XIV ONLINE.app";

    private const string BundleIdentifier = "com.square-enix.finalfantasyxiv";
    private const string ApplicationSupportAppFolder = "FINAL FANTASY XIV ONLINE";
    private const string ApplicationSupportPrefix = "Bottles/published_Final_Fantasy";
    private const string RelativePrefix = "Contents/SharedSupport/finalfantasyxiv/support/published_Final_Fantasy";
    private const string RelativeWine = "Contents/SharedSupport/finalfantasyxiv/FINAL FANTASY XIV ONLINE/wine";
    private const string RelativeGameRoot = "drive_c/Program Files (x86)/SquareEnix/FINAL FANTASY XIV - A Realm Reborn";

    public static OfficialMacAppInstall? TryResolveDefault() => TryResolve(new DirectoryInfo(DefaultAppPath));

    public static OfficialMacAppInstall? TryResolve(DirectoryInfo appBundle)
        => TryResolve(appBundle, null);

    public static OfficialMacAppInstall? TryResolve(DirectoryInfo appBundle, DirectoryInfo? applicationSupportDirectory)
    {
        if (!appBundle.Exists || !HasOfficialBundleIdentifier(appBundle))
            return null;

        var bundledWinePrefix = new DirectoryInfo(Path.Combine(appBundle.FullName, RelativePrefix));
        var winePrefix = TryGetUserWinePrefix(applicationSupportDirectory) ?? bundledWinePrefix;
        var wine = new FileInfo(Path.Combine(appBundle.FullName, RelativeWine));
        var gameRoot = new DirectoryInfo(Path.Combine(winePrefix.FullName, RelativeGameRoot));

        if (!wine.Exists || !winePrefix.Exists || !GameHelpers.PathHasExistingInstall(gameRoot.FullName))
            return null;

        return new OfficialMacAppInstall(appBundle, gameRoot, wine, winePrefix);
    }

    private static DirectoryInfo? TryGetUserWinePrefix(DirectoryInfo? applicationSupportDirectory)
    {
        var appSupport = applicationSupportDirectory?.FullName
                         ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appSupport))
            return null;

        var winePrefix = new DirectoryInfo(Path.Combine(appSupport, ApplicationSupportAppFolder, ApplicationSupportPrefix));
        var gameRoot = new DirectoryInfo(Path.Combine(winePrefix.FullName, RelativeGameRoot));

        return winePrefix.Exists && GameHelpers.PathHasExistingInstall(gameRoot.FullName)
            ? winePrefix
            : null;
    }

    private static bool HasOfficialBundleIdentifier(DirectoryInfo appBundle)
    {
        var plist = Path.Combine(appBundle.FullName, "Contents", "Info.plist");
        if (!File.Exists(plist))
            return false;

        try
        {
            var document = XDocument.Load(plist);
            var elements = document.Descendants().ToList();

            for (var i = 0; i < elements.Count - 1; i++)
            {
                if (elements[i].Name.LocalName == "key" &&
                    elements[i].Value == "CFBundleIdentifier" &&
                    elements[i + 1].Name.LocalName == "string" &&
                    elements[i + 1].Value == BundleIdentifier)
                    return true;
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }
}
