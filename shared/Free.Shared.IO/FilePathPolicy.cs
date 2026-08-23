namespace Free.Shared.IO;

/// <summary>
/// Portable, exception-free policy for interpreting user- and shell-supplied file paths.
/// Format support remains the responsibility of each app's adapter catalog.
/// </summary>
public static class FilePathPolicy
{
    public static bool TryGetFullPath(string? candidate, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        var path = candidate.Trim();
        if (HasInvalidPathCharacters(path))
            return false;

        try
        {
            fullPath = Path.GetFullPath(path);
            return !string.IsNullOrWhiteSpace(fullPath);
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

    public static bool TryGetExtension(string? path, out string extension)
    {
        extension = string.Empty;
        if (!IsUsablePathText(path))
            return false;

        try
        {
            extension = Path.GetExtension(path) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(extension);
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

    public static string GetExtensionOrEmpty(string? path) =>
        TryGetExtension(path, out var extension) ? extension : string.Empty;

    public static string NormalizeSafeExtension(string? extension, string fallback = "bin")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);

        var normalized = (extension ?? string.Empty).Trim().TrimStart('.');
        return normalized.Length > 0 && normalized.All(char.IsLetterOrDigit)
            ? normalized.ToLowerInvariant()
            : fallback;
    }

    public static bool TryChangeExtension(
        string? path,
        string? extension,
        out string changedPath)
    {
        changedPath = path ?? string.Empty;
        if (!IsUsablePathText(path) || extension?.IndexOf('\0') >= 0)
            return false;

        var usablePath = path!;
        try
        {
            changedPath = Path.ChangeExtension(usablePath, extension) ?? usablePath;
            return true;
        }
        catch (ArgumentException)
        {
            changedPath = usablePath;
            return false;
        }
        catch (NotSupportedException)
        {
            changedPath = usablePath;
            return false;
        }
        catch (PathTooLongException)
        {
            changedPath = usablePath;
            return false;
        }
    }

    public static bool TryGetFileName(string? path, out string fileName)
    {
        fileName = string.Empty;
        if (!IsUsablePathText(path))
            return false;

        try
        {
            fileName = Path.GetFileName(path) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(fileName);
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

    public static bool TryGetFileNameWithoutExtension(string? path, out string fileName)
    {
        fileName = string.Empty;
        if (!IsUsablePathText(path))
            return false;

        try
        {
            fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(fileName);
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

    public static string FileNameOrPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return TryGetFileName(path, out var fileName) ? fileName : path;
    }

    public static string FileNameWithoutExtensionOr(string? path, string fallback) =>
        TryGetFileNameWithoutExtension(path, out var fileName) ? fileName : fallback;

    public static bool AreEquivalent(string? left, string? right)
    {
        if (!TryGetFullPath(left, out var normalizedLeft) ||
            !TryGetFullPath(right, out var normalizedRight))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(normalizedLeft, normalizedRight, comparison);
    }

    private static bool IsUsablePathText(string? path) =>
        !string.IsNullOrWhiteSpace(path) && !HasInvalidPathCharacters(path);

    private static bool HasInvalidPathCharacters(string path) =>
        path.IndexOfAny(Path.GetInvalidPathChars()) >= 0;
}
