using Avalonia;
using Avalonia.Media;
using FreeX.App.Presentation.Rendering;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Converts a <see cref="CellGradientFill"/> model (OOXML <c>&lt;gradientFill&gt;</c>) into an
/// Avalonia <see cref="IBrush"/>, mirroring the WPF <c>BuildCellGradientBrush</c> method in
/// <c>GridView.Rendering.CellStyles.cs</c>.
/// </summary>
/// <remarks>
/// Gradient geometry and stop policy come from <see cref="CellFillMaterializationPlanner"/>;
/// this type only creates native Avalonia brushes.
/// </remarks>
internal static class CellGradientBrush
{
    /// <summary>
    /// Builds an Avalonia gradient brush for <paramref name="gradient"/>.
    /// Returns <see langword="null"/> when the gradient has no stops (caller falls through to white).
    /// </summary>
    public static IBrush? Build(CellGradientFill gradient)
    {
        var plan = CellFillMaterializationPlanner.PlanGradient(
            gradient,
            EmptyCellGradientBehavior.UseFallback);
        return plan is null ? null : Build(plan);
    }

    public static IBrush Build(CellGradientMaterializationPlan plan) =>
        plan.Kind == CellFillBackgroundKind.RadialGradient
            ? BuildRadial(plan)
            : BuildLinear(plan);

    // ── Linear ──────────────────────────────────────────────────────────────────────────────────

    private static IBrush BuildLinear(CellGradientMaterializationPlan gradient)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(gradient.Start.X, gradient.Start.Y, RelativeUnit.Relative),
            EndPoint   = new RelativePoint(gradient.End.X, gradient.End.Y, RelativeUnit.Relative),
            SpreadMethod = MapSpread(gradient.Spread),
        };

        foreach (var stop in gradient.Stops)
            brush.GradientStops.Add(new GradientStop(ToColor(stop.Color), stop.Offset));

        return brush;
    }

    // ── Path (radial approximation) ─────────────────────────────────────────────────────────────

    private static IBrush BuildRadial(CellGradientMaterializationPlan gradient)
    {
        // Path gradient: inner rectangle defined by insets → approximate as radial centered on
        // the inset origin (same approximation as WPF reference).
        // RadiusX/RadiusY in Avalonia 12 are RelativeScalar (value + unit), analogous to
        // WPF's RadiusX/RadiusY with MappingMode=RelativeToBoundingBox. We set each to the
        // largest half-extent from the computed origin so the gradient spans the full cell —
        // same logic as the WPF reference.
        var brush = new RadialGradientBrush
        {
            Center         = new RelativePoint(gradient.Center.X, gradient.Center.Y, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(gradient.Origin.X, gradient.Origin.Y, RelativeUnit.Relative),
            RadiusX        = new RelativeScalar(gradient.RadiusX, RelativeUnit.Relative),
            RadiusY        = new RelativeScalar(gradient.RadiusY, RelativeUnit.Relative),
            SpreadMethod   = MapSpread(gradient.Spread),
        };

        foreach (var stop in gradient.Stops)
            brush.GradientStops.Add(new GradientStop(ToColor(stop.Color), stop.Offset));

        return brush;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts an Excel linear-gradient degree to a WPF/Avalonia (StartPoint, EndPoint) pair
    /// in relative bounding-box coordinates ([0,1]×[0,1], origin = top-left, Y increases down).
    /// </summary>
    /// <remarks>
    /// Excel degrees are clockwise from the left edge (Y-down space):
    /// <list type="bullet">
    ///   <item>0° → left→right (pure horizontal)</item>
    ///   <item>90° → top→bottom (pure vertical)</item>
    ///   <item>180° → right→left</item>
    ///   <item>270° → bottom→top</item>
    /// </list>
    /// The direction vector is (cos θ, sin θ) in Y-down space, giving the gradient axis from the
    /// cell centre.  Start = centre − ½·dir, End = centre + ½·dir.
    /// This is an exact port of the WPF reference implementation.
    /// </remarks>
    /// <param name="degree">Excel gradient degree (clockwise from left, Y-down).</param>
    /// <returns>(start, end) in relative [0,1] coordinates.</returns>
    public static ((double X, double Y) Start, (double X, double Y) End) LinearGradientPoints(double degree)
    {
        var (start, end) = CellFillMaterializationPlanner.PlanLinearGradientAxis(degree);
        return ((start.X, start.Y), (end.X, end.Y));
    }

    private static GradientSpreadMethod MapSpread(CellGradientSpreadMode spread) =>
        spread switch
        {
            CellGradientSpreadMode.Pad => GradientSpreadMethod.Pad,
            _ => GradientSpreadMethod.Pad,
        };

    private static Color ToColor(CellColor c) => Color.FromRgb(c.R, c.G, c.B);
}
