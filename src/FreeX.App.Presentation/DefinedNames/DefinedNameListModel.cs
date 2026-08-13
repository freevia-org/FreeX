namespace FreeX.App.Presentation.DefinedNames;

/// <summary>What a defined name resolves to, used to drive the Name Manager's "kind" column and icons.</summary>
public enum DefinedNameKind
{
    /// <summary>The name refers to a cell or cell range.</summary>
    Range,

    /// <summary>The name refers to a formula/constant expression rather than a plain range.</summary>
    Formula,

    /// <summary>The name has an error in its refers-to or value (e.g. #REF!).</summary>
    Error
}

/// <summary>How the Name Manager list is filtered.</summary>
public enum DefinedNameFilter
{
    /// <summary>Show every defined name.</summary>
    All,

    /// <summary>Show only workbook-scoped names.</summary>
    Workbook,

    /// <summary>Show only worksheet-scoped names.</summary>
    Worksheet,

    /// <summary>Show only names whose refers-to or value contains an error.</summary>
    Errors,

    /// <summary>Show only names free of errors.</summary>
    NoErrors
}

/// <summary>
/// The stable identity of a defined name: its case-insensitive name text plus its real scope identity.
/// </summary>
public readonly record struct DefinedNameIdentity(string Name, DefinedNameScope Scope);

/// <summary>
/// A projected row for the Name Manager list: the name, its real scope, the refers-to text, an optional
/// value preview, a comment, and the derived <see cref="DefinedNameKind"/>. Display labels never determine
/// identity: a worksheet named <c>Workbook</c> remains distinct from <see cref="DefinedNameScope.Workbook"/>.
/// </summary>
public sealed record DefinedNameRow(
    string Name,
    DefinedNameScope Scope,
    string RefersTo,
    string Value,
    string Comment,
    DefinedNameKind Kind)
{
    /// <summary>The renderer-facing scope label.</summary>
    public string ScopeLabel => Scope.Label;

    /// <summary>The stable name/scope key used by validation and commands.</summary>
    public DefinedNameIdentity Identity => new(Name, Scope);

    /// <summary>True when this row is workbook-scoped according to its real scope identity.</summary>
    public bool IsWorkbookScoped => Scope.IsWorkbook;

    /// <summary>True when the refers-to or value carries a formula error.</summary>
    public bool HasError => Kind == DefinedNameKind.Error;
}

/// <summary>The column the Name Manager list is sorted by.</summary>
public enum DefinedNameSortColumn
{
    /// <summary>Sort by the defined name.</summary>
    Name,

    /// <summary>Sort by the scope label.</summary>
    Scope,

    /// <summary>Sort by the refers-to text.</summary>
    RefersTo
}

/// <summary>
/// Portable projection/sort/filter logic for the Name Manager list. It mirrors the desktop hosts' planner:
/// the same error tokens drive the error filter, workbook vs worksheet is decided by real scope identity, and the
/// kind is derived from the refers-to/value text. Pure data in, pure data out.
/// </summary>
public static class DefinedNameListProjector
{
    private static readonly string[] FormulaErrorTokens =
    [
        "#REF!",
        "#NAME?",
        "#VALUE!",
        "#DIV/0!",
        "#N/A",
        "#NUM!",
        "#NULL!"
    ];

    /// <summary>
    /// Build a row from its parts, deriving the <see cref="DefinedNameKind"/>. A refers-to or value containing
    /// a formula error yields <see cref="DefinedNameKind.Error"/>; a refers-to that is not a plain A1/A1:A1
    /// range yields <see cref="DefinedNameKind.Formula"/>; otherwise <see cref="DefinedNameKind.Range"/>.
    /// </summary>
    public static DefinedNameRow CreateRow(
        string name,
        DefinedNameScope scope,
        string refersTo,
        string value = "",
        string comment = "")
    {
        var kind = DeriveKind(refersTo, value);
        return new DefinedNameRow(name, scope, refersTo, value, comment, kind);
    }

