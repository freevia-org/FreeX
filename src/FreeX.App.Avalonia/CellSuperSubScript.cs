using FreeX.App.Presentation.Rendering;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Compatibility adapter for cell-level superscript and subscript materialization.
/// </summary>
/// <remarks>
/// Portable decisions are owned by <see cref="CellTextMaterializationPlanner"/>.
/// </remarks>
internal static class CellSuperSubScript
{
    // ── Constants (match WPF GridView.cs lines 487-489) ──────────────────────────────────────────

    /// <summary>
    /// Factor by which the cell font is shrunk for super/subscript (~7/12 ≈ 58.3%).
    /// Matches Excel's rendering of superscript and subscript glyphs.
    /// </summary>
    public const double FontSizeFactor = CellTextMaterializationPlanner.ScriptFontSizeFactor;

    /// <summary>
    /// Superscript baseline: shift UP by this fraction of the NORMAL (pre-scaled) font size.
    /// A negative margin-top (or positive Canvas offset upward) of <c>NormalFontSize × SuperBaselineRatio</c>.
    /// </summary>
    public const double SuperBaselineRatio = CellTextMaterializationPlanner.SuperscriptBaselineRatio;

    /// <summary>
    /// Subscript baseline: shift DOWN by this fraction of the NORMAL (pre-scaled) font size.
    /// A positive margin-top of <c>NormalFontSize × SubBaselineRatio</c>.
    /// </summary>
    public const double SubBaselineRatio = CellTextMaterializationPlanner.SubscriptBaselineRatio;

    // ── Public API ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="style"/> sets cell-level
    /// <see cref="CellStyle.Superscript"/> or <see cref="CellStyle.Subscript"/>.
    /// </summary>
    public static bool IsActive(CellStyle? style) =>
        CellTextMaterializationPlanner.Plan(
            string.Empty,
            false,
            style,
            1,
            null,
            CellTextMaterializationProfile.Avalonia).Baseline != CellTextBaselineKind.Baseline;

    /// <summary>
    /// Given the displayed font size (already scaled by zoom factor), returns the adjusted font
    /// size and the vertical offset in DIPs to apply as a top margin / canvas offset.
    /// </summary>
    /// <param name="style">Cell style (may be null; returns identity adjustment).</param>
    /// <param name="scaledFontSize">Font size in DIPs after zoom scaling.</param>
    /// <param name="adjustedFontSize">Output: reduced font size to use for the TextBlock.</param>
    /// <param name="verticalOffsetDip">
    /// Output: vertical offset in DIPs.
    /// Negative = upward (superscript uses negative top margin to shift the glyph up).
    /// Positive = downward (subscript uses positive top margin to shift the glyph down).
    /// </param>
    public static void Resolve(
        CellStyle? style,
        double scaledFontSize,
        out double adjustedFontSize,
        out double verticalOffsetDip)
    {
        var plan = CellTextMaterializationPlanner.Plan(
            string.Empty,
            false,
            style,
            scaledFontSize,
            null,
            CellTextMaterializationProfile.Avalonia);
        adjustedFontSize = plan.RenderedFontSize;
        verticalOffsetDip = plan.BaselineOffset;
    }
}
