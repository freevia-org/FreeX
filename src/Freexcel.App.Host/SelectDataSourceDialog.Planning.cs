using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record SelectDataSourceDialogResult(
    string SourceRangeText,
    bool FirstColumnIsCategories,
    bool SwitchRowColumn = false);

public sealed record SelectDataSourceRangeSelectionRequest(string CurrentText, bool CollapseDialog = true);

public sealed record SelectDataSourceSeriesPreview(string Name, string ValuesRangeText);

public sealed record SelectDataSourceCategoryPreview(string Label);

public sealed record SelectDataSourcePreview(
    IReadOnlyList<SelectDataSourceSeriesPreview> Series,
    IReadOnlyList<SelectDataSourceCategoryPreview> Categories,
    string CategoryRangeText);

public sealed partial class SelectDataSourceDialog
{
    private readonly record struct ParsedRangeReference(
        string? SheetName,
        uint StartCol,
        uint StartRow,
        uint EndCol,
        uint EndRow);

    public static SelectDataSourceDialogResult CreateResult(
        string sourceRangeText,
        bool firstColumnIsCategories,
        bool switchRowColumn = false) =>
        new(sourceRangeText.Trim(), firstColumnIsCategories, switchRowColumn);

    public static SelectDataSourcePreview InferPreviewEntries(string sourceRangeText, bool firstColumnIsCategories)
    {
        if (string.IsNullOrWhiteSpace(sourceRangeText))
            return new SelectDataSourcePreview([], [], "");

        var parsed = TryParseRangeReference(sourceRangeText);
        if (parsed is null)
        {
            return new SelectDataSourcePreview(
                [new SelectDataSourceSeriesPreview("Series 1", sourceRangeText.Trim())],
                [new SelectDataSourceCategoryPreview("Category labels")],
                "");
        }

        var range = parsed.Value;
        var firstSeriesColumn = firstColumnIsCategories && range.EndCol > range.StartCol
            ? range.StartCol + 1
            : range.StartCol;
        var firstDataRow = FirstCategoryDataRow(range, firstColumnIsCategories);
        var series = BuildSeriesPreviewEntries(sourceRangeText, range, firstSeriesColumn, firstDataRow);
        var categories = BuildCategoryPreviewEntries(range, firstDataRow);
        var categoryRange = firstColumnIsCategories
            ? FormatRangeReference(range.SheetName, range.StartCol, firstDataRow, range.StartCol, range.EndRow)
            : "";

        return new SelectDataSourcePreview(series, categories, categoryRange);
    }

    private static IReadOnlyList<SelectDataSourceSeriesPreview> BuildSeriesPreviewEntries(
        string sourceRangeText,
        ParsedRangeReference range,
        uint firstSeriesColumn,
        uint firstDataRow)
    {
        var series = new List<SelectDataSourceSeriesPreview>();
        for (var col = firstSeriesColumn; col <= range.EndCol; col++)
        {
            var seriesName = $"Series {series.Count + 1}";
            series.Add(new SelectDataSourceSeriesPreview(
                seriesName,
                FormatRangeReference(range.SheetName, col, firstDataRow, col, range.EndRow)));
        }

        if (series.Count == 0)
            series.Add(new SelectDataSourceSeriesPreview("Series 1", sourceRangeText.Trim()));

        return series;
    }

    private static IReadOnlyList<SelectDataSourceCategoryPreview> BuildCategoryPreviewEntries(
        ParsedRangeReference range,
        uint categoryStartRow)
    {
        var categories = new List<SelectDataSourceCategoryPreview>();
        for (var row = categoryStartRow; row <= range.EndRow; row++)
            categories.Add(new SelectDataSourceCategoryPreview($"Category {categories.Count + 1}"));

        if (categories.Count == 0)
            categories.Add(new SelectDataSourceCategoryPreview("Category labels"));

        return categories;
    }

    public static SelectDataSourceRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        new(currentText.Trim(), CollapseDialog: true);

    private static ParsedRangeReference? TryParseRangeReference(string text)
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

        if (!TryParseCellReference(parts[0], out var startCol, out var startRow)
            || !TryParseCellReference(parts[1], out var endCol, out var endRow))
            return null;

        return new ParsedRangeReference(
            sheetName,
            Math.Min(startCol, endCol),
            Math.Min(startRow, endRow),
            Math.Max(startCol, endCol),
            Math.Max(startRow, endRow));
    }

    private static uint FirstCategoryDataRow(ParsedRangeReference range, bool firstColumnIsCategories) =>
        firstColumnIsCategories && range.EndRow > range.StartRow ? range.StartRow + 1 : range.StartRow;

    private static bool TryParseCellReference(string text, out uint col, out uint row)
    {
        var normalized = text.Replace("$", "", StringComparison.Ordinal).Trim();
        var letterCount = normalized.TakeWhile(char.IsLetter).Count();
        col = 0;
        row = 0;
        if (letterCount == 0 || letterCount == normalized.Length)
            return false;

        col = CellAddress.ColumnNameToNumber(normalized[..letterCount]);
        return col > 0 && uint.TryParse(normalized[letterCount..], out row) && row > 0;
    }

    private static string FormatRangeReference(string? sheetName, uint startCol, uint startRow, uint endCol, uint endRow)
    {
        var prefix = string.IsNullOrWhiteSpace(sheetName) ? "" : $"{sheetName}!";
        var start = "$" + CellAddress.NumberToColumnName(startCol) + "$" + startRow;
        var end = "$" + CellAddress.NumberToColumnName(endCol) + "$" + endRow;
        return $"{prefix}{start}:{end}";
    }
}
