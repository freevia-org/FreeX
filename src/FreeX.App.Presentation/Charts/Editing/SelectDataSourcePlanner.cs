using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

// ---- Input / result shapes -----------------------------------------------------------------------

/// <summary>A single inferred series entry shown in the Legend Entries list.</summary>
public readonly record struct SelectDataSourceSeriesEntry(string Name, string ValuesRangeText);

/// <summary>A single inferred axis-label entry shown in the Horizontal (Category) Axis Labels list.</summary>
public readonly record struct SelectDataSourceCategoryEntry(string Label);

/// <summary>
/// All inferred preview entries produced from parsing a chart data range + category flag.
/// Mirrors <c>SelectDataSourcePreview</c> in the WPF shell's Planning.cs.
/// </summary>
public readonly record struct SelectDataSourcePreview(
    IReadOnlyList<SelectDataSourceSeriesEntry> Series,
    IReadOnlyList<SelectDataSourceCategoryEntry> Categories,
    string CategoryRangeText);

/// <summary>The result the dialog hands back to the shell when the user confirms.</summary>
public readonly record struct SelectDataSourceResult(
    string SourceRangeText,
    bool FirstColumnIsCategories,
    bool SwitchRowColumn = false);

// ---- Planner -------------------------------------------------------------------------------------

/// <summary>
/// Portable (no UI) planner for the "Select Data Source" chart-editing dialog.  Mirrors the inference
/// and normalisation logic in the WPF shell's <c>SelectDataSourceDialog.Planning.cs</c> so the same
/// series/category preview behaviour is available to every shell and to unit tests without a running UI.
/// <para>
/// The planner is deliberately stateless: every method receives its inputs as parameters and returns
/// plain records or primitives.  Each shell dialog drives it as its view-model helper; unit tests can
/// call it directly.
/// </para>
/// </summary>
public static class SelectDataSourcePlanner
{
    // ---- Public API ------------------------------------------------------------------------------

    /// <summary>
    /// Builds preview entries (series list + category list + category range text) by parsing
    /// <paramref name="sourceRangeText"/> using the same algorithm as the WPF shell.
    /// Returns empty lists when the range text is blank or unparseable.
    /// </summary>
    public static SelectDataSourcePreview InferPreviewEntries(
        string sourceRangeText,
        bool firstColumnIsCategories)
    {
        if (string.IsNullOrWhiteSpace(sourceRangeText))
            return new SelectDataSourcePreview([], [], string.Empty);

        var parsed = TryParseRangeReference(sourceRangeText);
        if (parsed is null)
        {
            // Unparseable: show one fallback series, one fallback category.
            return new SelectDataSourcePreview(
                [new SelectDataSourceSeriesEntry(FormatSeriesName(1), sourceRangeText.Trim())],
                [new SelectDataSourceCategoryEntry(CategoryLabelsFallback)],
                string.Empty);
        }

        var range = parsed.Value;
        var firstSeriesColumn = firstColumnIsCategories && range.EndCol > range.StartCol
            ? range.StartCol + 1
            : range.StartCol;
        var firstDataRow = FirstDataRow(range, firstColumnIsCategories);

        var series = BuildSeriesEntries(sourceRangeText, range, firstSeriesColumn, firstDataRow);
        var categories = BuildCategoryEntries(range, firstDataRow);
        var categoryRange = firstColumnIsCategories
            ? FormatRangeReference(range.SheetName, range.StartCol, firstDataRow, range.StartCol, range.EndRow)
            : string.Empty;

        return new SelectDataSourcePreview(series, categories, categoryRange);
    }

    /// <summary>
    /// Normalises <paramref name="sourceRangeText"/> and creates a <see cref="SelectDataSourceResult"/>
    /// from the dialog's confirmed values.
    /// </summary>
    public static SelectDataSourceResult CreateResult(
        string sourceRangeText,
        bool firstColumnIsCategories,
        bool switchRowColumn = false) =>
        new(sourceRangeText.Trim(), firstColumnIsCategories, switchRowColumn);

    /// <summary>
    /// Formats a column + row cell address as <c>$A$1</c>.
    /// </summary>
    public static string FormatCellRef(uint col, uint row) =>
        "$" + CellAddress.NumberToColumnName(col) + "$" + row;

