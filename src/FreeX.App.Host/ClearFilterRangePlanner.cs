using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class ClearFilterRangePlanner
{
    public static GridRange Create(Sheet sheet, GridRange selectedRange)
        => AutoFilterToggleRangePlanner.Create(sheet, selectedRange);

    public static bool HasActiveFilter(Sheet sheet, GridRange range)
        => AutoFilterDropdownMenuPlanner.HasActiveFilter(sheet, range);
}
