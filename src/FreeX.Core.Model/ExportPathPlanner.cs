using Free.Shared.IO;

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
        string.Equals(FilePathPolicy.GetExtensionOrEmpty(path), ".xps", StringComparison.OrdinalIgnoreCase)
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
        if (!forceMatchingExtension && FilePathPolicy.TryGetExtension(path, out _))
            return path;

        return FilePathPolicy.TryChangeExtension(path, format == ExportFileFormat.Xps ? ".xps" : ".pdf", out var normalized)
            ? normalized
            : path;
    }

    private static bool PathsEqual(string left, string right) =>
        FilePathPolicy.AreEquivalent(left, right);
}
