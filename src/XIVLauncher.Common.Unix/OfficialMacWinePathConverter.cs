using System;
using System.IO;

namespace XIVLauncher.Common.Unix;

public sealed class OfficialMacWinePathConverter
{
    private readonly string driveCRoot;

    public OfficialMacWinePathConverter(DirectoryInfo winePrefix)
    {
        this.driveCRoot = Path.GetFullPath(Path.Combine(winePrefix.FullName, "drive_c"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public string ToWinePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));

        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("Path must be absolute.", nameof(path));

        var fullPath = Path.GetFullPath(path);

        if (string.Equals(fullPath, this.driveCRoot, StringComparison.Ordinal))
            return @"C:\";

        if (fullPath.StartsWith(this.driveCRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            var relative = fullPath[(this.driveCRoot.Length + 1)..];
            return @"C:\" + relative.Replace('/', '\\');
        }

        return "Z:" + fullPath.Replace('/', '\\');
    }
}
