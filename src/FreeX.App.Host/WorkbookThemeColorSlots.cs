using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class WorkbookThemeColorSlots
{
    public static IReadOnlyList<WorkbookThemeColorSlot> All { get; } =
    [
        WorkbookThemeColorSlot.Dark1,
        WorkbookThemeColorSlot.Light1,
        WorkbookThemeColorSlot.Dark2,
        WorkbookThemeColorSlot.Light2,
        WorkbookThemeColorSlot.Accent1,
        WorkbookThemeColorSlot.Accent2,
        WorkbookThemeColorSlot.Accent3,
        WorkbookThemeColorSlot.Accent4,
        WorkbookThemeColorSlot.Accent5,
        WorkbookThemeColorSlot.Accent6,
        WorkbookThemeColorSlot.Hyperlink,
        WorkbookThemeColorSlot.FollowedHyperlink
    ];
}
