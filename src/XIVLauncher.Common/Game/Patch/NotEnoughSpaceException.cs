using System;

namespace XIVLauncher.Common.Game.Patch;

public class NotEnoughSpaceException : Exception
{
    public enum SpaceKind
    {
        Patches,
        AllPatches,
        Game,
    }

    public SpaceKind Kind { get; private set; }

    public long BytesRequired { get; set; }

    public long BytesFree { get; set; }

    public NotEnoughSpaceException(SpaceKind kind, long required, long free)
        : base(CreateMessage(kind, required, free))
    {
        this.Kind = kind;
        this.BytesRequired = required;
        this.BytesFree = free;
    }

    private static string CreateMessage(SpaceKind kind, long required, long free)
    {
        var location = kind switch
        {
            SpaceKind.Patches => "patch download folder",
            SpaceKind.AllPatches => "patch download folder",
            SpaceKind.Game => "game folder",
            _ => "target folder",
        };

        return $"Not enough free disk space in the {location}. Required: {FormatBytes(required)}. Available: {FormatBytes(free)}.";
    }

    private static string FormatBytes(long bytes)
        => $"{bytes / 1024d / 1024d / 1024d:0.##} GB";
}
