using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;

using FreeX.App.Avalonia.Charts;
using FreeX.Core.Model;

using FluentAssertions;

using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaRect = Avalonia.Controls.Shapes.Rectangle;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-66 fix bucket app-markers regression tests (R66-meta-2 — the rendering-side twin of R65's
/// io-chart-enums marker-style fix): the <see cref="ChartMarkerStyle"/> members added in R65 (X,
/// Star, Plus, Dot, Dash, Auto) previously all fell through
/// <see cref="AvaloniaChartRenderer.BuildMarker"/>'s switch to the "Circle" default arm, so a chart
/// with e.g. an X or Star marker rendered as a plain circle. These tests pin distinct geometry for
/// the newly-added members and confirm the pre-existing members still render unchanged.
/// Uses the Avalonia headless platform (via the shared <see cref="RibbonHeadlessApp"/> session)
/// because Path/StreamGeometry needs <c>IPlatformRenderInterface</c> to construct/measure.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaChartMarkerGeometryTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── New line-based markers (X/Star/Plus/Dash) must not collapse to a circle ─────────────────

    [Theory]
    [InlineData(ChartMarkerStyle.X)]
    [InlineData(ChartMarkerStyle.Star)]
    [InlineData(ChartMarkerStyle.Plus)]
    [InlineData(ChartMarkerStyle.Dash)]
    public async Task BuildMarker_NewLineStyles_ReturnPathNotEllipse(ChartMarkerStyle style)
    {
        // Before the fix: X/Star/Plus/Dash all fell through to the default arm and produced a
        // Circle-shaped Ellipse instead of distinct line-based geometry.
        await Session.Dispatch(() =>
        {
            var control = AvaloniaChartRenderer.BuildMarker(style, 50, 50, Brushes.Blue, Brushes.Red);
            control.Should().BeOfType<AvaloniaPath>(
                $"{style} marker must produce distinct line geometry, not the Circle fallback");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BuildMarker_Dash_GeometryIsFlatHorizontalLine()
    {
        // Distinguishes Dash from X/Plus/Star: a horizontal-only line segment has zero height,
        // whereas the others span both axes.
        await Session.Dispatch(() =>
        {
            var control = AvaloniaChartRenderer.BuildMarker(ChartMarkerStyle.Dash, 50, 50, Brushes.Blue, Brushes.Red);
            var path = control.Should().BeOfType<AvaloniaPath>().Subject;
            var bounds = path.Data!.Bounds;
            bounds.Height.Should().Be(0, "Dash is a single horizontal line segment");
            bounds.Width.Should().BeGreaterThan(0, "Dash must still have horizontal extent");
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(ChartMarkerStyle.X)]
    [InlineData(ChartMarkerStyle.Plus)]
    [InlineData(ChartMarkerStyle.Star)]
    public async Task BuildMarker_XPlusStar_GeometrySpansBothAxesUnlikeDash(ChartMarkerStyle style)
    {
        await Session.Dispatch(() =>
        {
            var control = AvaloniaChartRenderer.BuildMarker(style, 50, 50, Brushes.Blue, Brushes.Red);
            var path = control.Should().BeOfType<AvaloniaPath>().Subject;
            var bounds = path.Data!.Bounds;
            bounds.Height.Should().BeGreaterThan(0, $"{style} must span the vertical axis, unlike Dash");
            bounds.Width.Should().BeGreaterThan(0, $"{style} must span the horizontal axis");
        }, CancellationToken.None);
    }

    // ── Dot must be a smaller circle, not the same size as the default Circle marker ────────────

    [Fact]
    public async Task BuildMarker_Dot_ReturnsSmallerEllipseThanCircle()
    {
        // Before the fix: Dot fell through to the default arm and produced the same full-size
        // Ellipse as Circle. Dot must now be visibly smaller.
        await Session.Dispatch(() =>
        {
            var dot = AvaloniaChartRenderer.BuildMarker(ChartMarkerStyle.Dot, 50, 50, Brushes.Blue, Brushes.Red);
            var circle = AvaloniaChartRenderer.BuildMarker(ChartMarkerStyle.Circle, 50, 50, Brushes.Blue, Brushes.Red);

            var dotEllipse = dot.Should().BeOfType<AvaloniaEllipse>().Subject;
            var circleEllipse = circle.Should().BeOfType<AvaloniaEllipse>().Subject;

            dotEllipse.Width.Should().BeLessThan(circleEllipse.Width,
                "Dot must render smaller than the default Circle marker");
        }, CancellationToken.None);
    }

    // ── Sibling no-regression tests: pre-existing mappings + Auto's Circle fallback ─────────────

    [Fact]
    public async Task BuildMarker_Auto_StillReturnsFullSizeCircleEllipse()
    {
        // Auto is documented to fall back to the automatic/default marker (Circle); confirm the new
        // explicit switch arms didn't disturb that.
        await Session.Dispatch(() =>
        {
            var auto = AvaloniaChartRenderer.BuildMarker(ChartMarkerStyle.Auto, 50, 50, Brushes.Blue, Brushes.Red);
            var circle = AvaloniaChartRenderer.BuildMarker(ChartMarkerStyle.Circle, 50, 50, Brushes.Blue, Brushes.Red);

            var autoEllipse = auto.Should().BeOfType<AvaloniaEllipse>().Subject;
            var circleEllipse = circle.Should().BeOfType<AvaloniaEllipse>().Subject;

            autoEllipse.Width.Should().Be(circleEllipse.Width,
                "Auto must use the same automatic/default marker as Circle");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BuildMarker_Square_StillReturnsRectangleUnchanged()
    {
        await Session.Dispatch(() =>
        {
            var control = AvaloniaChartRenderer.BuildMarker(ChartMarkerStyle.Square, 50, 50, Brushes.Blue, Brushes.Red);
            control.Should().BeOfType<AvaloniaRect>();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BuildMarker_None_StillReturnsNull()
    {
        await Session.Dispatch(() =>
        {
            var control = AvaloniaChartRenderer.BuildMarker(ChartMarkerStyle.None, 50, 50, Brushes.Blue, Brushes.Red);
            control.Should().BeNull();
        }, CancellationToken.None);
    }
}
