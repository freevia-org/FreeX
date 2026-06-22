using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class AdvancedFilterDefaultListRangePlanner
{
    public static GridRange Create(Sheet sheet, GridRange selectedRange)
        => AdvancedFilterPlanner.CreateDefaultListRange(sheet, selectedRange);
}
