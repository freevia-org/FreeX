using Avalonia;
using Avalonia.Media;
using Free.Shared.Ribbon.Avalonia;

namespace Free.Shared.Ribbon.Tests;

/// <summary>
/// Regression tests for round 577's finding in <see cref="SvgIconParser"/>: every numeric field it
/// reads went through <c>double.TryParse</c>, which returns <c>true</c> with +/-Infinity on
/// magnitude overflow (it has not thrown since .NET Core) and also accepts the literal "NaN"
/// spelling, so non-finite numbers entered the produced Avalonia drawing.
/// <para>
/// The gradient-offset site is the sharp one: it "bounded" the offset with
/// <see cref="Math.Clamp(double, double, double)"/>, which bounds Infinity but PROPAGATES NaN --
/// the same sub-shape found in r547/r555 -- so a stop offset of NaN reached
/// <see cref="GradientStop.Offset"/> unchanged.
/// </para>
/// <para>
/// This parser is not fed only by the repository's own icon assets: FreeW's Insert Picture writes a
/// user-chosen .svg to a temporary file and parses it here
/// (<c>AvaloniaPictureRasterizerPort.RasterizeSvg</c> -> <c>SvgIconRasterizer</c>), so the input is
/// an arbitrary external file.
/// </para>
/// </summary>
public sealed class R577_SvgNonFiniteNumberTests
{
    private static Drawing Parse(string svg)
    {
        using var temporaryDirectory = new TestTemporaryDirectory("free-shared-ribbon-svg-r577-");
        var path = Path.Combine(temporaryDirectory.Path, "icon.svg");
        File.WriteAllText(path, svg);
        return SvgIconRasterizer.LoadFile(path).Drawing!;
    }

    private static IEnumerable<GradientStop> GradientStops(Drawing drawing) =>
        Flatten(drawing)
            .OfType<GeometryDrawing>()
            .Select(geometry => geometry.Brush)
            .OfType<GradientBrush>()
            .SelectMany(brush => brush.GradientStops);

    private static IEnumerable<Drawing> Flatten(Drawing drawing)
    {
        yield return drawing;
        if (drawing is not DrawingGroup group)
            yield break;
        foreach (var child in group.Children)
            foreach (var nested in Flatten(child))
                yield return nested;
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("NaN%")]
    [InlineData("1e400")]
    [InlineData("1e400%")]
    public void GradientStopOffset_IsAlwaysFinite(string offset)
    {
        var drawing = Parse($$"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
              <defs>
                <linearGradient id="g" x1="0" y1="0" x2="1" y2="0">
                  <stop offset="{{offset}}" stop-color="#ff0000" />
                  <stop offset="1" stop-color="#0000ff" />
                </linearGradient>
              </defs>
              <rect x="0" y="0" width="32" height="32" fill="url(#g)" />
            </svg>
            """);

        var stops = GradientStops(drawing).ToList();
        stops.Should().NotBeEmpty("the gradient-filled rect must have produced a gradient brush");
        foreach (var stop in stops)
            double.IsFinite(stop.Offset).Should().BeTrue(
                $"offset \"{offset}\" produced the non-finite GradientStop.Offset {stop.Offset}");
    }

    [Fact]
    public void GradientStopOffset_FiniteValuesStillReadThrough()
    {
        // Non-vacuity: the finite test must not have flattened every offset to the 0 fallback.
        var drawing = Parse("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
              <defs>
                <linearGradient id="g" x1="0" y1="0" x2="1" y2="0">
                  <stop offset="25%" stop-color="#ff0000" />
                  <stop offset="1" stop-color="#0000ff" />
                </linearGradient>
              </defs>
              <rect x="0" y="0" width="32" height="32" fill="url(#g)" />
            </svg>
            """);

        GradientStops(drawing).Select(stop => stop.Offset).Should().Equal(0.25, 1.0);
    }

