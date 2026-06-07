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
    public const double MinimumWidth = 18;
    public const double MaximumWidth = 160;
    public const double MinimumHeight = 18;

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

        var rule = DataValidationService.GetApplicable(sheet, activeCell)
            .FirstOrDefault(static rule => rule.Type == DvType.List && rule.ShowDropdown);
        if (rule is null)
            return false;

        var items = DataValidationService.GetListItems(rule, sheet, workbook);
        if (items.Count == 0)
            return false;

        var currentText = FormatCellValue(sheet.GetCell(activeCell)?.Value);
        var selectedItem = items.FirstOrDefault(item =>
            string.Equals(item, currentText, StringComparison.OrdinalIgnoreCase));

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
