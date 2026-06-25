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
}
