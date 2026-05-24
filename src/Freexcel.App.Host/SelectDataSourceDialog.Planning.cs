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
    public static SelectDataSourceDialogResult CreateResult(
        string sourceRangeText,
        bool firstColumnIsCategories,
        bool switchRowColumn = false) =>
        new(sourceRangeText.Trim(), firstColumnIsCategories, switchRowColumn);

    public static SelectDataSourcePreview InferPreviewEntries(string sourceRangeText, bool firstColumnIsCategories)
    {
        var parsed = TryParseRangeReference(sourceRangeText);
        if (parsed is null)
        {
            return new SelectDataSourcePreview(
                [new SelectDataSourceSeriesPreview("Series 1", sourceRangeText.Trim())],
                [new SelectDataSourceCategoryPreview("Category labels")],
                "");
        }

        var (sheetName, startCol, startRow, endCol, endRow) = parsed.Value;
        var firstSeriesColumn = firstColumnIsCategories && endCol > startCol ? startCol + 1 : startCol;
        var firstDataRow = firstColumnIsCategories && endRow > startRow ? startRow + 1 : startRow;
        var series = new List<SelectDataSourceSeriesPreview>();
        for (var col = firstSeriesColumn; col <= endCol; col++)
        {
            var seriesName = $"Series {series.Count + 1}";
            series.Add(new SelectDataSourceSeriesPreview(
                seriesName,
                FormatRangeReference(sheetName, col, firstDataRow, col, endRow)));
        }

        if (series.Count == 0)
            series.Add(new SelectDataSourceSeriesPreview("Series 1", sourceRangeText.Trim()));

        var categories = new List<SelectDataSourceCategoryPreview>();
        var categoryStartRow = firstColumnIsCategories && endRow > startRow ? startRow + 1 : startRow;
        for (var row = categoryStartRow; row <= endRow; row++)
            categories.Add(new SelectDataSourceCategoryPreview($"Category {categories.Count + 1}"));

        if (categories.Count == 0)
            categories.Add(new SelectDataSourceCategoryPreview("Category labels"));

        var categoryRange = firstColumnIsCategories
            ? FormatRangeReference(sheetName, startCol, categoryStartRow, startCol, endRow)
            : "";

        return new SelectDataSourcePreview(series, categories, categoryRange);
    }

    public static SelectDataSourceRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        new(currentText.Trim(), CollapseDialog: true);

    private static (string? SheetName, uint StartCol, uint StartRow, uint EndCol, uint EndRow)? TryParseRangeReference(string text)
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

        return (
            sheetName,
            Math.Min(startCol, endCol),
            Math.Min(startRow, endRow),
            Math.Max(startCol, endCol),
            Math.Max(startRow, endRow));
    }

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
