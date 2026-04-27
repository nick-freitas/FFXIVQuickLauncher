using System;
using System.Collections.Generic;
using System.Linq;

namespace XIVLauncher.Mac.Services;

public sealed class MacLaunchOptions
{
    public bool UseDalamud { get; init; } = true;

    public static MacLaunchOptions FromArgs(IEnumerable<string> args)
    {
        var disableDalamud = args.Any(arg =>
            string.Equals(arg, "--no-dalamud", StringComparison.OrdinalIgnoreCase));

        return new MacLaunchOptions
        {
            UseDalamud = !disableDalamud,
        };
    }
}
