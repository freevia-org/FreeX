using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class HeaderFooterTableLayoutPlannerTests
{
    private static Table TwoCells()
    {
        var table = Table.Create(1, 2);
        table.ColumnWidthsPt.AddRange([100, 200]);
        table.DefaultCellMargins = new TableCellMargins(3, 7, 5, 9);
        return table;
    }

    [Fact]
    public void LegacyGridAndPaddingPlaceTheFirstTextEdgeAtTheIndent()
    {
        var table = TwoCells();
        table.AutoFit = AutoFitMode.Window;
        table.PreferredWidthPercent = 100;
        table.Rows[0].Cells[1].VerticalAlignment = TableCellVerticalAlignment.Bottom;
        var paragraph = table.Rows[0].Cells[0].Paragraphs[0];
        var plan = HeaderFooterTableLayoutPlanner.Build(table, 500, true,
            [new(0, 0, 40), new(0, 1, 24)], dipPerPoint: 1);

        plan.WidthDip.Should().BeApproximately(300, 1e-9, "the authored legacy grid includes the outdented padding");
        plan.LeftOffsetDip.Should().Be(-7);
        plan.ColumnWidthsDip[0].Should().BeApproximately(100, 1e-9);
        plan.ColumnWidthsDip[1].Should().BeApproximately(200, 1e-9);
        plan.RowHeightsDip.Should().Equal(48);
        plan.Cells[0].ContentBounds.XDip.Should().Be(0);
        plan.Cells[0].ContentBounds.WidthDip.Should().BeApproximately(84, 1e-9);
        plan.Cells[1].ContentOffsetDip.Should().Be(16);
        plan.Cells[1].ContentBounds.YDip.Should().Be(3);
        (plan.Cells[1].ContentBounds.YDip + plan.Cells[1].ContentOffsetDip + 24).Should().Be(43);
        plan.Cells[0].SourceCell.Should().BeSameAs(table.Rows[0].Cells[0]);
        plan.Cells[0].SourceCell.Paragraphs[0].Should().BeSameAs(paragraph);
        table.ColumnWidthsPt.Should().Equal(100, 200);
    }

    [Fact]
    public void ModernPercentageTableUsesItsAuthoredShareWithoutLegacyOutdent()
    {
        var table = TwoCells();
        table.AutoFit = AutoFitMode.Window;
        table.PreferredWidthPercent = 50;
        var plan = HeaderFooterTableLayoutPlanner.Build(table, 400, false, dipPerPoint: 1);

        plan.WidthDip.Should().BeApproximately(200, .001);
        plan.LeftOffsetDip.Should().Be(0);
        plan.ColumnWidthsDip[0].Should().BeApproximately(200d / 3, .001);
        plan.Cells[0].ContentBounds.XDip.Should().Be(7);
    }

    [Theory]
    [InlineData(TableAlignment.Left, 15)]
    [InlineData(TableAlignment.Center, 97.5)]
    [InlineData(TableAlignment.Right, 180)]
    public void PreferredWidthAndIndentComposeWithAlignment(TableAlignment alignment, double expectedLeft)
    {
        var table = TwoCells();
        table.PreferredWidthPt = 120;
        table.IndentFromLeftPt = 15;
        table.Alignment = alignment;
        var plan = HeaderFooterTableLayoutPlanner.Build(table, 300, false, dipPerPoint: 1);
        plan.WidthDip.Should().Be(120);
        plan.LeftOffsetDip.Should().Be(expectedLeft);
    }

    [Fact]
    public void GridBeforeAndHorizontalSpanKeepSourceCellAddressing()
    {
        var table = Table.Create(1, 1);
        table.ColumnWidthsPt.AddRange([50, 60, 70]);
        table.Rows[0].GridBefore = 1;
        table.Rows[0].Cells[0].GridSpan = 2;
        table.DefaultCellMargins = new TableCellMargins(0, 0, 0, 0);
        var plan = HeaderFooterTableLayoutPlanner.Build(table, 300, false, [new(0, 0, 20)], 1);
        var cell = plan.Cells.Should().ContainSingle().Which;
        cell.GridColumn.Should().Be(1);
        cell.GridSpan.Should().Be(2);
        cell.Bounds.XDip.Should().BeApproximately(50, 1e-9);
        cell.Bounds.WidthDip.Should().BeApproximately(130, 1e-9);
        cell.SourceCell.Should().BeSameAs(table.Rows[0].Cells[0]);
    }

    [Theory]
    [InlineData(TableRowHeightRule.AtLeast, 60)]
    [InlineData(TableRowHeightRule.Exact, 30)]
    public void ExactRowsClipWhileMinimumRowsGrow(TableRowHeightRule rule, double height)
    {
        var table = Table.Create(1, 1);
        table.DefaultCellMargins = new TableCellMargins(0, 0, 0, 0);
        table.Rows[0].HeightPt = 30;
        table.Rows[0].HeightRule = rule;
        table.Rows[0].Cells[0].VerticalAlignment = TableCellVerticalAlignment.Bottom;
        var plan = HeaderFooterTableLayoutPlanner.Build(table, 100, false, [new(0, 0, 60)], 1);
        plan.HeightDip.Should().Be(height);
        plan.Cells[0].ContentBounds.HeightDip.Should().Be(height);
        plan.Cells[0].ContentOffsetDip.Should().Be(0, "overflow never gets a negative alignment offset");
    }

    [Fact]
    public void VerticalMergeReservesItsCombinedRegionAndDoesNotPaintContinuationTwice()
    {
        var table = Table.Create(2, 2);
        table.DefaultCellMargins = new TableCellMargins(4, 0, 6, 0);
        table.Rows[0].Cells[0].VerticalMerge = VerticalMergeState.Restart;
        table.Rows[1].Cells[0].VerticalMerge = VerticalMergeState.Continue;
        var plan = HeaderFooterTableLayoutPlanner.Build(table, 200, false,
            [new(0, 0, 100), new(0, 1, 10), new(1, 1, 20)], 1);

        plan.Cells.Should().HaveCount(3);
        plan.RowHeightsDip.Should().Equal(20, 90);
        var merged = plan.Cells.Single(cell => cell.RowIndex == 0 && cell.CellIndex == 0);
        merged.RowSpan.Should().Be(2);
        merged.Bounds.HeightDip.Should().Be(110);
        merged.ContentBounds.HeightDip.Should().Be(100);
    }

    [Fact]
    public void AllExactRowsStayAuthoredEvenWhenMergedContentOverflows()
    {
        var table = Table.Create(2, 1);
        table.DefaultCellMargins = new TableCellMargins(0, 0, 0, 0);
        table.Rows[0].Cells[0].VerticalMerge = VerticalMergeState.Restart;
        table.Rows[1].Cells[0].VerticalMerge = VerticalMergeState.Continue;
        table.Rows[0].HeightPt = 20;
        table.Rows[1].HeightPt = 30;
        foreach (var row in table.Rows) row.HeightRule = TableRowHeightRule.Exact;
        var plan = HeaderFooterTableLayoutPlanner.Build(table, 200, false, [new(0, 0, 100)], 1);
        plan.RowHeightsDip.Should().Equal(20, 30);
        plan.Cells.Should().ContainSingle().Which.Bounds.HeightDip.Should().Be(50);
    }

    [Fact]
    public void ExplicitRemovalOverridesInheritedEdgesAndStylesStayDistinct()
    {
        var table = TwoCells();
        table.Borders = new TableBorders { Top = new(), Bottom = new(), InsideVertical = new() };
        var source = table.Rows[0].Cells[0];
        source.ShadingColorHex = "943634";
        source.Borders = new CellBorders
        {
            NoTop = true,
            Bottom = new(BorderLineStyle.Double, "#123456", 2),
            Right = new(BorderLineStyle.Dashed, "#ABCDEF", 1),
        };
        var plan = HeaderFooterTableLayoutPlanner.Build(table, 300, false, [new(0, 0, 20)], 1);
        var cell = plan.Cells[0];
        cell.Borders.Edge(TableCellBorderVisualEdge.Top).IsVisible.Should().BeFalse();
        cell.Borders.Edge(TableCellBorderVisualEdge.Bottom).Style.Should().Be(BorderLineStyle.Double);
        cell.Borders.Edge(TableCellBorderVisualEdge.Right).Style.Should().Be(BorderLineStyle.Dashed);
        cell.Fill.EffectiveFillHex!.TrimStart('#').Should().Be("943634");
        source.Borders.NoTop.Should().BeTrue();
    }

    [Fact]
    public void PerCellZeroMarginOverridesTheTableDefaultWithoutChangingTheOtherCell()
    {
        var table = TwoCells();
        table.Rows[0].Cells[0].Margins = new TableCellMargins(0, 0, 0, 0);
        var plan = HeaderFooterTableLayoutPlanner.Build(table, 300, false,
            [new(0, 0, 20), new(0, 1, 20)], 1);
        plan.Cells[0].ContentBounds.XDip.Should().Be(0);
        plan.Cells[0].ContentBounds.WidthDip.Should().BeApproximately(100, 1e-9);
        plan.Cells[1].ContentBounds.XDip.Should().BeApproximately(107, 1e-9);
        plan.Cells[1].ContentBounds.WidthDip.Should().BeApproximately(184, 1e-9);
    }

    [Fact]
    public void RightToLeftProjectsTheGridWithoutReorderingTheSourceCells()
    {
        var table = TwoCells();
        table.RightToLeft = true;
        table.DefaultCellMargins = new(0, 0, 0, 0);
        var plan = HeaderFooterTableLayoutPlanner.Build(table, 300, false, dipPerPoint: 1);
        plan.Cells[0].Bounds.XDip.Should().BeApproximately(200, 1e-9);
        plan.Cells[1].Bounds.XDip.Should().BeApproximately(0, 1e-9);
        plan.Cells[0].SourceCell.Should().BeSameAs(table.Rows[0].Cells[0]);
    }

    [Fact]
    public void NegativeAuthoredIndentRemainsSignedAndComposesWithLegacyPadding()
    {
        var table = TwoCells();
        table.IndentFromLeftPt = -12;
        var current = HeaderFooterTableLayoutPlanner.Build(table, 300, false, dipPerPoint: 1);
        var legacy = HeaderFooterTableLayoutPlanner.Build(table, 300, true, dipPerPoint: 1);
        current.LeftOffsetDip.Should().Be(-12);
        legacy.LeftOffsetDip.Should().Be(-19);
        legacy.Cells[0].ContentBounds.XDip.Should().Be(-12);
        table.IndentFromLeftPt.Should().Be(-12);
    }

    [Fact]
    public void LegacyExplicitDxaWidthIsNotReplacedByTheGrid()
    {
        var table = TwoCells();
        table.AutoFit = AutoFitMode.Fixed;
        table.PreferredWidthPt = 120;
        var plan = HeaderFooterTableLayoutPlanner.Build(table, 300, true, dipPerPoint: 1);
        plan.WidthDip.Should().BeApproximately(120, 1e-9);
        plan.LeftOffsetDip.Should().Be(-7);
    }
    [Theory]
    [InlineData(TableAlignment.Center, -25)]
    [InlineData(TableAlignment.Right, -50)]
    public void OversizedAlignedGridUsesSignedOverflow(TableAlignment alignment, double expected)
    {
        var table = TwoCells();
        table.Alignment = alignment;
        var plan = HeaderFooterTableLayoutPlanner.Build(table, 250, true, dipPerPoint: 1);
        plan.WidthDip.Should().BeApproximately(300, 1e-9);
        plan.LeftOffsetDip.Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void MergedBottomEdgeComesFromTheFinalContinuationAndHonorsRemoval()
    {
        var table = Table.Create(2, 1);
        var first = table.Rows[0].Cells[0]; var last = table.Rows[1].Cells[0];
        first.VerticalMerge = VerticalMergeState.Restart; last.VerticalMerge = VerticalMergeState.Continue;
        first.Borders = new CellBorders { Bottom = new(BorderLineStyle.Single, "#123456", 1) };
        last.Borders = new CellBorders { Bottom = new(BorderLineStyle.Double, "#654321", 2) };
        var plan = HeaderFooterTableLayoutPlanner.Build(table, 100, false, dipPerPoint: 1);
        plan.Cells.Single().Borders.Edge(TableCellBorderVisualEdge.Bottom).Style.Should().Be(BorderLineStyle.Double);
        last.Borders = new CellBorders { NoBottom = true };
        HeaderFooterTableLayoutPlanner.Build(table, 100, false, dipPerPoint: 1).Cells.Single().Borders
            .Edge(TableCellBorderVisualEdge.Bottom).IsVisible.Should().BeFalse();
    }

}
