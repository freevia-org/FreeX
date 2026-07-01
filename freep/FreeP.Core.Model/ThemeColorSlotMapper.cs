using Free.Shared.Drawing;

namespace FreeP.Core.Model;

/// <summary>
/// Shared DrawingML theme color role mapping for FreeP import and render-time resolution.
/// </summary>
public static class ThemeColorSlotMapper
{
    public static bool TryMapRole(string? roleName, out ThemeColorSlot slot)
    {
        slot = default;
        if (!DrawingMlThemeColorSlotMapper.TryMapRole(roleName, out var sharedSlot))
            return false;

        slot = FromSharedSlot(sharedSlot);
        return true;
    }

    public static ThemeColorSlot MapRoleToSlot(
        string? roleName,
        IReadOnlyDictionary<string, string>? effectiveClrMap,
        ThemeColorSlot fallbackSlot = ThemeColorSlot.Dk1) =>
        FromSharedSlot(DrawingMlThemeColorSlotMapper.MapRoleToSlot(
            roleName,
            effectiveClrMap,
            ToSharedSlot(fallbackSlot)));

    public static string ToSchemeColorString(ThemeColorSlot slot) =>
        DrawingMlThemeColorSlotMapper.ToSchemeColorValue(ToSharedSlot(slot));

    private static ThemeColorSlot FromSharedSlot(DrawingMlThemeColorSlot slot) =>
        slot switch
        {
            DrawingMlThemeColorSlot.Dark1 => ThemeColorSlot.Dk1,
            DrawingMlThemeColorSlot.Light1 => ThemeColorSlot.Lt1,
            DrawingMlThemeColorSlot.Dark2 => ThemeColorSlot.Dk2,
            DrawingMlThemeColorSlot.Light2 => ThemeColorSlot.Lt2,
            DrawingMlThemeColorSlot.Accent1 => ThemeColorSlot.Accent1,
            DrawingMlThemeColorSlot.Accent2 => ThemeColorSlot.Accent2,
            DrawingMlThemeColorSlot.Accent3 => ThemeColorSlot.Accent3,
            DrawingMlThemeColorSlot.Accent4 => ThemeColorSlot.Accent4,
            DrawingMlThemeColorSlot.Accent5 => ThemeColorSlot.Accent5,
            DrawingMlThemeColorSlot.Accent6 => ThemeColorSlot.Accent6,
            DrawingMlThemeColorSlot.Hyperlink => ThemeColorSlot.HLink,
            DrawingMlThemeColorSlot.FollowedHyperlink => ThemeColorSlot.FolHLink,
            _ => ThemeColorSlot.Dk1
        };

    private static DrawingMlThemeColorSlot ToSharedSlot(ThemeColorSlot slot) =>
        slot switch
        {
            ThemeColorSlot.Dk1 => DrawingMlThemeColorSlot.Dark1,
            ThemeColorSlot.Lt1 => DrawingMlThemeColorSlot.Light1,
            ThemeColorSlot.Dk2 => DrawingMlThemeColorSlot.Dark2,
            ThemeColorSlot.Lt2 => DrawingMlThemeColorSlot.Light2,
            ThemeColorSlot.Accent1 => DrawingMlThemeColorSlot.Accent1,
            ThemeColorSlot.Accent2 => DrawingMlThemeColorSlot.Accent2,
            ThemeColorSlot.Accent3 => DrawingMlThemeColorSlot.Accent3,
            ThemeColorSlot.Accent4 => DrawingMlThemeColorSlot.Accent4,
            ThemeColorSlot.Accent5 => DrawingMlThemeColorSlot.Accent5,
            ThemeColorSlot.Accent6 => DrawingMlThemeColorSlot.Accent6,
            ThemeColorSlot.HLink => DrawingMlThemeColorSlot.Hyperlink,
            ThemeColorSlot.FolHLink => DrawingMlThemeColorSlot.FollowedHyperlink,
            _ => DrawingMlThemeColorSlot.Dark1
        };
}
