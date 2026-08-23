using Free.Shared.Drawing;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxDrawingThemeColorSlots
{
    private static readonly (WorkbookThemeColorSlot Slot, string ElementName)[] ColorSchemeElementEntries =
        DrawingMlThemeColorSlotMapper.ColorSchemeElements
            .Select(element => (FromSharedSlot(element.Slot), element.ElementName))
            .ToArray();

    public static IReadOnlyList<(WorkbookThemeColorSlot Slot, string ElementName)> ColorSchemeElements =>
        ColorSchemeElementEntries;

    public static bool TryMapRole(string? roleName, out WorkbookThemeColorSlot slot)
    {
        slot = default;
        if (!DrawingMlThemeColorSlotMapper.TryMapRole(roleName, out var sharedSlot))
            return false;

        slot = FromSharedSlot(sharedSlot);
        return true;
    }

    public static string ToSchemeColorValue(WorkbookThemeColorSlot slot) =>
        DrawingMlThemeColorSlotMapper.ToSchemeColorValue(ToSharedSlot(slot));

    private static WorkbookThemeColorSlot FromSharedSlot(DrawingMlThemeColorSlot slot) =>
        slot switch
        {
            DrawingMlThemeColorSlot.Dark1 => WorkbookThemeColorSlot.Dark1,
            DrawingMlThemeColorSlot.Light1 => WorkbookThemeColorSlot.Light1,
            DrawingMlThemeColorSlot.Dark2 => WorkbookThemeColorSlot.Dark2,
            DrawingMlThemeColorSlot.Light2 => WorkbookThemeColorSlot.Light2,
            DrawingMlThemeColorSlot.Accent1 => WorkbookThemeColorSlot.Accent1,
            DrawingMlThemeColorSlot.Accent2 => WorkbookThemeColorSlot.Accent2,
            DrawingMlThemeColorSlot.Accent3 => WorkbookThemeColorSlot.Accent3,
            DrawingMlThemeColorSlot.Accent4 => WorkbookThemeColorSlot.Accent4,
            DrawingMlThemeColorSlot.Accent5 => WorkbookThemeColorSlot.Accent5,
            DrawingMlThemeColorSlot.Accent6 => WorkbookThemeColorSlot.Accent6,
            DrawingMlThemeColorSlot.Hyperlink => WorkbookThemeColorSlot.Hyperlink,
            DrawingMlThemeColorSlot.FollowedHyperlink => WorkbookThemeColorSlot.FollowedHyperlink,
            _ => WorkbookThemeColorSlot.Dark1
        };

    public static DrawingMlThemeColorSlot ToSharedSlot(WorkbookThemeColorSlot slot) =>
        slot switch
        {
            WorkbookThemeColorSlot.Dark1 => DrawingMlThemeColorSlot.Dark1,
            WorkbookThemeColorSlot.Light1 => DrawingMlThemeColorSlot.Light1,
            WorkbookThemeColorSlot.Dark2 => DrawingMlThemeColorSlot.Dark2,
            WorkbookThemeColorSlot.Light2 => DrawingMlThemeColorSlot.Light2,
            WorkbookThemeColorSlot.Accent1 => DrawingMlThemeColorSlot.Accent1,
            WorkbookThemeColorSlot.Accent2 => DrawingMlThemeColorSlot.Accent2,
            WorkbookThemeColorSlot.Accent3 => DrawingMlThemeColorSlot.Accent3,
            WorkbookThemeColorSlot.Accent4 => DrawingMlThemeColorSlot.Accent4,
            WorkbookThemeColorSlot.Accent5 => DrawingMlThemeColorSlot.Accent5,
            WorkbookThemeColorSlot.Accent6 => DrawingMlThemeColorSlot.Accent6,
            WorkbookThemeColorSlot.Hyperlink => DrawingMlThemeColorSlot.Hyperlink,
            WorkbookThemeColorSlot.FollowedHyperlink => DrawingMlThemeColorSlot.FollowedHyperlink,
            _ => DrawingMlThemeColorSlot.Dark1
        };
}
