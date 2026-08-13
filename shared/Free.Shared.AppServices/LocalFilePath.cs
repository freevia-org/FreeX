using Free.Shared.IO;

namespace Free.Shared.AppServices;

public static class LocalFilePath
{
    public static bool TryNormalize(string? candidate, out string normalizedPath) =>
        TryNormalize(candidate, OperatingSystem.IsWindows(), out normalizedPath);

    internal static bool TryNormalize(string? candidate, bool isWindows, out string normalizedPath)
    {
        normalizedPath = "";
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        var path = candidate.Trim();
        if (TryCreateExplicitUri(path, out var uri))
        {
            if (!uri.IsFile)
                return false;

            if (IsForeignWindowsDriveFileUri(uri, isWindows))
                return false;

            path = uri.LocalPath;
        }

        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (path.Contains('\0', StringComparison.Ordinal))
            return false;

        if (IsForeignWindowsDriveRootedPath(path, isWindows))
            return false;

        if (IsUnixAbsolutePath(path))
        {
            normalizedPath = path;
            return true;
        }

        return FilePathPolicy.TryGetFullPath(path, out normalizedPath);
    }

    private static bool TryCreateExplicitUri(string candidate, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
            return false;

        if (IsWindowsDrivePath(candidate, parsed.Scheme))
            return false;

        uri = parsed;
        return true;
    }

    private static bool IsWindowsDrivePath(string candidate, string scheme) =>
        scheme.Length == 1 &&
        candidate.Length >= 2 &&
        candidate[1] == ':' &&
        char.IsAsciiLetter(candidate[0]);

    private static bool IsForeignWindowsDriveRootedPath(string path, bool isWindows) =>
        !isWindows && IsWindowsDriveRootedPath(path);

    private static bool IsForeignWindowsDriveFileUri(Uri uri, bool isWindows) =>
        !isWindows &&
        uri.IsFile &&
        IsWindowsDriveUriPath(Uri.UnescapeDataString(uri.AbsolutePath));

    private static bool IsWindowsDriveUriPath(string path) =>
        IsWindowsDriveRootedPath(path) ||
        path.Length >= 4 &&
        path[0] == '/' &&
        IsWindowsDriveRootedPath(path[1..]);

    private static bool IsWindowsDriveRootedPath(string path) =>
        path.Length >= 3 &&
        path[1] == ':' &&
        path[2] is '\\' or '/' &&
        char.IsAsciiLetter(path[0]);

    private static bool IsUnixAbsolutePath(string path) =>
        path.Length >= 2 &&
        path[0] == '/' &&
        path[1] is not '/' and not '\\';
}
