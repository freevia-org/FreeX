namespace FreeX.Core.Model;

public enum ExportFileFormat
{
    Pdf,
    Xps
}

public sealed record ExportPathPlan(
    string Path,
    ExportFileFormat Format,
    string? FallbackPath = null)
{
    public bool UsesXpsFallback => FallbackPath is not null;
    public string ActualPath => FallbackPath ?? Path;
}

public static class ExportPathPlanner
{
    public static ExportFileFormat InferFormat(string path) =>
        string.Equals(TryGetExtension(path, out var extension) ? extension : "", ".xps", StringComparison.OrdinalIgnoreCase)
            ? ExportFileFormat.Xps
            : ExportFileFormat.Pdf;

    public static ExportPathPlan Plan(string path)
    {
        var format = InferFormat(path);
        return Plan(path, format, forceMatchingExtension: format == ExportFileFormat.Pdf);
    }

    public static ExportPathPlan Plan(string path, ExportFileFormat format) =>
        Plan(path, format, forceMatchingExtension: true);

    public static bool ShouldPromptForNormalizedOverwrite(
        string requestedPath,
        ExportPathPlan plan,
        Func<string, bool> pathExists)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(pathExists);

        return !PathsEqual(requestedPath, plan.Path) && pathExists(plan.Path);
    }

    public static string GetFallbackXpsPath(string requestedPath) =>
        Path.ChangeExtension(requestedPath, ".xps") ?? requestedPath;

    private static ExportPathPlan Plan(string path, ExportFileFormat format, bool forceMatchingExtension)
    {
        var normalizedPath = NormalizePath(path, format, forceMatchingExtension);
        return new ExportPathPlan(normalizedPath, format);
    }

    private static string NormalizePath(string path, ExportFileFormat format, bool forceMatchingExtension)
    {
        if (!forceMatchingExtension && TryGetExtension(path, out _))
            return path;

        return TryChangeExtension(path, format == ExportFileFormat.Xps ? ".xps" : ".pdf", out var normalized)
            ? normalized
            : path;
    }

    private static bool TryGetExtension(string path, out string extension)
    {
        if (HasInvalidPathChars(path))
        {
            extension = "";
            return false;
        }

        try
        {
            extension = Path.GetExtension(path) ?? "";
            return !string.IsNullOrEmpty(extension);
        }
        catch (ArgumentException)
        {
            extension = "";
            return false;
        }
        catch (NotSupportedException)
        {
            extension = "";
            return false;
        }
        catch (PathTooLongException)
        {
            extension = "";
            return false;
        }
    }

    private static bool TryChangeExtension(string path, string extension, out string normalizedPath)
    {
        if (HasInvalidPathChars(path))
        {
            normalizedPath = path;
            return false;
        }

        try
        {
            normalizedPath = Path.ChangeExtension(path, extension) ?? path;
            return true;
        }
        catch (ArgumentException)
        {
            normalizedPath = path;
            return false;
        }
        catch (NotSupportedException)
        {
            normalizedPath = path;
            return false;
        }
        catch (PathTooLongException)
        {
            normalizedPath = path;
            return false;
        }
    }

    private static bool HasInvalidPathChars(string path) =>
        path.IndexOf('\0') >= 0;

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            left = Path.GetFullPath(left);
            right = Path.GetFullPath(right);
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (PathTooLongException)
        {
        }

        return string.Equals(left, right, PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