    /// <summary>Apply a <see cref="DefinedNameFilter"/> to a set of rows, preserving order.</summary>
    public static IReadOnlyList<DefinedNameRow> Filter(
        IEnumerable<DefinedNameRow> rows,
        DefinedNameFilter filter) =>
        filter switch
        {
            DefinedNameFilter.Workbook => rows.Where(r => r.IsWorkbookScoped).ToList(),
            DefinedNameFilter.Worksheet => rows.Where(r => !r.IsWorkbookScoped).ToList(),
            DefinedNameFilter.Errors => rows.Where(r => r.HasError).ToList(),
            DefinedNameFilter.NoErrors => rows.Where(r => !r.HasError).ToList(),
            _ => rows.ToList()
        };

    /// <summary>Sort rows by a column, case-insensitively, optionally descending.</summary>
    public static IReadOnlyList<DefinedNameRow> Sort(
        IEnumerable<DefinedNameRow> rows,
        DefinedNameSortColumn column = DefinedNameSortColumn.Name,
        bool descending = false)
    {
        Func<DefinedNameRow, string> key = column switch
        {
            DefinedNameSortColumn.Scope => r => r.ScopeLabel,
            DefinedNameSortColumn.RefersTo => r => r.RefersTo,
            _ => r => r.Name
        };

        var ordered = descending
            ? rows.OrderByDescending(key, StringComparer.OrdinalIgnoreCase)
            : rows.OrderBy(key, StringComparer.OrdinalIgnoreCase);
        return ordered.ToList();
    }

    /// <summary>Filter then sort in one call — the typical Name Manager projection.</summary>
    public static IReadOnlyList<DefinedNameRow> Project(
        IEnumerable<DefinedNameRow> rows,
        DefinedNameFilter filter = DefinedNameFilter.All,
        DefinedNameSortColumn sortColumn = DefinedNameSortColumn.Name,
        bool descending = false) =>
        Sort(Filter(rows, filter), sortColumn, descending);

    /// <summary>True when <paramref name="text"/> contains any of the formula error tokens (case-insensitive).</summary>
    public static bool ContainsFormulaError(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var token in FormulaErrorTokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static DefinedNameKind DeriveKind(string refersTo, string value)
    {
        if (ContainsFormulaError(refersTo) || ContainsFormulaError(value))
            return DefinedNameKind.Error;

        return IsPlainRange(refersTo) ? DefinedNameKind.Range : DefinedNameKind.Formula;
    }

    private static bool IsPlainRange(string? refersTo)
    {
        if (string.IsNullOrWhiteSpace(refersTo))
            return false;

        var text = refersTo.Trim();
        if (text.StartsWith('='))
            text = text[1..].Trim();

        // Strip an optional sheet qualifier ("Sheet1!", "'My Sheet'!") before the cell/range body.
        var bang = text.LastIndexOf('!');
        if (bang >= 0)
            text = text[(bang + 1)..];

        // Reject anything carrying operators/function syntax — those are formulas, not plain ranges.
        if (text.AsSpan().IndexOfAny("+-*/()^&,% ") >= 0)
            return false;

        var parts = text.Split(':');
        return parts.Length switch
        {
            1 => IsCellToken(parts[0]),
            2 => IsCellToken(parts[0]) && IsCellToken(parts[1]),
            _ => false
        };
    }

    private static bool IsCellToken(string token)
    {
        token = token.Replace("$", "", StringComparison.Ordinal);
        if (token.Length == 0)
            return false;

        var i = 0;
        while (i < token.Length && char.IsLetter(token[i]))
            i++;

        var hasColumn = i > 0;
        var hasRow = i < token.Length;
        for (var r = i; r < token.Length; r++)
        {
            if (!char.IsDigit(token[r]))
                return false;
        }

        return hasColumn || hasRow;
    }
}
