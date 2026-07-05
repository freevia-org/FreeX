using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum HyperlinkNavigationKind
{
    External,
    LocalFile,
    WorksheetCell
}

public sealed record HyperlinkNavigationPlan(
    HyperlinkNavigationKind Kind,
    string Target,
    CellAddress? Address,
    string? LocalPath = null);

public static class HyperlinkNavigationPlanner
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "mailto", "ftp"
    };

    public static bool IsAllowedScheme(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return AllowedSchemes.Contains(uri.Scheme);
    }

    public static bool TryCreatePlan(Sheet? sheet, CellAddress address, out HyperlinkNavigationPlan? plan) =>
        TryCreatePlan(sheet, address, currentWorkbookPath: null, out plan);

    public static bool TryCreatePlan(
        Sheet? sheet,
        CellAddress address,
        string? currentWorkbookPath,
        out HyperlinkNavigationPlan? plan)
    {
        plan = null;
        if (sheet is null ||
            !sheet.Hyperlinks.TryGetValue(address, out var target) ||
            string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        sheet.HyperlinkMetadata.TryGetValue(address, out var metadata);
        var kind = metadata?.LinkType ?? HyperlinkTargetKind.ExistingFileOrWebPage;
        var normalizedTarget = target.Trim();

        if (kind == HyperlinkTargetKind.PlaceInThisDocument)
        {
            var reference = !string.IsNullOrWhiteSpace(metadata?.Bookmark)
                ? metadata.Bookmark.Trim()
                : normalizedTarget;
            plan = new HyperlinkNavigationPlan(HyperlinkNavigationKind.WorksheetCell, reference, null);
            return true;
        }

        if (kind is HyperlinkTargetKind.ExistingFileOrWebPage or HyperlinkTargetKind.CreateNewDocument &&
            TryResolveLocalFileTarget(normalizedTarget, currentWorkbookPath, out var localPath))
        {
            plan = new HyperlinkNavigationPlan(
                HyperlinkNavigationKind.LocalFile,
                normalizedTarget,
                null,
                localPath);
            return true;
        }

        plan = new HyperlinkNavigationPlan(HyperlinkNavigationKind.External, normalizedTarget, null);
        return true;
    }

    private static bool TryResolveLocalFileTarget(
        string target,
        string? currentWorkbookPath,
        out string localPath)
    {
        localPath = "";
        if (string.IsNullOrWhiteSpace(target) ||
            target.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        if (TryCreateExplicitUri(target, out var uri))
        {
            return uri.IsFile &&
                IsLocalFileUri(uri) &&
                TryNormalizeExplicitLocalPath(uri.LocalPath, out localPath);
        }

        if (IsLocalAbsolutePath(target))
            return TryNormalizeExplicitLocalPath(target, out localPath);

        return TryResolveWorkbookRelativePath(target, currentWorkbookPath, out localPath);
    }

    private static bool TryResolveWorkbookRelativePath(
        string target,
        string? currentWorkbookPath,
        out string localPath)
    {
        localPath = "";
        if (string.IsNullOrWhiteSpace(currentWorkbookPath) ||
            IsRootedPath(target) ||
            !TryResolveLocalFileTarget(currentWorkbookPath, null, out var workbookPath))
        {
            return false;
        }

        var workbookDirectory = Path.GetDirectoryName(workbookPath);
        if (string.IsNullOrWhiteSpace(workbookDirectory))
            return false;

        try
        {
            localPath = Path.GetFullPath(target, workbookDirectory);
            return !string.IsNullOrWhiteSpace(localPath) &&
                !localPath.Contains('\0', StringComparison.Ordinal);
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

    private static bool TryNormalizeExplicitLocalPath(string path, out string localPath)
    {
        localPath = "";
        if (string.IsNullOrWhiteSpace(path) ||
            path.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        if (IsUnixAbsolutePath(path))
        {
            localPath = path;
            return true;
        }

        try
        {
            localPath = Path.GetFullPath(path);
            return !string.IsNullOrWhiteSpace(localPath);
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

    private static bool IsLocalFileUri(Uri uri) =>
        string.IsNullOrEmpty(uri.Host) ||
        string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        uri.IsLoopback;

    private static bool IsLocalAbsolutePath(string path) =>
        IsUnixAbsolutePath(path) ||
        OperatingSystem.IsWindows() && IsWindowsDrivePath(path, path.Length >= 2 ? path[..1] : "");

    private static bool IsRootedPath(string path) =>
        IsUnixAbsolutePath(path) ||
        IsUncPath(path) ||
        IsWindowsRootRelativePath(path) ||
        IsWindowsDrivePath(path, path.Length >= 2 ? path[..1] : "");

    private static bool IsWindowsDrivePath(string candidate, string scheme) =>
        scheme.Length == 1 &&
        candidate.Length >= 3 &&
        candidate[1] == ':' &&
        candidate[2] is '\\' or '/' &&
        char.IsAsciiLetter(candidate[0]);

    private static bool IsUnixAbsolutePath(string path) =>
        path.Length >= 2 &&
        path[0] == '/' &&
        path[1] is not '/' and not '\\';

    private static bool IsUncPath(string path) =>
        path.Length >= 2 &&
        path[0] is '\\' or '/' &&
        path[1] is '\\' or '/';

    private static bool IsWindowsRootRelativePath(string path) =>
        path.Length >= 1 &&
        path[0] == '\\';
}