    private static IList<Point> PolygonPoints(Drawing drawing) =>
        Flatten(drawing)
            .OfType<GeometryDrawing>()
            .Select(geometry => geometry.Geometry)
            .OfType<PolylineGeometry>()
            .Single()
            .Points;

    [Theory]
    [InlineData("1e400")]
    [InlineData("NaN")]
    public void PolygonPoints_NonFiniteTokensNeverEnterTheGeometry(string coordinate)
    {
        // <polygon points> is the attribute SplitNumbers reads, so this drives the number-list
        // flush directly. A dropped token re-pairs the remaining numbers exactly as a dropped
        // unparseable token always did; what must never happen is a non-finite point. (The
        // assertion reads the geometry's own points rather than its bounds: Geometry.GetBounds
        // needs a real Avalonia render interface, which the headless test host does not have.)
        var points = PolygonPoints(Parse(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 32 32\">" +
            $"<polygon points=\"4,4 {coordinate},20 28,28 4,28\" fill=\"#000000\" />" +
            "</svg>"));

        foreach (var point in points)
        {
            double.IsFinite(point.X).Should().BeTrue($"point {point} came from \"{coordinate}\"");
            double.IsFinite(point.Y).Should().BeTrue($"point {point} came from \"{coordinate}\"");
        }
    }

    [Fact]
    public void PolygonPoints_FiniteCoordinatesStillReadThrough()
    {
        // Non-vacuity: an ordinary polygon must still carry the coordinates it was given.
        var points = PolygonPoints(Parse(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 32 32\">" +
            "<polygon points=\"4,4 20,20 28,28 4,28\" fill=\"#000000\" />" +
            "</svg>"));

        points.Should().Equal(new Point(4, 4), new Point(20, 20), new Point(28, 28), new Point(4, 28));
    }

    private static IEnumerable<Rect> RectangleRects(Drawing drawing) =>
        Flatten(drawing)
            .OfType<GeometryDrawing>()
            .Select(geometry => geometry.Geometry)
            .OfType<RectangleGeometry>()
            .Select(geometry => geometry.Rect);

    [Theory]
    [InlineData("1e400")]
    [InlineData("NaN")]
    public void RectAttributes_NonFiniteLengthNeverReachesTheGeometry(string length)
    {
        // ParseDouble is the third entry point (element attributes: x/y/width/height/r/rx/ry/...).
        // BuildRect's own range test is the reject form `w <= 0 || h <= 0`, which rejects NEITHER
        // Infinity (not <= 0) nor NaN (every comparison with NaN is false) -- so the length had to
        // be stopped at the parse. With ParseDouble returning null the `?? 0` fallback makes that
        // reject test fire, and the unusable rect is simply not drawn.
        var rects = RectangleRects(Parse(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 32 32\">" +
            $"<rect x=\"4\" y=\"4\" width=\"{length}\" height=\"8\" fill=\"#000000\" />" +
            "<line x1=\"0\" y1=\"0\" x2=\"32\" y2=\"32\" stroke=\"#000000\" stroke-width=\"2\" />" +
            "</svg>")).ToList();

        foreach (var rect in rects)
        {
            double.IsFinite(rect.Width).Should().BeTrue($"rect {rect} came from width=\"{length}\"");
            double.IsFinite(rect.Height).Should().BeTrue($"rect {rect} came from width=\"{length}\"");
            double.IsFinite(rect.X).Should().BeTrue($"rect {rect} came from width=\"{length}\"");
            double.IsFinite(rect.Y).Should().BeTrue($"rect {rect} came from width=\"{length}\"");
        }
    }

    [Fact]
    public void RectAttributes_FiniteLengthsStillDrawTheRect()
    {
        // Non-vacuity: the guard must not have stopped ordinary rects from being drawn at all --
        // without this, the theory above would pass over an empty sequence.
        RectangleRects(Parse(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 32 32\">" +
            "<rect x=\"4\" y=\"4\" width=\"16\" height=\"8\" fill=\"#000000\" />" +
            "</svg>")).Should().Contain(new Rect(4, 4, 16, 8));
    }
}
