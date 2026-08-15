using Free.Shared.Shell;
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
    bool SwitchRowColumn = false,
    IReadOnlyList<int>? PendingSeriesRemovals = null,
    ChartBlankDisplayMode BlankDisplayMode = ChartBlankDisplayMode.Gap,
    bool ShowDataInHiddenRowsAndColumns = false);

/// <summary>A shell request to pick a replacement chart data range.</summary>
public readonly record struct SelectDataSourceRangeSelectionRequest(string CurrentText, bool CollapseDialog = true);

public enum SelectDataSourceDialogFieldId
{
    ChartDataRange,
    SwitchRowColumn,
    SeriesList,
    AxisLabelsList,
    FirstColumnCategories,
}

public enum SelectDataSourceDialogActionId
{
    AddSeries,
    EditSeries,
    RemoveSeries,
    EditAxisLabels,
    HiddenEmptyCells,
}

public sealed record SelectDataSourceDialogFieldDescriptor : DialogFieldPlan<SelectDataSourceDialogFieldId>
{
    public SelectDataSourceDialogFieldDescriptor(
        SelectDataSourceDialogFieldId Id,
        string LabelResourceKey,
        string AutomationId,
        string? AutomationNameResourceKey = null,
        string? HelpResourceKey = null)
        : base(
            Id,
            ControlKindFor(Id),
            LabelResourceKey,
            AutomationNameResourceKey,
            AutomationId,
            HelpResourceKey)
    {
    }

    public string LabelResourceKey => Label;

    public string? AutomationNameResourceKey => AccessibleName;

    public string? HelpResourceKey => HelpText;

    public void Deconstruct(
        out SelectDataSourceDialogFieldId Id,
        out string LabelResourceKey,
        out string AutomationId,
        out string? AutomationNameResourceKey,
        out string? HelpResourceKey)
    {
        Id = this.Id;
        LabelResourceKey = this.LabelResourceKey;
        AutomationId = this.AutomationId;
        AutomationNameResourceKey = this.AutomationNameResourceKey;
        HelpResourceKey = this.HelpResourceKey;
    }

    private static DialogControlKind ControlKindFor(SelectDataSourceDialogFieldId id) => id switch
    {
        SelectDataSourceDialogFieldId.ChartDataRange => DialogControlKind.Text,
        SelectDataSourceDialogFieldId.SwitchRowColumn => DialogControlKind.Toggle,
        SelectDataSourceDialogFieldId.SeriesList => DialogControlKind.List,
        SelectDataSourceDialogFieldId.AxisLabelsList => DialogControlKind.List,
        SelectDataSourceDialogFieldId.FirstColumnCategories => DialogControlKind.Toggle,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, null),
    };
}

public sealed record SelectDataSourceDialogActionDescriptor : DialogSurfaceActionPlan<SelectDataSourceDialogActionId>
{
    public SelectDataSourceDialogActionDescriptor(
        SelectDataSourceDialogActionId Id,
        string LabelResourceKey,
        string AutomationId)
        : base(Id, LabelResourceKey, null, AutomationId)
    {
    }

    public string LabelResourceKey => Label;

    public void Deconstruct(
        out SelectDataSourceDialogActionId Id,
        out string LabelResourceKey,
        out string AutomationId)
    {
        Id = this.Id;
        LabelResourceKey = this.LabelResourceKey;
        AutomationId = this.AutomationId;
    }
}

public sealed record SelectDataSourceListPanelDescriptor(
    SelectDataSourceDialogFieldDescriptor ListField,
    string TitleResourceKey,
    IReadOnlyList<SelectDataSourceDialogActionDescriptor> Actions);

// ---- Planner -------------------------------------------------------------------------------------

/// <summary>
/// Portable (no UI) planner for the "Select Data Source" chart-editing dialog. Keeps the inference
/// and normalisation logic shared so the same series/category preview behaviour is available to every
/// shell and to unit tests without a running UI.
/// <para>
/// The planner is deliberately stateless: every method receives its inputs as parameters and returns
/// plain records or primitives.  Each shell dialog drives it as its view-model helper; unit tests can
/// call it directly.
/// </para>
/// </summary>
public static class SelectDataSourcePlanner
{
    public const string DialogTitleResourceKey = "SelectDataSource_Title";
    public const string DialogAutomationId = "SelectChartDataDialog";
    public const string SelectRangeAutomationNameResourceKey = "SelectDataSource_SelectChartDataRangeAutomationName";
    public const string HiddenEmptyCellsTitleResourceKey = "SelectDataSource_HiddenEmptyCellsTitle";
    public const string HiddenEmptyCellsMessageResourceKey = "SelectDataSource_HiddenEmptyCellsMessage";
    public const string InvalidRangeMessageResourceKey = "SelectDataSource_InvalidRangeMessage";

