using Free.Shared.Drawing;
using FluentAssertions;

namespace Free.Shared.Pdf.Tests;

public sealed class DrawingMlPatternFillPlannerTests
{
    public static TheoryData<string?, DrawingMlPatternFillFamily> PresetCases => new()
    {
        { "horz", DrawingMlPatternFillFamily.Horizontal },
        { "ltHorz", DrawingMlPatternFillFamily.Horizontal },
        { "medGray", DrawingMlPatternFillFamily.Horizontal },
        { "dkHorz", DrawingMlPatternFillFamily.Horizontal },
        { "pct5", DrawingMlPatternFillFamily.Horizontal },
        { "pct10", DrawingMlPatternFillFamily.Horizontal },
        { "pct20", DrawingMlPatternFillFamily.Horizontal },
        { "vert", DrawingMlPatternFillFamily.Vertical },
        { "ltVert", DrawingMlPatternFillFamily.Vertical },
        { "dkVert", DrawingMlPatternFillFamily.Vertical },
        { "pct25", DrawingMlPatternFillFamily.Vertical },
        { "pct30", DrawingMlPatternFillFamily.Vertical },
        { "diagStripe", DrawingMlPatternFillFamily.DownDiagonal },
        { "ltDnDiag", DrawingMlPatternFillFamily.DownDiagonal },
        { "dkDnDiag", DrawingMlPatternFillFamily.DownDiagonal },
        { "dnDiag", DrawingMlPatternFillFamily.DownDiagonal },
        { "pct50", DrawingMlPatternFillFamily.DownDiagonal },
        { "ltUpDiag", DrawingMlPatternFillFamily.UpDiagonal },
        { "dkUpDiag", DrawingMlPatternFillFamily.UpDiagonal },
        { "upDiag", DrawingMlPatternFillFamily.UpDiagonal },
        { "pct60", DrawingMlPatternFillFamily.UpDiagonal },
        { "pct70", DrawingMlPatternFillFamily.UpDiagonal },
        { "cross", DrawingMlPatternFillFamily.Cross },
        { "ltGrid", DrawingMlPatternFillFamily.Cross },
        { "dkGrid", DrawingMlPatternFillFamily.Cross },
        { "pct75", DrawingMlPatternFillFamily.Cross },
        { "pct80", DrawingMlPatternFillFamily.Cross },
        { "dotGrid", DrawingMlPatternFillFamily.Dot },
        { "dotDmnd", DrawingMlPatternFillFamily.Dot },
        { "smGrid", DrawingMlPatternFillFamily.Dot },
        { "pct90", DrawingMlPatternFillFamily.Dot },
        { "horzBrick", DrawingMlPatternFillFamily.Brick },
        { "divot", DrawingMlPatternFillFamily.Brick },
        { "weave", DrawingMlPatternFillFamily.Brick },
        { "diagCross", DrawingMlPatternFillFamily.DiagonalCross },
        { "ltDiagCross", DrawingMlPatternFillFamily.DiagonalCross },
        { "dkDiagCross", DrawingMlPatternFillFamily.DiagonalCross },
        { "smDot", DrawingMlPatternFillFamily.DiagonalCross },
        { "pct40", DrawingMlPatternFillFamily.DiagonalCross },
        { "unknown", DrawingMlPatternFillFamily.DiagonalCross },
        { "", DrawingMlPatternFillFamily.DiagonalCross },
        { null, DrawingMlPatternFillFamily.DiagonalCross },
    };

    public static TheoryData<DrawingMlPatternFillFamily, double, double, DrawingMlPatternFillPrimitive[]> RecipeCases => new()
    {
        {
            DrawingMlPatternFillFamily.Horizontal,
            8,
            8,
            [Background(8, 8), Line(0, 4, 8, 4)]
        },
        {
            DrawingMlPatternFillFamily.Vertical,
            8,
            8,
            [Background(8, 8), Line(4, 0, 4, 8)]
        },
        {
            DrawingMlPatternFillFamily.DownDiagonal,
            8,
            8,
            [Background(8, 8), Line(0, 0, 8, 8)]
        },
        {
            DrawingMlPatternFillFamily.UpDiagonal,
            8,
            8,
            [Background(8, 8), Line(0, 8, 8, 0)]
        },
        {
            DrawingMlPatternFillFamily.Cross,
            8,
            8,
            [Background(8, 8), Line(0, 4, 8, 4), Line(4, 0, 4, 8)]
        },
        {
            DrawingMlPatternFillFamily.Dot,
            8,
            8,
            [Background(8, 8), new DrawingMlPatternFillEllipse(4, 4, 1, 1, DrawingMlPatternFillColorRole.Foreground)]
        },
        {
            DrawingMlPatternFillFamily.Brick,
            12,
            8,
            [
                Background(12, 8),
                Line(0, 0, 12, 0, 0.5),
                Line(6, 4, 12, 4, 0.5),
                Line(0, 4, 3, 4, 0.5),
                Line(6, 0, 6, 4, 0.5),
                Line(0, 4, 0, 8, 0.5),
                Line(12, 4, 12, 8, 0.5),
            ]
        },
        {
            DrawingMlPatternFillFamily.DiagonalCross,
            8,
            8,
            [Background(8, 8), Line(0, 0, 8, 8), Line(8, 0, 0, 8)]
        },
    };

    [Theory]
    [MemberData(nameof(PresetCases))]
    public void Classify_CoversCanonicalPresetMapping(
        string? preset,
        DrawingMlPatternFillFamily expected)
    {
        DrawingMlPatternFillPlanner.Classify(preset).Should().Be(expected);
        DrawingMlPatternFillPlanner.Plan(preset).Family.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(RecipeCases))]
    public void RecipeFor_DefinesCompleteCanonicalTile(
        DrawingMlPatternFillFamily family,
        double expectedWidth,
        double expectedHeight,
        DrawingMlPatternFillPrimitive[] expectedPrimitives)
    {
        var recipe = DrawingMlPatternFillPlanner.RecipeFor(family);

        recipe.Family.Should().Be(family);
        recipe.TileWidth.Should().Be(expectedWidth);
        recipe.TileHeight.Should().Be(expectedHeight);
        recipe.Primitives.Should().Equal(expectedPrimitives);
    }

    [Fact]
    public void DriftCases_UseEstablishedWpfPdfFamilies()
    {
        DrawingMlPatternFillPlanner.Classify("horzBrick").Should().Be(DrawingMlPatternFillFamily.Brick);
        DrawingMlPatternFillPlanner.Classify("smDot").Should().Be(DrawingMlPatternFillFamily.DiagonalCross);
        DrawingMlPatternFillPlanner.Classify("pct40").Should().Be(DrawingMlPatternFillFamily.DiagonalCross);
    }

    private static DrawingMlPatternFillRectangle Background(double width, double height) =>
        new(0, 0, width, height, DrawingMlPatternFillColorRole.Background);

    private static DrawingMlPatternFillLine Line(
        double startX,
        double startY,
        double endX,
        double endY,
        double strokeWidth = 1) =>
        new(
            new DrawingMlPatternFillPoint(startX, startY),
            new DrawingMlPatternFillPoint(endX, endY),
            strokeWidth,
            DrawingMlPatternFillColorRole.Foreground);
}
