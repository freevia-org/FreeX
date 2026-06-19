using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum CellBorderPreset
{
    All,
    Outside,
    Inside,
    NoBorder,
    Top,
    Right,
    Bottom,
    Left,

    // Compound / thick / double presets (parity with the Home ▸ Borders dropdown). These reuse the
    // same BorderShortcutService diff builders as the basic presets, only with a thicker/doubled
    // BorderStyle or a top+bottom combination.
    ThickBottom,
    DoubleBottom,
    ThickOutside,
    TopAndBottom,
    TopAndThickBottom,
    TopAndDoubleBottom
}

public static class CellBorderPresetPlanner
{
    public static StyleDiff Plan(
        CellBorderPreset preset,
        GridRange range,
        CellAddress address,
        BorderStyle style = BorderStyle.Thin,
        CellColor? color = null)
    {
        var borderColor = color ?? CellColor.Black;
        return preset switch
        {
            CellBorderPreset.All => BorderShortcutService.GetAllBorderDiff(style, borderColor),
            CellBorderPreset.Outside => BorderShortcutService.GetOutlineBorderDiff(range, address, style, borderColor),
            CellBorderPreset.Inside => BorderShortcutService.GetInsideBorderDiff(range, address, style, borderColor),
            CellBorderPreset.NoBorder => BorderShortcutService.GetClearBorderDiff(),
            CellBorderPreset.Top => BorderShortcutService.GetSingleBorderDiff(BorderEdge.Top, style, borderColor),
            CellBorderPreset.Right => BorderShortcutService.GetSingleBorderDiff(BorderEdge.Right, style, borderColor),
            CellBorderPreset.Bottom => BorderShortcutService.GetSingleBorderDiff(BorderEdge.Bottom, style, borderColor),
            CellBorderPreset.Left => BorderShortcutService.GetSingleBorderDiff(BorderEdge.Left, style, borderColor),
            CellBorderPreset.ThickBottom =>
                BorderShortcutService.GetSingleBorderDiff(BorderEdge.Bottom, BorderStyle.Thick, borderColor),
            CellBorderPreset.DoubleBottom =>
                BorderShortcutService.GetSingleBorderDiff(BorderEdge.Bottom, BorderStyle.Double, borderColor),
            CellBorderPreset.ThickOutside =>
                BorderShortcutService.GetOutlineBorderDiff(range, address, BorderStyle.Thick, borderColor),
            CellBorderPreset.TopAndBottom =>
                BorderShortcutService.GetTopAndBottomBorderDiff(range, address, style, style, borderColor),
            CellBorderPreset.TopAndThickBottom =>
                BorderShortcutService.GetTopAndBottomBorderDiff(range, address, style, BorderStyle.Thick, borderColor),
            CellBorderPreset.TopAndDoubleBottom =>
                BorderShortcutService.GetTopAndBottomBorderDiff(range, address, style, BorderStyle.Double, borderColor),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
        };
    }

    public static string GetDisplayName(CellBorderPreset preset) =>
        preset switch
        {
            CellBorderPreset.All => "All Borders",
            CellBorderPreset.Outside => "Outside Borders",
            CellBorderPreset.Inside => "Inside Borders",
            CellBorderPreset.NoBorder => "No Border",
            CellBorderPreset.Top => "Top Border",
            CellBorderPreset.Right => "Right Border",
            CellBorderPreset.Bottom => "Bottom Border",
            CellBorderPreset.Left => "Left Border",
            CellBorderPreset.ThickBottom => "Thick Bottom Border",
            CellBorderPreset.DoubleBottom => "Bottom Double Border",
            CellBorderPreset.ThickOutside => "Thick Outside Borders",
            CellBorderPreset.TopAndBottom => "Top and Bottom Border",
            CellBorderPreset.TopAndThickBottom => "Top and Thick Bottom Border",
            CellBorderPreset.TopAndDoubleBottom => "Top and Double Bottom Border",
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
        };

    public static bool RequiresPerCellPlanning(CellBorderPreset preset) =>
        preset is CellBorderPreset.Outside
            or CellBorderPreset.Inside
            or CellBorderPreset.ThickOutside
            or CellBorderPreset.TopAndBottom
            or CellBorderPreset.TopAndThickBottom
            or CellBorderPreset.TopAndDoubleBottom;
}
