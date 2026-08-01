using Avalonia;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class AvaloniaInlineTableLayoutPlannerTests
{
    [Fact]
    public void AuthoredCellInsetsAndBottomAnchorShapeInlineTableTextArea()
    {
        var cell = new TableCell
        {
            InsetLeftPt = 6,
            InsetTopPt = 12,
            InsetRightPt = 18,
            InsetBottomPt = 24,
            Anchor = TableCellAnchor.Bottom,
        };

        var plan = AvaloniaInlineTableLayoutPlanner.PlanCellText(
            cell,
            new Rect(10, 20, 100, 80),
            measuredTextHeight: 10);

        plan.Area.Should().Be(new Rect(18, 36, 68, 32));
        plan.Origin.Should().Be(new Point(18, 58));
    }

    [Fact]
    public void UnspecifiedInsetsKeepExistingInlineTableInsetAndTopAnchor()
    {
        var plan = AvaloniaInlineTableLayoutPlanner.PlanCellText(
            new TableCell(),
            new Rect(10, 20, 40, 30),
            measuredTextHeight: 100);

        plan.Area.Should().Be(new Rect(12, 22, 36, 26));
        plan.Origin.Should().Be(new Point(12, 22));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(TableRowHorizontalAlignment.Left, 0)]
    [InlineData(TableRowHorizontalAlignment.Center, 30)]
    [InlineData(TableRowHorizontalAlignment.Right, 60)]
    public void RowHorizontalAlignmentOffsetsInlineTableWithinAvailableWidth(
        TableRowHorizontalAlignment? alignment,
        double expectedOffset)
    {
        AvaloniaInlineTableLayoutPlanner.GetHorizontalOffset(alignment, 120, 60)
            .Should().Be(expectedOffset);
    }

    [Fact]
    public void GridSpan_UsesAnchorBoundsForCoveredColumnAndHit()
    {
        var table = MakeTable(1, 3);
        table.Rows[0].Cells[0].GridSpan = 2;
        table.Rows[0].Cells[1].HMerge = true;

        var plan = AvaloniaInlineTableGridLayout.Create(
            table,
            new Point(10, 20),
            availableWidth: 144);

        var anchor = plan.GetCell(0, 0);
        var continuation = plan.GetCell(0, 1);
        anchor.Should().NotBeNull();
        continuation.Should().NotBeNull();
        continuation!.RowIndex.Should().Be(0);
        continuation.ColumnIndex.Should().Be(0);
        anchor!.Bounds.Should().Be(new Rect(10, 20, 96, 24));
        plan.HitTest(new Point(70, 30)).Should().Be(anchor);
    }

    [Fact]
    public void RowSpan_UsesAnchorBoundsForCoveredRowAndHit()
    {
        var table = MakeTable(2, 2);
        table.Rows[0].Cells[0].RowSpan = 2;
        table.Rows[1].Cells[0].VMerge = true;

        var plan = AvaloniaInlineTableGridLayout.Create(
            table,
            new Point(10, 20),
            availableWidth: 96);

        var anchor = plan.GetCell(0, 0);
        var continuation = plan.GetCell(1, 0);
        anchor.Should().NotBeNull();
        continuation.Should().NotBeNull();
        continuation!.RowIndex.Should().Be(0);
        continuation.ColumnIndex.Should().Be(0);
        anchor!.Bounds.Should().Be(new Rect(10, 20, 48, 48));
        plan.HitTest(new Point(30, 55)).Should().Be(anchor);
    }

    [Fact]
    public void CompactImportedGridSpan_StillOwnsCoveredColumn()
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(457200);
        table.ColumnWidthsEmu.Add(457200);
        table.ColumnWidthsEmu.Add(457200);
        table.Rows.Add(new TableRow
        {
            HeightEmu = 228600,
            Cells =
            {
                new TableCell { GridSpan = 2 },
                new TableCell(),
            },
        });

        var plan = AvaloniaInlineTableGridLayout.Create(
            table,
            new Point(0, 0),
            availableWidth: 144);

        plan.GetCell(0, 1)!.ColumnIndex.Should().Be(0);
        plan.GetCell(0, 2)!.ColumnIndex.Should().Be(2);
        plan.HitTest(new Point(70, 12))!.ColumnIndex.Should().Be(0);
    }

    [Fact]
    public void Cells_EnumeratesEachLogicalAnchorOnceAndKeepsSourceCellIndex()
    {
        var table = new TableShape();
        for (int column = 0; column < 3; column++)
            table.ColumnWidthsEmu.Add(457200);

        table.Rows.Add(new TableRow
        {
            HeightEmu = 228600,
            Cells =
            {
                new TableCell { GridSpan = 2, RowSpan = 2 },
                new TableCell { HMerge = true },
                new TableCell(),
            },
        });
        table.Rows.Add(new TableRow
        {
            HeightEmu = 228600,
            Cells =
            {
                new TableCell { VMerge = true },
                new TableCell { VMerge = true },
                new TableCell(),
            },
        });

        var plan = AvaloniaInlineTableGridLayout.Create(
            table,
            new Point(0, 0),
            availableWidth: 144);

        plan.Cells.Select(cell => (cell.RowIndex, cell.ColumnIndex, cell.SourceCellIndex))
            .Should().Equal(
                (0, 0, 0),
                (0, 2, 2),
                (1, 2, 2));
        plan.GetCell(0, 1).Should().Be(plan.Cells[0]);
        plan.GetCell(1, 0).Should().Be(plan.Cells[0]);
    }

    private static TableShape MakeTable(int rows, int columns)
    {
        var table = new TableShape();
        for (int column = 0; column < columns; column++)
            table.ColumnWidthsEmu.Add(457200);
        for (int row = 0; row < rows; row++)
        {
            var modelRow = new TableRow { HeightEmu = 228600 };
            for (int column = 0; column < columns; column++)
                modelRow.Cells.Add(new TableCell());
            table.Rows.Add(modelRow);
        }
        return table;
    }
}