    private static readonly SelectDataSourceDialogFieldDescriptor ChartDataRangeField = new(
        SelectDataSourceDialogFieldId.ChartDataRange,
        "SelectDataSource_ChartDataRangeLabel",
        "SelectChartDataRangeBox",
        "SelectDataSource_ChartDataRangeAutomationName");

    private static readonly SelectDataSourceDialogFieldDescriptor SwitchRowColumnField = new(
        SelectDataSourceDialogFieldId.SwitchRowColumn,
        "SelectDataSource_SwitchRowColumn",
        "SelectChartDataSwitchRowColumnCheck");

    private static readonly SelectDataSourceDialogFieldDescriptor FirstColumnCategoriesField = new(
        SelectDataSourceDialogFieldId.FirstColumnCategories,
        "SelectDataSource_FirstColumnCategories",
        "SelectChartDataCategoriesCheck");

    private static readonly SelectDataSourceDialogActionDescriptor AddSeriesAction = new(
        SelectDataSourceDialogActionId.AddSeries,
        "SelectDataSource_AddSeriesButton",
        "SelectChartDataAddSeriesButton");

    private static readonly SelectDataSourceDialogActionDescriptor EditSeriesAction = new(
        SelectDataSourceDialogActionId.EditSeries,
        "SelectDataSource_EditSeriesButton",
        "SelectChartDataEditSeriesButton");

    private static readonly SelectDataSourceDialogActionDescriptor RemoveSeriesAction = new(
        SelectDataSourceDialogActionId.RemoveSeries,
        "SelectDataSource_RemoveSeriesButton",
        "SelectChartDataRemoveSeriesButton");

    private static readonly SelectDataSourceDialogActionDescriptor EditAxisLabelsAction = new(
        SelectDataSourceDialogActionId.EditAxisLabels,
        "SelectDataSource_EditAxisLabelsButton",
        "SelectChartDataEditAxisLabelsButton");

    private static readonly SelectDataSourceDialogActionDescriptor HiddenEmptyCellsAction = new(
        SelectDataSourceDialogActionId.HiddenEmptyCells,
        "SelectDataSource_HiddenEmptyCellsButton",
        "SelectChartDataHiddenEmptyButton");

    private static readonly SelectDataSourceListPanelDescriptor SeriesPanel = new(
        new SelectDataSourceDialogFieldDescriptor(
            SelectDataSourceDialogFieldId.SeriesList,
            "SelectDataSource_SeriesPanelTitle",
            "SelectChartDataSeriesList",
            "SelectDataSource_SeriesListAutomationName",
            "SelectDataSource_SeriesListHelpText"),
        "SelectDataSource_SeriesPanelTitle",
        [AddSeriesAction, EditSeriesAction, RemoveSeriesAction]);

    private static readonly SelectDataSourceListPanelDescriptor AxisLabelsPanel = new(
        new SelectDataSourceDialogFieldDescriptor(
            SelectDataSourceDialogFieldId.AxisLabelsList,
            "SelectDataSource_AxisLabelsPanelTitle",
            "SelectChartDataAxisLabelsList",
            "SelectDataSource_AxisLabelsListAutomationName",
            "SelectDataSource_AxisLabelsListHelpText"),
        "SelectDataSource_AxisLabelsPanelTitle",
        [EditAxisLabelsAction]);

    // ---- Public API ------------------------------------------------------------------------------

    public static SelectDataSourceDialogFieldDescriptor GetChartDataRangeField() => ChartDataRangeField;

    public static SelectDataSourceDialogFieldDescriptor GetSwitchRowColumnField() => SwitchRowColumnField;

    public static SelectDataSourceDialogFieldDescriptor GetFirstColumnCategoriesField() => FirstColumnCategoriesField;

    public static SelectDataSourceListPanelDescriptor GetSeriesPanel() => SeriesPanel;

    public static SelectDataSourceListPanelDescriptor GetAxisLabelsPanel() => AxisLabelsPanel;

    public static SelectDataSourceDialogActionDescriptor GetHiddenEmptyCellsAction() => HiddenEmptyCellsAction;

