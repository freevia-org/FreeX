using FreeX.Core.Formula;
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
        if (sheet is null)
            return false;

        if (sheet.Hyperlinks.TryGetValue(address, out var target) && !string.IsNullOrWhiteSpace(target))
        {
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

        // No explicit Insert-Hyperlink object on this cell. Excel also makes a cell whose sole
        // hyperlink mechanism is a literal =HYPERLINK("target", ...) formula click-navigable, so
        // fall back to inspecting the formula for a direct, literal-string HYPERLINK() call.
        if (TryGetHyperlinkFormulaTarget(sheet, address, out var formulaTarget))
        {
            var normalizedFormulaTarget = formulaTarget.Trim();

            // Excel's documented convention for an intra-workbook HYPERLINK() target is a leading
            // '#' followed by the worksheet reference, e.g. "#Sheet2!A1".
            if (normalizedFormulaTarget.StartsWith('#'))
            {
                plan = new HyperlinkNavigationPlan(
                    HyperlinkNavigationKind.WorksheetCell,
                    normalizedFormulaTarget[1..],
                    null);
                return true;
            }

            if (TryResolveLocalFileTarget(normalizedFormulaTarget, currentWorkbookPath, out var localPath))
            {
                plan = new HyperlinkNavigationPlan(
                    HyperlinkNavigationKind.LocalFile,
                    normalizedFormulaTarget,
                    null,
                    localPath);
                return true;
            }

            plan = new HyperlinkNavigationPlan(HyperlinkNavigationKind.External, normalizedFormulaTarget, null);
            return true;
        }

        return false;
    }

    /// <summary>
    /// If the cell at <paramref name="address"/> holds a formula whose top-level call is
    /// HYPERLINK(link_location, ...), returns the resolved link_location text: a literal string
    /// argument is used as-is, and any other expression (a cell reference, concatenation, nested
    /// function call, etc.) is evaluated against the sheet so the cell is click-navigable exactly
    /// like Excel, which does not require link_location to be a literal.
    /// </summary>
    private static bool TryGetHyperlinkFormulaTarget(Sheet sheet, CellAddress address, out string target)
    {
        target = "";
        var cell = sheet.GetCell(address);
        if (cell?.FormulaText is not { } formulaText)
            return false;

        FormulaNode ast;
        try
        {
            ast = FormulaEvaluator.ParseFormula(formulaText);
        }
        catch (FormulaParseException)
        {
            return false;
        }

        if (ast is not FunctionCallNode { Arguments.Count: > 0 } call ||
            !string.Equals(call.FunctionName, "HYPERLINK", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (call.Arguments[0] is StringNode { Value: { Length: > 0 } linkText })
        {
            target = linkText;
            return true;
        }

        var value = new FormulaEvaluator().Evaluate(call.Arguments[0], sheet, currentCell: address);
        if (!TryScalarToHyperlinkText(value, out var computedTarget))
            return false;

        target = computedTarget;
        return true;
    }

    /// <summary>
    /// Converts a HYPERLINK() link_location's computed scalar result to display/navigation text,
    /// matching Excel's own coercion (numbers/dates use their plain numeric text, booleans use
    /// TRUE/FALSE). Errors, blanks, and ranges are not valid link locations and are left unresolved.
    /// </summary>
    private static bool TryScalarToHyperlinkText(ScalarValue value, out string text)
    {
        switch (value)
        {
            case TextValue { Value.Length: > 0 } t:
                text = t.Value;
                return true;
            case NumberValue n:
                text = n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            case DateTimeValue d:
                text = d.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            case BoolValue b:
                text = b.Value ? "TRUE" : "FALSE";
                return true;
            default:
                text = "";
                return false;
        }
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
            path.Length > 32_767 ||
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
