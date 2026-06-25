using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Calculates font-size and baseline-offset adjustments for cell-level superscript and subscript,
/// mirroring WPF's <c>ResolveSuperSubFontAdjustment</c> in
/// <c>GridView.Rendering.CellStyles.cs</c> (lines 317–338) and the constants defined in
/// <c>GridView.cs</c> (lines 487–489).
/// </summary>
/// <remarks>
/// All math is kept in static pure methods so the logic is unit-testable without a UI thread.
/// </remarks>
internal static class CellSuperSubScript
{
    // ── Constants (match WPF GridView.cs lines 487-489) ──────────────────────────────────────────

    /// <summary>
    /// Factor by which the cell font is shrunk for super/subscript (~7/12 ≈ 58.3%).
    /// Matches Excel's rendering of superscript and subscript glyphs.
    /// </summary>
    public const double FontSizeFactor = 0.583;

    /// <summary>
    /// Superscript baseline: shift UP by this fraction of the NORMAL (pre-scaled) font size.
    /// A negative margin-top (or positive Canvas offset upward) of <c>NormalFontSize × SuperBaselineRatio</c>.
    /// </summary>
    public const double SuperBaselineRatio = 0.33;

    /// <summary>
    /// Subscript baseline: shift DOWN by this fraction of the NORMAL (pre-scaled) font size.
    /// A positive margin-top of <c>NormalFontSize × SubBaselineRatio</c>.
    /// </summary>
    public const double SubBaselineRatio = 0.14;

    // ── Public API ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="style"/> sets cell-level
    /// <see cref="CellStyle.Superscript"/> or <see cref="CellStyle.Subscript"/>.
    /// </summary>
    public static bool IsActive(CellStyle? style) =>
        style?.Superscript == true || style?.Subscript == true;

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
        if (style?.Superscript == true)
        {
            adjustedFontSize  = scaledFontSize * FontSizeFactor;
            verticalOffsetDip = -(scaledFontSize * SuperBaselineRatio);   // upward
        }
        else if (style?.Subscript == true)
        {
            adjustedFontSize  = scaledFontSize * FontSizeFactor;
            verticalOffsetDip =  scaledFontSize * SubBaselineRatio;       // downward
        }
        else
        {
            adjustedFontSize  = scaledFontSize;
            verticalOffsetDip = 0;
        }
    }
}
