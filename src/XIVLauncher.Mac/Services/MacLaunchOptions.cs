using System;
using System.Collections.Generic;
using System.Linq;

namespace XIVLauncher.Mac.Services;

public sealed class MacLaunchOptions
{
    public bool ExperimentalDalamud { get; init; }

    public static MacLaunchOptions FromArgs(IEnumerable<string> args)
    {
        var experimentalDalamud = args.Any(arg =>
            string.Equals(arg, "--experimental-dalamud", StringComparison.OrdinalIgnoreCase));

        return new MacLaunchOptions
        {
            ExperimentalDalamud = experimentalDalamud,
        };
    }
}
