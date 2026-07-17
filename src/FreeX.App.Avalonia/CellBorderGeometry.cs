using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Pure mapping helpers (no Avalonia types) from <see cref="BorderStyle"/> to rendering parameters.
/// Mirrors the WPF <c>DrawBorderEdge</c> thickness and dash tables in
/// <c>GridView.Rendering.CellStyles.cs</c> so the two renderers stay in sync.
/// Kept in a static class so the mapping is unit-testable without a running UI.
/// </summary>
internal static class CellBorderGeometry
{
    // ── Thickness (logical pixels, matching WPF reference) ──────────────────────────────────────

    /// <summary>
    /// Returns the stroke thickness for a border style, matching the WPF
    /// <c>DrawBorderEdge</c> thickness table (Hair=0.25, Thin=0.5,
    /// Medium/MediumDashed/MediumDashDot/MediumDashDotDot=1.5, Thick=2.5).
    /// </summary>
    public static double GetThickness(BorderStyle style) => style switch
    {
        BorderStyle.Hair                                              => 0.25,
        BorderStyle.Thin                                              => 0.5,
        BorderStyle.Medium or BorderStyle.MediumDashed
            or BorderStyle.MediumDashDot or BorderStyle.MediumDashDotDot => 1.5,
        BorderStyle.Thick                                             => 2.5,
        _                                                             => 0.5,   // Dashed, Dotted, DashDot, DashDotDot, SlantDashDot
    };

    // ── Dash arrays (Avalonia has no DashStyles presets) ────────────────────────────────────────
    // Arrays are segment / gap lengths in units of stroke thickness (matching WPF DashStyles).
    // Solid (null) means no StrokeDashArray is set.

    private static readonly double[] Dash        = [2, 2];
    private static readonly double[] Dot         = [1, 2];
    private static readonly double[] DashDot     = [2, 2, 1, 2];
    private static readonly double[] DashDotDot  = [2, 2, 1, 2, 1, 2];

    /// <summary>
    /// Returns the Avalonia <c>StrokeDashArray</c> doubles for a border style, or
    /// <see langword="null"/> for solid styles (no dash array needed).
    /// Mirrors the WPF <c>DrawBorderEdge</c> dash mapping:
    /// <list type="bullet">
    ///   <item>Dashed / MediumDashed → [2,2]</item>
    ///   <item>Dotted → [1,2]</item>
    ///   <item>DashDot / MediumDashDot / SlantDashDot → [2,2,1,2] (SlantDashDot approximated)</item>
    ///   <item>DashDotDot / MediumDashDotDot → [2,2,1,2,1,2]</item>
    ///   <item>Hair / Thin / Medium / Thick / Double → null (solid)</item>
    /// </list>
    /// </summary>
    public static double[]? GetDashArray(BorderStyle style) => style switch
    {
        BorderStyle.Dashed or BorderStyle.MediumDashed               => Dash,
        BorderStyle.Dotted                                            => Dot,
        BorderStyle.DashDot or BorderStyle.MediumDashDot
            or BorderStyle.SlantDashDot                               => DashDot,
        BorderStyle.DashDotDot or BorderStyle.MediumDashDotDot       => DashDotDot,
        _                                                             => null,
    };

    // ── Double-border geometry (two parallel lines) ─────────────────────────────────────────────

    /// <summary>
    /// Gap in DIPs between the centerlines of the two strokes used to render
    /// <see cref="BorderStyle.Double"/>, matching the WPF <c>DrawDoubleBorderLines</c> constant
    /// in <c>GridView.Rendering.CellStyles.cs</c>.
    /// </summary>
    public const double DoubleBorderGap = 1.0;

    /// <summary>
    /// Computes the two offset line segments used to render Excel's "Double" border style as two
    /// thin parallel lines straddling the requested edge, instead of a single solid line. Works
    /// for horizontal, vertical, and diagonal edges alike by offsetting perpendicular to the edge
    /// direction, mirroring the WPF <c>DrawDoubleBorderLines</c> helper. Pure geometry (plain
    /// doubles, no Avalonia types) so it is unit-testable without a running UI.
    /// </summary>
    /// <remarks>
    /// When the requested edge has effectively zero length, both returned segments coincide with
    /// the original (degenerate) edge — there is no direction to offset perpendicular to.
    /// </remarks>
    public static (double X1, double Y1, double X2, double Y2, double X3, double Y3, double X4, double Y4)
        GetDoubleBorderLineOffsets(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6)
            return (x1, y1, x2, y2, x1, y1, x2, y2);

        var offsetX = -dy / length * (DoubleBorderGap / 2.0);
        var offsetY = dx / length * (DoubleBorderGap / 2.0);

        return (x1 + offsetX, y1 + offsetY, x2 + offsetX, y2 + offsetY,
                x1 - offsetX, y1 - offsetY, x2 - offsetX, y2 - offsetY);
    }

    // ── Adjacent-edge border-conflict resolution ────────────────────────────────────────────────

    /// <summary>
    /// Excel's deterministic weight ranking for resolving two conflicting border styles that both
    /// describe the same physical grid edge (one from each of the two adjoining cells), heaviest/
    /// most-prominent first. An unrecognized style ranks lowest (last). Mirrors the WPF
    /// <c>BorderEdgePrecedence</c> table in <c>GridView.Rendering.cs</c>.
    /// </summary>
    private static readonly BorderStyle[] BorderEdgePrecedence =
    {
        BorderStyle.Double,
        BorderStyle.Thick,
        BorderStyle.Medium,
        BorderStyle.MediumDashDotDot,
        BorderStyle.MediumDashDot,
        BorderStyle.MediumDashed,
        BorderStyle.SlantDashDot,
        BorderStyle.Thin,
        BorderStyle.DashDotDot,
        BorderStyle.DashDot,
        BorderStyle.Dashed,
        BorderStyle.Dotted,
        BorderStyle.Hair,
        BorderStyle.None,
    };

    private static int BorderEdgePrecedenceRank(BorderStyle style)
    {
        var index = Array.IndexOf(BorderEdgePrecedence, style);
        return index < 0 ? BorderEdgePrecedence.Length : index;
    }

    /// <summary>
    /// Resolves which of two <see cref="CellBorder"/> values describing the same shared grid edge
    /// (one owned by each neighboring cell) should actually be painted, matching Excel's
    /// deterministic "heavier style wins" rule instead of whichever cell happens to be drawn last.
    /// Symmetric in its two arguments, so both neighboring cells compute the identical winner
    /// regardless of render/iteration order. Mirrors the WPF <c>ResolveBorderEdgeWinner</c> in
    /// <c>GridView.Rendering.cs</c>.
    /// </summary>
    /// <remarks>
    /// Not yet wired into any caller: <see cref="CellBorderPanel"/> is constructed per-cell from a
    /// single <see cref="CellStyle"/> with no neighbor-style access (see <c>MainWindow.cs</c>'s
    /// cell-creation helpers that construct <c>CellBorderPanel</c>), so plumbing the four
    /// neighboring edge styles through to this resolver is tracked as residual follow-up work.
    /// </remarks>
    public static CellBorder ResolveBorderEdgeWinner(CellBorder mine, CellBorder neighbor)
    {
        if (mine.Style == BorderStyle.None) return neighbor;
        if (neighbor.Style == BorderStyle.None) return mine;
        return BorderEdgePrecedenceRank(mine.Style) <= BorderEdgePrecedenceRank(neighbor.Style) ? mine : neighbor;
    }
}