    /// <summary>
    /// Formats a range reference as <c>SheetName!$A$1:$D$4</c> (omits the sheet prefix when
    /// <paramref name="sheetName"/> is null/empty).
    /// </summary>
    public static string FormatRangeReference(
        string? sheetName,
        uint startCol,
        uint startRow,
        uint endCol,
        uint endRow)
    {
        var prefix = string.IsNullOrWhiteSpace(sheetName) ? string.Empty : $"{sheetName}!";
        var start = FormatCellRef(startCol, startRow);
        var end = FormatCellRef(endCol, endRow);
        return $"{prefix}{start}:{end}";
    }

    // ---- Constants exposed for tests and the view ------------------------------------------------

    /// <summary>Fallback label used when categories cannot be inferred from the range.</summary>
    public const string CategoryLabelsFallback = "Category labels";

    /// <summary>Formats a 1-based series display name: "Series {n}".</summary>
    public static string FormatSeriesName(int oneBasedIndex) => $"Series {oneBasedIndex}";

    /// <summary>Formats a 1-based category display name: "Category {n}".</summary>
    public static string FormatCategoryName(int oneBasedIndex) => $"Category {oneBasedIndex}";

    /// <summary>Formats a series list-item line: "{name}    {range}".</summary>
    public static string FormatSeriesListItem(string name, string valuesRange) =>
        $"{name}    {valuesRange}";

    /// <summary>Formats a new (user-added, not yet range-bound) series list item.</summary>
    public static string FormatNewSeriesItem(int oneBasedIndex) =>
        $"Series {oneBasedIndex}    <select range>";

    // ---- Parsing internals -----------------------------------------------------------------------

    private readonly record struct ParsedRange(
        string? SheetName,
        uint StartCol,
        uint StartRow,
        uint EndCol,
        uint EndRow);

    private static ParsedRange? TryParseRangeReference(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return null;

        string? sheetName = null;
        var bangIndex = trimmed.LastIndexOf('!');
        if (bangIndex >= 0)
        {
            sheetName = trimmed[..bangIndex].Trim('\'');
            trimmed = trimmed[(bangIndex + 1)..];
        }

        var parts = trimmed.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            parts = [parts[0], parts[0]];
        if (parts.Length != 2)
            return null;

        if (!TryParseCellRef(parts[0], out var startCol, out var startRow) ||
            !TryParseCellRef(parts[1], out var endCol, out var endRow))
            return null;

        return new ParsedRange(
            sheetName,
            Math.Min(startCol, endCol),
            Math.Min(startRow, endRow),
            Math.Max(startCol, endCol),
            Math.Max(startRow, endRow));
    }

    private static bool TryParseCellRef(string text, out uint col, out uint row)
    {
        var normalized = text.Replace("$", string.Empty, StringComparison.Ordinal).Trim();
        var letterCount = normalized.TakeWhile(char.IsLetter).Count();
        col = 0;
        row = 0;
        if (letterCount == 0 || letterCount == normalized.Length)
            return false;

        col = CellAddress.ColumnNameToNumber(normalized[..letterCount]);
        return col > 0 && uint.TryParse(normalized[letterCount..], out row) && row > 0;
    }

    private static uint FirstDataRow(ParsedRange range, bool firstColumnIsCategories) =>
        firstColumnIsCategories && range.EndRow > range.StartRow ? range.StartRow + 1 : range.StartRow;

    private static IReadOnlyList<SelectDataSourceSeriesEntry> BuildSeriesEntries(
        string sourceRangeText,
        ParsedRange range,
        uint firstSeriesColumn,
        uint firstDataRow)
    {
        var entries = new List<SelectDataSourceSeriesEntry>();
        for (var col = firstSeriesColumn; col <= range.EndCol; col++)
        {
            entries.Add(new SelectDataSourceSeriesEntry(
                FormatSeriesName(entries.Count + 1),
                FormatRangeReference(range.SheetName, col, firstDataRow, col, range.EndRow)));
        }

        if (entries.Count == 0)
            entries.Add(new SelectDataSourceSeriesEntry(FormatSeriesName(1), sourceRangeText.Trim()));

        return entries;
    }

    private static IReadOnlyList<SelectDataSourceCategoryEntry> BuildCategoryEntries(
        ParsedRange range,
        uint categoryStartRow)
    {
        var entries = new List<SelectDataSourceCategoryEntry>();
        for (var row = categoryStartRow; row <= range.EndRow; row++)
            entries.Add(new SelectDataSourceCategoryEntry(FormatCategoryName(entries.Count + 1)));

        if (entries.Count == 0)
            entries.Add(new SelectDataSourceCategoryEntry(CategoryLabelsFallback));

        return entries;
    }
}
