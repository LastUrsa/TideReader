namespace TideReader.Backend.Services;

internal static class OutputPathPolicy
{
    public static string NormalizeFolderPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Output folder is required.", nameof(path));
        }

        var trimmed = path.Trim();
        if (trimmed.StartsWith(@"\\?\") || trimmed.StartsWith(@"\\.\"))
        {
            throw new ArgumentException("Device paths are not allowed.", nameof(path));
        }

        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new ArgumentException("Output folder must be an absolute path.", nameof(path));
        }

        var fullPath = Path.GetFullPath(trimmed);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Output folder must resolve to a rooted path.", nameof(path));
        }

        if (PathsEqual(fullPath, root))
        {
            throw new ArgumentException("Using a drive or share root as the output folder is not allowed.", nameof(path));
        }

        if (File.Exists(fullPath))
        {
            throw new ArgumentException("Output folder cannot point to an existing file.", nameof(path));
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
