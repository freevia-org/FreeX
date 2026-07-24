using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record DataValidationDropdownCellBounds(
    double Left,
    double Top,
    double Width,
    double Height);

public sealed record DataValidationDropdownBounds(
    double Left,
    double Top,
    double Width,
    double Height);

public sealed record DataValidationDropdownPlan(
    IReadOnlyList<string> Items,
    string? SelectedItem,
    DataValidationDropdownBounds Bounds);

public static class DataValidationDropdownPlanner
{
    public const int MaximumDropdownItems = 10_000;
    public const double MinimumWidth = 18;
    public const double MaximumWidth = 160;
    public const double MinimumHeight = 18;

    /// <summary>
    /// True when <paramref name="activeCell"/> carries an in-cell list validation that shows a
    /// dropdown with at least one selectable item. This is the layout-free predicate behind the
    /// "Pick From Drop-down List" cell context-menu command: it answers "is there a list to pick from?"
    /// without needing the cell's pixel bounds (which <see cref="TryPlan"/> requires for placement).
    /// </summary>
    public static bool HasDropdownList(Workbook workbook, Sheet sheet, CellAddress activeCell)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);

        if (activeCell.Sheet != sheet.Id)
            return false;

        if (!TryGetDropdownItems(workbook, sheet, activeCell, out var items))
            return false;

        return items.Count > 0 && items.Count <= MaximumDropdownItems;
    }

    public static bool TryPlan(
        Workbook workbook,
        Sheet sheet,
        CellAddress activeCell,
        DataValidationDropdownCellBounds cellBounds,
        out DataValidationDropdownPlan plan)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);

        plan = default!;
        if (activeCell.Sheet != sheet.Id || !HasUsableBounds(cellBounds))
            return false;

        if (!TryGetDropdownItems(workbook, sheet, activeCell, out var items))
            return false;

        if (items.Count == 0 || items.Count > MaximumDropdownItems)
            return false;

        var currentText = FormatCellValue(sheet.GetCell(activeCell)?.Value);
        var selectedItem = FindSelectedItem(items, currentText);

        var width = Math.Max(MinimumWidth, Math.Min(cellBounds.Width, MaximumWidth));
        var height = Math.Max(MinimumHeight, cellBounds.Height);
        plan = new DataValidationDropdownPlan(
            items,
            selectedItem,
            new DataValidationDropdownBounds(
                cellBounds.Left + cellBounds.Width - width,
                cellBounds.Top,
                width,
                height));
        return true;
    }

    private static bool HasUsableBounds(DataValidationDropdownCellBounds bounds) =>
        IsFinite(bounds.Left) &&
        IsFinite(bounds.Top) &&
        IsFinite(bounds.Width) &&
        IsFinite(bounds.Height) &&
        bounds.Width >= 0 &&
        bounds.Height >= 0;

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static DataValidation? FindDropdownRule(IEnumerable<DataValidation> rules)
    {
        foreach (var rule in rules)
        {
            if (rule.Type == DvType.List && rule.ShowDropdown)
                return rule;
        }

        return null;
    }

    /// <summary>
    /// Resolves the items "Pick From Drop-down List" should offer for <paramref name="activeCell"/>:
    /// the in-cell list validation's items when one applies, otherwise (matching Excel, where this
    /// command works on any plain text column with no Data Validation at all) the distinct text
    /// values from the contiguous run of non-blank cells in the same column as the active cell.
    /// </summary>
    private static bool TryGetDropdownItems(
        Workbook workbook,
        Sheet sheet,
        CellAddress activeCell,
        out IReadOnlyList<string> items)
    {
        var rule = FindDropdownRule(DataValidationService.GetApplicable(sheet, activeCell));
        if (rule is not null)
        {
            try
            {
                items = DataValidationService.GetListItems(rule, sheet, activeCell, workbook);
                return true;
            }
            catch
            {
                items = Array.Empty<string>();
                return false;
            }
        }

        items = GetContiguousColumnTextValues(sheet, activeCell);
        return true;
    }

    private static IReadOnlyList<string> GetContiguousColumnTextValues(Sheet sheet, CellAddress activeCell)
    {
        var col = activeCell.Col;

        var topRow = activeCell.Row;
        while (topRow > 1 && IsNonBlank(sheet.GetCell(topRow - 1, col)))
            topRow--;

        var bottomRow = activeCell.Row;
        while (bottomRow < CellAddress.MaxRow && IsNonBlank(sheet.GetCell(bottomRow + 1, col)))
            bottomRow++;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<string>();
        for (var row = topRow; row <= bottomRow; row++)
        {
            if (row == activeCell.Row)
                continue;

            if (sheet.GetCell(row, col)?.Value is TextValue { Value.Length: > 0 } text && seen.Add(text.Value))
                items.Add(text.Value);
        }

        return items;
    }

    private static bool IsNonBlank(Cell? cell) => cell is not null && cell.Value is not BlankValue;

    private static string? FindSelectedItem(IReadOnlyList<string> items, string currentText)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (string.Equals(item, currentText, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        return null;
    }

    private static string FormatCellValue(ScalarValue? value) => value switch
    {
        null or BlankValue => "",
        NumberValue n => n.Value.ToString(CultureInfo.InvariantCulture),
        TextValue t => t.Value,
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        DateTimeValue dt => FormatDateTimeCellValue(dt),
        ErrorValue err => err.Code,
        _ => ""
    };

    private static string FormatDateTimeCellValue(DateTimeValue value)
    {
        try
        {
            return value.ToDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {
            return value.Value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
