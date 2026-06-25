using Avalonia;
using Avalonia.Media;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Converts a <see cref="CellGradientFill"/> model (OOXML <c>&lt;gradientFill&gt;</c>) into an
/// Avalonia <see cref="IBrush"/>, mirroring the WPF <c>BuildCellGradientBrush</c> method in
/// <c>GridView.Rendering.CellStyles.cs</c>.
/// </summary>
/// <remarks>
/// Degree→StartPoint/EndPoint math is kept in a separate, UI-free static method
/// (<see cref="LinearGradientPoints"/>) so it can be exercised by unit tests without
/// a running Avalonia application.
/// </remarks>
internal static class CellGradientBrush
{
    /// <summary>
    /// Builds an Avalonia gradient brush for <paramref name="gradient"/>.
    /// Returns <see langword="null"/> when the gradient has no stops (caller falls through to white).
    /// </summary>
    public static IBrush? Build(CellGradientFill gradient)
    {
        if (gradient.Stops.Count == 0)
            return null;

        if (gradient.Type == CellGradientFillType.Path)
            return BuildRadial(gradient);

        return BuildLinear(gradient);
    }

    // ── Linear ──────────────────────────────────────────────────────────────────────────────────

    private static IBrush BuildLinear(CellGradientFill gradient)
    {
        var (start, end) = LinearGradientPoints(gradient.Degree);

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(start.X, start.Y, RelativeUnit.Relative),
            EndPoint   = new RelativePoint(end.X,   end.Y,   RelativeUnit.Relative),
        };

        foreach (var stop in gradient.Stops.OrderBy(s => s.Position))
            brush.GradientStops.Add(new GradientStop(ToColor(stop.Color), stop.Position));

        return brush;
    }

    // ── Path (radial approximation) ─────────────────────────────────────────────────────────────

    private static IBrush BuildRadial(CellGradientFill gradient)
    {
        // Path gradient: inner rectangle defined by insets → approximate as radial centered on
        // the inset origin (same approximation as WPF reference).
        var originX = gradient.Left + (1.0 - gradient.Left - gradient.Right) / 2.0;
        var originY = gradient.Top  + (1.0 - gradient.Top  - gradient.Bottom) / 2.0;

        // RadiusX/RadiusY in Avalonia 12 are RelativeScalar (value + unit), analogous to
        // WPF's RadiusX/RadiusY with MappingMode=RelativeToBoundingBox. We set each to the
        // largest half-extent from the computed origin so the gradient spans the full cell —
        // same logic as the WPF reference.
        var brush = new RadialGradientBrush
        {
            Center         = new RelativePoint(originX, originY, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(originX, originY, RelativeUnit.Relative),
            RadiusX        = new RelativeScalar(Math.Max(originX, 1.0 - originX), RelativeUnit.Relative),
            RadiusY        = new RelativeScalar(Math.Max(originY, 1.0 - originY), RelativeUnit.Relative),
        };

        foreach (var stop in gradient.Stops.OrderBy(s => s.Position))
            brush.GradientStops.Add(new GradientStop(ToColor(stop.Color), stop.Position));

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
        var radians = degree * Math.PI / 180.0;
        var dx = Math.Cos(radians);
        var dy = Math.Sin(radians); // positive = downward in Y-down space

        var start = (X: 0.5 - 0.5 * dx, Y: 0.5 - 0.5 * dy);
        var end   = (X: 0.5 + 0.5 * dx, Y: 0.5 + 0.5 * dy);
        return (start, end);
    }

    private static Color ToColor(CellColor c) => Color.FromRgb(c.R, c.G, c.B);
}
