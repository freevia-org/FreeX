namespace FreeX.App.Services;

public static class LocalFilePath
{
    public static bool TryNormalize(string? candidate, out string normalizedPath)
    {
        normalizedPath = "";
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        var path = candidate.Trim();
        if (TryCreateExplicitUri(path, out var uri))
        {
            if (!uri.IsFile)
                return false;

            path = uri.LocalPath;
        }

        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (path.Contains('\0', StringComparison.Ordinal))
            return false;

        if (IsUnixAbsolutePath(path))
        {
            normalizedPath = path;
            return true;
        }

        try
        {
            normalizedPath = Path.GetFullPath(path);
            return !string.IsNullOrWhiteSpace(normalizedPath);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
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

    private static bool IsUnixAbsolutePath(string path) =>
        path.Length >= 2 &&
        path[0] == '/' &&
        path[1] is not '/' and not '\\';
}
