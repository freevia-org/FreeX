using FreeX.App.Presentation.Shapes;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Shapes;

/// <summary>
/// r164 remediation, unbounded declared quantity -- shared tier. A chord's start/end angles come from
/// DrawingML adjustment guides (<c>&lt;a:gd name="adj1" fmla="val ..."/&gt;</c>), parsed as a plain
/// double with no range check, and the sweep used to be normalized by repeated <c>+= 360</c> /
/// <c>-= 360</c>. Past roughly 1e19 a double is too coarse to change by 360 at all -- <c>x - 360 == x</c>
/// -- so a shape carrying <c>val 1e308</c> did not merely take a long time: the loop never terminated
/// (measured on a background thread that never returned, in both sweep directions).
///
/// The builder is shared, so the same crafted guide reaches FreeX, FreeW and FreeP alike.
/// </summary>
public sealed class R164_ChordAdjustmentGuideTests
{
    private static readonly LayoutRect Bounds = new(0, 0, 100, 100);

    private static ShapeGeometry BuildChord(double adj1, double adj2) =>
        ShapeGeometryBuilder.Build(
            DrawingShapeKind.Chord,
            Bounds,
            new Dictionary<string, double> { ["adj1"] = adj1, ["adj2"] = adj2 });

    [Theory]
    [InlineData(0d, 1e308)]
    [InlineData(1e308, 0d)]
    [InlineData(-1e308, 1e308)]
    [InlineData(0d, double.MaxValue)]
    public void Build_AbsurdAdjustmentGuide_ReturnsInsteadOfSpinningForever(double adj1, double adj2)
    {
        var geometry = BuildChord(adj1, adj2);

        geometry.Contours.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(double.NaN, 0d)]
    [InlineData(0d, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, double.PositiveInfinity)]
    public void Build_NonFiniteAdjustmentGuide_FallsBackToTheFullCircleInsteadOfNaNCoordinates(double adj1, double adj2)
    {
        var geometry = BuildChord(adj1, adj2);

        geometry.Contours.Should().NotBeEmpty();
        foreach (var contour in geometry.Contours)
        {
            double.IsFinite(contour.Start.X).Should().BeTrue();
            double.IsFinite(contour.Start.Y).Should().BeTrue();
        }
    }

    [Fact]
    public void Build_AnOrdinaryChord_IsUnchanged()
    {
        // Sibling/no-regression: a normal quarter-turn chord still produces its arc, and an
        // out-of-range guide still normalizes to the same sweep it did before (450 degrees -> 90).
        var ordinary = BuildChord(0, 90 * 60000);
        var wrapped = BuildChord(0, 450 * 60000);

        ordinary.Contours.Should().NotBeEmpty();
        wrapped.Contours.Should().NotBeEmpty();
        wrapped.Contours[0].Start.X.Should().BeApproximately(ordinary.Contours[0].Start.X, 0.001);
        wrapped.Contours[0].Start.Y.Should().BeApproximately(ordinary.Contours[0].Start.Y, 0.001);
    }
}
