using FluentAssertions;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Sparklines;

public sealed class SparklineAxisLinePlannerTests
{
    private static readonly LayoutRect Cell = new(4, 6, 100, 40);

    [Theory]
    [InlineData(SparklineKind.Column, 46)]
    [InlineData(SparklineKind.WinLoss, 26)]
    public void ResolveY_PositiveData_UsesKindSpecificBaseline(SparklineKind kind, double expected)
    {
        SparklineAxisLinePlanner.ResolveY(kind, [5, 10, 3], Cell)
            .Should().Be(expected);
    }

    [Fact]
    public void ResolveY_NegativeColumnData_UsesTopBaseline()
    {
        SparklineAxisLinePlanner.ResolveY(SparklineKind.Column, [-5, -10, -3], Cell)
            .Should().Be(Cell.Top);
    }

    [Fact]
    public void ResolveY_MixedColumnData_UsesMidpoint()
    {
        SparklineAxisLinePlanner.ResolveY(SparklineKind.Column, [5, -3], Cell)
            .Should().Be(Cell.Top + (Cell.Height / 2));
    }

    [Fact]
    public void ResolveY_LineRangeOutsideZero_ReturnsNull()
    {
        SparklineAxisLinePlanner.ResolveY(SparklineKind.Line, [9, 12, 15, 11], Cell)
            .Should().BeNull();
    }

    [Fact]
    public void ResolveY_AsymmetricLineRange_UsesActualZeroPosition()
    {
        SparklineAxisLinePlanner.ResolveY(SparklineKind.Line, [-2, 8, 3], Cell)
            .Should().BeApproximately(Cell.Bottom - (0.2 * Cell.Height), 0.001);
    }

    [Fact]
    public void ResolveY_LineOverrides_ControlVisibilityAndPosition()
    {
        SparklineAxisLinePlanner.ResolveY(
                SparklineKind.Line,
                [4, 8],
                Cell,
                overrideMinimum: -4,
                overrideMaximum: 12)
            .Should().BeApproximately(Cell.Bottom - (0.25 * Cell.Height), 0.001);

        SparklineAxisLinePlanner.ResolveY(
                SparklineKind.Line,
                [-4, 8],
                Cell,
                overrideMinimum: 2,
                overrideMaximum: 12)
            .Should().BeNull();
    }

    [Fact]
    public void ResolveY_LineIgnoresNonFiniteValuesAndRequiresFiniteData()
    {
        SparklineAxisLinePlanner.ResolveY(SparklineKind.Line, [double.NaN, -1, double.PositiveInfinity, 3], Cell)
            .Should().BeApproximately(Cell.Bottom - (0.25 * Cell.Height), 0.001);

        SparklineAxisLinePlanner.ResolveY(SparklineKind.Line, [double.NaN, double.PositiveInfinity], Cell)
            .Should().BeNull();
    }

    [Fact]
    public void ResolveY_DegenerateBounds_ReturnsNull()
    {
        SparklineAxisLinePlanner.ResolveY(SparklineKind.Column, [1], new LayoutRect(0, 0, 0, 20))
            .Should().BeNull();
    }

    [Fact]
    public void BothRenderers_ConsumeSharedAxisPlanner()
    {
        var wpfSource = File.ReadAllText(Path.Combine(
            RepositoryFileLocator.FindDirectory("src", "FreeX.App.UI"),
            "GridView.Overlays.Sparklines.cs"));
        var avaloniaSource = File.ReadAllText(Path.Combine(
            RepositoryFileLocator.FindDirectory("src", "FreeX.App.Avalonia"),
            "SparklineCellPanel.cs"));

        wpfSource.Should().Contain("SparklineAxisLinePlanner.ResolveY(");
        avaloniaSource.Should().Contain("SparklineAxisLinePlanner.ResolveY(");
        avaloniaSource.Should().NotContain("var y = rect.Top + (rect.Height / 2);");
    }
}