    /// <summary>
    /// Builds preview entries (series list + category list + category range text) by parsing
    /// <paramref name="sourceRangeText"/> using the same algorithm as the WPF shell.
    /// Returns empty lists when the range text is blank or unparseable.
    /// </summary>
    public static SelectDataSourcePreview InferPreviewEntries(
        string sourceRangeText,
        bool firstColumnIsCategories,
        bool switchRowColumn = false) =>
        InferPreviewEntries(
            sourceRangeText,
            firstColumnIsCategories,
            FormatSeriesName,
            FormatCategoryName,
            CategoryLabelsFallback,
            switchRowColumn);

    /// <summary>
    /// Builds preview entries while letting a shell supply localized display text for generated labels.
    /// <paramref name="switchRowColumn"/> mirrors the dialog's "Switch Row/Column" checkbox
    /// (<c>chart.SeriesInRows</c>): when set, every row/column role below is transposed -- series are
    /// derived from ROWS (one series per row, spanning columns) and categories from COLUMNS, exactly
    /// swapping the roles the non-transposed branch gives them. This lets toggling the checkbox refresh
    /// the dialog's own Series/Axis-Labels preview lists (R92-app-chart-data-edit-5-2) instead of only
    /// taking effect once OK applies <c>ChangeChartSourceCommand</c>'s <c>SeriesInRows</c>.
    /// </summary>
    public static SelectDataSourcePreview InferPreviewEntries(
        string sourceRangeText,
        bool firstColumnIsCategories,
        Func<int, string> formatSeriesName,
        Func<int, string> formatCategoryName,
        string categoryLabelsFallback,
        bool switchRowColumn = false)
    {
        ArgumentNullException.ThrowIfNull(formatSeriesName);
        ArgumentNullException.ThrowIfNull(formatCategoryName);

        if (string.IsNullOrWhiteSpace(sourceRangeText))
            return new SelectDataSourcePreview([], [], string.Empty);

        var parsed = TryParseRangeReference(sourceRangeText);
        if (parsed is null)
        {
            // Unparseable: show one fallback series, one fallback category.
            return new SelectDataSourcePreview(
                [new SelectDataSourceSeriesEntry(formatSeriesName(1), sourceRangeText.Trim())],
                [new SelectDataSourceCategoryEntry(categoryLabelsFallback)],
                string.Empty);
        }

        var range = parsed.Value;
        if (switchRowColumn)
        {
            // Transposed: series iterate ROWS (skipping the category row when firstColumnIsCategories),
            // categories iterate COLUMNS starting where each series' data begins -- the literal
            // row<->col swap of the branch below.
            var firstSeriesRow = firstColumnIsCategories && range.EndRow > range.StartRow
                ? range.StartRow + 1
                : range.StartRow;
            var firstDataCol = firstColumnIsCategories && range.EndCol > range.StartCol
                ? range.StartCol + 1
                : range.StartCol;

            var transposedSeries = BuildSeriesEntriesTransposed(sourceRangeText, range, firstSeriesRow, firstDataCol, formatSeriesName);
            var transposedCategories = BuildCategoryEntriesTransposed(range, firstDataCol, formatCategoryName, categoryLabelsFallback);
            var transposedCategoryRange = firstColumnIsCategories
                ? FormatRangeReference(range.SheetName, firstDataCol, range.StartRow, range.EndCol, range.StartRow)
                : string.Empty;

            return new SelectDataSourcePreview(transposedSeries, transposedCategories, transposedCategoryRange);
        }

        var firstSeriesColumn = firstColumnIsCategories && range.EndCol > range.StartCol
            ? range.StartCol + 1
            : range.StartCol;
        var firstDataRow = FirstDataRow(range, firstColumnIsCategories);

        var series = BuildSeriesEntries(sourceRangeText, range, firstSeriesColumn, firstDataRow, formatSeriesName);
        var categories = BuildCategoryEntries(range, firstDataRow, formatCategoryName, categoryLabelsFallback);
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
    /// Normalises the current data range and requests collapsed range-picking chrome.
    /// </summary>
    public static SelectDataSourceRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        new(currentText.Trim(), CollapseDialog: true);

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

    /// <summary>
    /// Parses a single rectangular range reference, or returns <c>null</c> when the text is not one
    /// (the caller then shows its "cannot parse" fallback preview).
    /// <para>
    /// R137-app-select-data-source-multiarea: a discontiguous union such as
    /// <c>Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5</c> is NOT a single rectangle and must take the fallback
    /// path. Without the explicit union check below, <see cref="string.LastIndexOf(char)"/> on '!'
    /// lands on the LAST area's sheet separator, so the tail (<c>$C$1:$C$5</c>) splits cleanly on ':'
    /// and the parse "succeeds" against only the final area -- silently dropping every earlier one
    /// and leaving a garbage <c>SheetName</c> ("Sheet1!$A$1:$A$5,Sheet1") that then gets re-emitted
    /// into every previewed series range. The prefix-less spelling (<c>$A$1:$A$5,$C$1:$C$5</c>)
    /// already fell out to <c>null</c> via the 3-way ':' split, so this only makes the sheet-prefixed
    /// spelling behave the same way. It also matches what the dialog's own OK-time validation does:
    /// <c>ChartInputParser.TryParseDataRange</c> rejects every union spelling, so a preview built
    /// from one area was describing a range the user could never actually apply.
    /// </para>
    /// </summary>
    private static ParsedRange? TryParseRangeReference(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return null;

        // Quote-aware split, shared with the range-text codec, so a comma inside a quoted sheet name
        // ('Budget, Q1'!$A$1:$A$5) is not mistaken for a union separator.
        if (WorkbookRangeTextCodec.SplitReferences(trimmed).Count > 1)
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
        uint firstDataRow,
        Func<int, string> formatSeriesName)
    {
        var entries = new List<SelectDataSourceSeriesEntry>();
        for (var col = firstSeriesColumn; col <= range.EndCol; col++)
        {
            entries.Add(new SelectDataSourceSeriesEntry(
                formatSeriesName(entries.Count + 1),
                FormatRangeReference(range.SheetName, col, firstDataRow, col, range.EndRow)));
        }

        if (entries.Count == 0)
            entries.Add(new SelectDataSourceSeriesEntry(formatSeriesName(1), sourceRangeText.Trim()));

        return entries;
    }

    private static IReadOnlyList<SelectDataSourceCategoryEntry> BuildCategoryEntries(
        ParsedRange range,
        uint categoryStartRow,
        Func<int, string> formatCategoryName,
        string categoryLabelsFallback)
    {
        var entries = new List<SelectDataSourceCategoryEntry>();
        for (var row = categoryStartRow; row <= range.EndRow; row++)
            entries.Add(new SelectDataSourceCategoryEntry(formatCategoryName(entries.Count + 1)));

        if (entries.Count == 0)
            entries.Add(new SelectDataSourceCategoryEntry(categoryLabelsFallback));

        return entries;
    }

    // ---- Transposed (Switch Row/Column) variants --------------------------------------------------
    // Literal row<->col swap of BuildSeriesEntries/BuildCategoryEntries above: a "series" is now one
    // ROW (spanning the data columns), and "categories" are counted per data COLUMN instead of per
    // data row. See InferPreviewEntries' switchRowColumn branch for how the skip offsets are derived.

    private static IReadOnlyList<SelectDataSourceSeriesEntry> BuildSeriesEntriesTransposed(
        string sourceRangeText,
        ParsedRange range,
        uint firstSeriesRow,
        uint firstDataCol,
        Func<int, string> formatSeriesName)
    {
        var entries = new List<SelectDataSourceSeriesEntry>();
        for (var row = firstSeriesRow; row <= range.EndRow; row++)
        {
            entries.Add(new SelectDataSourceSeriesEntry(
                formatSeriesName(entries.Count + 1),
                FormatRangeReference(range.SheetName, firstDataCol, row, range.EndCol, row)));
        }

        if (entries.Count == 0)
            entries.Add(new SelectDataSourceSeriesEntry(formatSeriesName(1), sourceRangeText.Trim()));

        return entries;
    }

    private static IReadOnlyList<SelectDataSourceCategoryEntry> BuildCategoryEntriesTransposed(
        ParsedRange range,
        uint categoryStartCol,
        Func<int, string> formatCategoryName,
        string categoryLabelsFallback)
    {
        var entries = new List<SelectDataSourceCategoryEntry>();
        for (var col = categoryStartCol; col <= range.EndCol; col++)
            entries.Add(new SelectDataSourceCategoryEntry(formatCategoryName(entries.Count + 1)));

        if (entries.Count == 0)
            entries.Add(new SelectDataSourceCategoryEntry(categoryLabelsFallback));

        return entries;
    }
}
