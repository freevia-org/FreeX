using FreeX.Core.Model;

namespace FreeX.App.Presentation.GridInteraction;

/// <summary>
/// Determines whether a cell surface covers the worksheet's default gridlines. Explicit authored
/// cell borders are rendered separately and are not affected by this policy.
/// </summary>
public static class CellSurfaceGridlinePlanner
{
    public static bool HasVisibleFill(CellStyle? style, WorkbookTheme theme) =>
        style is not null &&
        (style.ResolveFillColor(theme) is not null ||
         style.GradientFill is not null ||
         style.FillPatternStyle != CellFillPatternStyle.None);
}
