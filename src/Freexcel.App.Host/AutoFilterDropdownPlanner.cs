using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record AutoFilterDropdownPlan(GridRange Range, uint FilterColumnOffset);

public sealed record AutoFilterChecklistItem(string DisplayText, string Value);

public static class AutoFilterDropdownPlanner
{
    public const string BlankDisplayText = "(Blanks)";

    public static bool TryPlan(GridRange currentRegion, CellAddress activeCell, out AutoFilterDropdownPlan plan)
    {
        plan = default!;
        if (activeCell.Sheet != currentRegion.Start.Sheet ||
            activeCell.Row != currentRegion.Start.Row ||
            activeCell.Col < currentRegion.Start.Col ||
            activeCell.Col > currentRegion.End.Col)
        {
            return false;
        }

        plan = new AutoFilterDropdownPlan(currentRegion, activeCell.Col - currentRegion.Start.Col);
        return true;
    }

    public static IReadOnlyList<AutoFilterChecklistItem> CreateChecklistItems(Sheet sheet, AutoFilterDropdownPlan plan)
    {
        var filterColumn = plan.Range.Start.Col + plan.FilterColumnOffset;
        var seenValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<AutoFilterChecklistItem>();

        for (var row = plan.Range.Start.Row + 1; row <= plan.Range.End.Row; row++)
        {
            var value = SpreadsheetDisplayFormatter.FormatCellValue(sheet.GetValue(row, filterColumn));
            if (!seenValues.Add(value))
                continue;

            items.Add(new AutoFilterChecklistItem(
                string.IsNullOrEmpty(value) ? BlankDisplayText : value,
                value));
        }

        return items;
    }
}
