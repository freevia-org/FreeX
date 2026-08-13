using FreeW.App.Presentation.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class RendererNeutralProjectionCatalogTests
{
    [Fact]
    public void EquationCatalogOwnsThirteenUniqueFreshPresetsAndTheDefaultEquation()
    {
        EquationPresetCatalog.Presets.Should().HaveCount(13);
        EquationPresetCatalog.Presets.Select(preset => preset.Kind).Should().OnlyHaveUniqueItems();
        EquationPresetCatalog.Presets.Select(preset => preset.CommandId).Should().OnlyHaveUniqueItems();
        EquationPresetCatalog.Presets.Should().OnlyContain(preset =>
            preset.CommandId.StartsWith("freew.equation-", StringComparison.Ordinal));

        var fraction = EquationPresetCatalog.Get(EquationPresetKind.Fraction);
        var first = fraction.CreateEquation();
        var second = fraction.CreateEquation();
        first.Should().NotBeSameAs(second);
        first.Runs.Should().NotBeSameAs(second.Runs);
        first.LinearText.Should().Be("a/b");
        EquationPresetCatalog.CreateDefaultEquation().LinearText.Should().Be("E = mc^2");
    }

    [Fact]
    public void TableGridProjectionNormalizesMalformedSpansAndOwnsLogicalLookup()
    {
        var row = new TableRow();
        row.Cells.Add(Cell(span: 0));
        row.Cells.Add(Cell(span: 3));
        row.Cells.Add(Cell(span: -7));

        var projected = TableGridProjection.ProjectRow(row);

        projected.Select(cell => (cell.StartColumn, cell.Span))
            .Should().Equal((0, 1), (1, 3), (4, 1));
        TableGridProjection.RowWidth(row).Should().Be(5);
        TableGridProjection.StartColumn(row, 2).Should().Be(4);
        TableGridProjection.At(row, 3)?.CellIndex.Should().Be(1);
        TableGridProjection.At(row, 4)?.CellIndex.Should().Be(2);
        TableGridProjection.At(row, 5).Should().BeNull();
        TableGridProjection.StartingAt(row, 2).Should().BeNull();
        TableGridProjection.SpanWithinWidth(projected[1], gridWidth: 2).Should().Be(1);
    }

    [Fact]
    public void TableGridProjectionUsesTheWidestLogicalRow()
    {
        var table = new Table();
        var narrow = new TableRow();
        narrow.Cells.Add(Cell(span: 1));
        var wide = new TableRow();
        wide.Cells.Add(Cell(span: 2));
        wide.Cells.Add(Cell(span: 3));
        table.Rows.Add(narrow);
        table.Rows.Add(wide);

        TableGridProjection.TableWidth(table).Should().Be(5);
    }

    [Fact]
    public void ListMarkerSequenceContinuesAcrossBodyAndBulletAndHonorsOverrides()
    {
        var planner = new DocumentListMarkerSequencePlanner();

        planner.Advance(Formatting(ListKind.Number)).MarkerText.Should().Be("1.");
        planner.Advance(Formatting(ListKind.None)).MarkerText.Should().BeNull();
        planner.Advance(Formatting(ListKind.Bullet)).MarkerText.Should().Be("\u2022");
        planner.Advance(Formatting(ListKind.Number)).MarkerText.Should().Be("2.");
        planner.Advance(Formatting(ListKind.Number, startOverride: 6)).MarkerText.Should().Be("6.");
        planner.Advance(Formatting(ListKind.Number)).MarkerText.Should().Be("7.");
    }

    [Fact]
    public void ListMarkerSequenceResetsDeeperLevelsAndExplicitResetClearsState()
    {
        var planner = new DocumentListMarkerSequencePlanner();

        planner.Advance(Formatting(ListKind.Number, level: 1)).MarkerText.Should().Be("1.");
        planner.Advance(Formatting(ListKind.Number, level: 0)).MarkerText.Should().Be("1.");
        planner.Advance(Formatting(ListKind.Number, level: 1)).MarkerText.Should().Be("1.");
        planner.Reset();
        planner.Advance(Formatting(ListKind.Number, level: 0)).MarkerText.Should().Be("1.");
    }

    [Theory]
    [InlineData(-2, "Normal")]
    [InlineData(0, "Title")]
    [InlineData(1, "Heading1")]
    [InlineData(99, "Heading6")]
    public void OutlineControllerOwnsHeadingStyleProjection(int level, string expectedStyleId)
    {
        OutlineViewController.HeadingStyleIdForLevel(level).Should().Be(expectedStyleId);
    }

    [Fact]
    public void DocumentTextRangeProjectionOwnsEligibilityAndOffsetClamping()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Table());
        document.Blocks.Add(new Paragraph("abc"));

        DocumentTextRangeProjection.TryProject(
            document,
            blockIndex: 0,
            startOffset: 0,
            endOffset: 1,
            range: out _).Should().BeFalse();
        DocumentTextRangeProjection.TryProject(
            document,
            blockIndex: 1,
            startOffset: -4,
            endOffset: 20,
            range: out var range).Should().BeTrue();
        range.Should().Be(new DocumentTextRange(
            new DocumentTextPosition(1, 0),
            new DocumentTextPosition(1, 3)));
        DocumentTextRangeProjection.TryProject(
            document,
            blockIndex: 1,
            startOffset: 0,
            endOffset: 1,
            range: out _,
            isEligible: _ => false).Should().BeFalse();
    }

    private static TableCell Cell(int span) => new() { GridSpan = span };

    private static ParagraphFormatting Formatting(
        ListKind kind,
        int level = 0,
        int? startOverride = null) =>
        new()
        {
            ListKind = kind,
            ListLevel = level,
            ListStartOverride = startOverride,
        };
}
