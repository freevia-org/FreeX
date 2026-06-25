using Avalonia.Media;
using FreeX.App.Avalonia;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for <see cref="CellGradientBrush.LinearGradientPoints"/> — the pure degree→points
/// math that mirrors WPF's <c>BuildCellGradientBrush</c>. No UI thread required.
/// </summary>
public sealed class CellGradientBrushTests
{
    // ── Degree → StartPoint/EndPoint mapping ─────────────────────────────────────────────────────

    [Fact]
    public void LinearGradientPoints_Degree0_IsLeftToRight()
    {
        // degree=0: cos(0)=1, sin(0)=0 → start=(0,0.5), end=(1,0.5)
        var (start, end) = CellGradientBrush.LinearGradientPoints(0);

        start.X.Should().BeApproximately(0.0,  1e-9);
        start.Y.Should().BeApproximately(0.5,  1e-9);
        end.X.Should().BeApproximately(1.0,    1e-9);
        end.Y.Should().BeApproximately(0.5,    1e-9);
    }

    [Fact]
    public void LinearGradientPoints_Degree90_IsTopToBottom()
    {
        // degree=90: cos(90°)≈0, sin(90°)=1 → start=(0.5,0), end=(0.5,1)
        var (start, end) = CellGradientBrush.LinearGradientPoints(90);

        start.X.Should().BeApproximately(0.5,  1e-9);
        start.Y.Should().BeApproximately(0.0,  1e-9);
        end.X.Should().BeApproximately(0.5,    1e-9);
        end.Y.Should().BeApproximately(1.0,    1e-9);
    }

    [Fact]
    public void LinearGradientPoints_Degree180_IsRightToLeft()
    {
        // degree=180: cos(180°)=-1, sin(180°)≈0 → start=(1,0.5), end=(0,0.5)
        var (start, end) = CellGradientBrush.LinearGradientPoints(180);

        start.X.Should().BeApproximately(1.0,  1e-9);
        start.Y.Should().BeApproximately(0.5,  1e-9);
        end.X.Should().BeApproximately(0.0,    1e-9);
        end.Y.Should().BeApproximately(0.5,    1e-9);
    }

    [Fact]
    public void LinearGradientPoints_Degree270_IsBottomToTop()
    {
        // degree=270: cos(270°)≈0, sin(270°)=-1 → start=(0.5,1), end=(0.5,0)
        var (start, end) = CellGradientBrush.LinearGradientPoints(270);

        start.X.Should().BeApproximately(0.5,  1e-9);
        start.Y.Should().BeApproximately(1.0,  1e-9);
        end.X.Should().BeApproximately(0.5,    1e-9);
        end.Y.Should().BeApproximately(0.0,    1e-9);
    }

    [Fact]
    public void LinearGradientPoints_Degree45_IsDiagonal()
    {
        // degree=45: cos(45°)=sin(45°)=1/√2 ≈ 0.7071
        // start=(0.5-0.5*0.7071, 0.5-0.5*0.7071) ≈ (0.1464, 0.1464)
        // end  =(0.5+0.5*0.7071, 0.5+0.5*0.7071) ≈ (0.8536, 0.8536)
        var (start, end) = CellGradientBrush.LinearGradientPoints(45);

        var halfRoot2 = 0.5 * Math.Sqrt(2.0) / 2.0; // 0.5 * cos(45)
        var expected = 0.5 - halfRoot2;

        start.X.Should().BeApproximately(expected, 1e-9);
        start.Y.Should().BeApproximately(expected, 1e-9);
        end.X.Should().BeApproximately(1.0 - expected, 1e-9);
        end.Y.Should().BeApproximately(1.0 - expected, 1e-9);
    }

    // ── Start/End symmetry: start + end = (1,1) always ──────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(135)]
    [InlineData(180)]
    [InlineData(225)]
    [InlineData(270)]
    [InlineData(315)]
    public void LinearGradientPoints_StartPlusEnd_IsCentreTimesTwo(double degree)
    {
        var (start, end) = CellGradientBrush.LinearGradientPoints(degree);

        // start + end = (2*0.5, 2*0.5) = (1, 1)
        (start.X + end.X).Should().BeApproximately(1.0, 1e-9);
        (start.Y + end.Y).Should().BeApproximately(1.0, 1e-9);
    }

    // ── Stop ordering: Build returns stops ordered by position ───────────────────────────────────

    [Fact]
    public void Build_LinearGradient_ReturnsLinearGradientBrush_WithStopsInOrder()
    {
        // Arrange — 3-stop gradient with stops given out of order
        var gradient = new CellGradientFill
        {
            Type   = CellGradientFillType.Linear,
            Degree = 0,
            Stops  =
            [
                new CellGradientStop(1.0, new CellColor(0, 0, 255)),   // blue at end
                new CellGradientStop(0.5, new CellColor(255, 255, 0)), // yellow at mid
                new CellGradientStop(0.0, new CellColor(255, 0, 0)),   // red at start
            ],
        };

        // Act
        var brush = CellGradientBrush.Build(gradient);

        // Assert
        brush.Should().BeOfType<LinearGradientBrush>();
        var lgb = (LinearGradientBrush)brush!;
        lgb.GradientStops.Should().HaveCount(3);

        // Stops are ordered by position ascending after Build
        lgb.GradientStops[0].Offset.Should().BeApproximately(0.0, 1e-9);
        lgb.GradientStops[1].Offset.Should().BeApproximately(0.5, 1e-9);
        lgb.GradientStops[2].Offset.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Build_PathGradient_ReturnsRadialGradientBrush()
    {
        var gradient = new CellGradientFill
        {
            Type   = CellGradientFillType.Path,
            Left   = 0.1,
            Right  = 0.1,
            Top    = 0.1,
            Bottom = 0.1,
            Stops  =
            [
                new CellGradientStop(0.0, new CellColor(255, 255, 255)),
                new CellGradientStop(1.0, new CellColor(0, 0, 0)),
            ],
        };

        var brush = CellGradientBrush.Build(gradient);

        brush.Should().BeOfType<RadialGradientBrush>();
    }

    [Fact]
    public void Build_EmptyStops_ReturnsNull()
    {
        var gradient = new CellGradientFill { Stops = [] };

        var brush = CellGradientBrush.Build(gradient);

        brush.Should().BeNull();
    }
}
