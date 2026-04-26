using System;

namespace XIVLauncher.Common.Game.Patch;

public static class PatchPlatform
{
    public static string GetPatchRoute(Platform platform) => platform switch
    {
        Platform.Win32 => "win32",
        Platform.Win32OnLinux => "win32",
        Platform.Linux => "win32",
        Platform.Mac => "win32",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null),
    };
}
