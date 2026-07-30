using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// Regression coverage for the round-95 HIGH finding: a hidden row/column sitting in the INTERIOR of
/// a page's print range was silently reinstated (with its normal, non-zero size) on the Avalonia/
/// portable interactive Print Preview and PDF export render paths, because
/// <see cref="PageContentRenderModelBuilder.Build"/> reconstructed each page's row/column list by
/// looping the lossy <c>PageAxisSegment.Start..End</c> range instead of using the pagination plan's
/// explicit, already hidden-excluding index list. The WPF native print path
/// (<see cref="WorksheetPrintRenderPlanner"/>, covered by
/// <see cref="WorksheetPrintRenderPlannerHiddenRowsTests"/>) never had this bug, since it reads the
/// explicit <c>TitleRows</c>/<c>BodyRows</c> lists directly and never collapses them into a segment.
///
/// These tests drive the real product entry point for the Avalonia/portable interactive print preview
/// -- <see cref="PrintPreviewPaginationContext.TryCreate"/> + <see cref="PrintPreviewPaginationContext.BuildPage"/>
/// -- and additionally flatten the result through <see cref="PrintPreviewInstructionBuilder.Build"/> so
/// the assertions land on actual paint instructions (positioned text/rectangles), not just readable
/// state.
/// </summary>
public sealed class R95_HiddenRowsColumnsPrintRangeInteriorTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    private static (Workbook Workbook, Sheet Sheet) CreateBook()
    {
        var workbook = new Workbook("Hidden interior print range");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    [Fact]
    public void R95_BuildPage_HiddenInteriorRowsExcludedFromPrintedCellsAndPositions()
    {
        var (workbook, sheet) = CreateBook();
        for (uint row = 1; row <= 10; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1));
        // Rows 4 and 5 sit in the MIDDLE of the print range, not at either edge.
        sheet.HiddenRows.Add(4);
        sheet.HiddenRows.Add(5);

        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var context).Should().BeTrue();
        context.PageCount.Should().Be(1);

        var layout = context.BuildPage(0);
        layout.Should().NotBeNull();

        var printedRows = layout!.Cells.Select(c => c.Row).Distinct().OrderBy(r => r).ToList();
        printedRows.Should().Equal(1u, 2u, 3u, 6u, 7u, 8u, 9u, 10u);

        // Ink-level proof, not just "state is readable": row 6's rendered block sits directly beneath
        // row 3's -- no phantom gap where rows 4/5 would otherwise have been reinstated.
        var row3 = layout.Cells.Single(c => c.Row == 3);
        var row6 = layout.Cells.Single(c => c.Row == 6);
        row6.Bounds.Y.Should().BeApproximately(row3.Bounds.Y + row3.Bounds.Height, 0.01);

        var painting = PrintPreviewInstructionBuilder.Build(layout);
        painting.Instructions.Should().NotContain(i => i.Kind == PrintPreviewPaintKind.Text && i.Text == "4");
        painting.Instructions.Should().NotContain(i => i.Kind == PrintPreviewPaintKind.Text && i.Text == "5");
        painting.Instructions.Should().Contain(i => i.Kind == PrintPreviewPaintKind.Text && i.Text == "6");
    }

    [Fact]
    public void R95_BuildPage_HiddenInteriorColumnsExcludedFromPrintedCellsAndPositions()
    {
        var (workbook, sheet) = CreateBook();
        for (uint col = 1; col <= 10; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 10));
        sheet.HiddenCols.Add(4);
        sheet.HiddenCols.Add(5);

        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var context).Should().BeTrue();
        var layout = context.BuildPage(0);
        layout.Should().NotBeNull();

        var printedColumns = layout!.Cells.Select(c => c.Column).Distinct().OrderBy(c => c).ToList();
        printedColumns.Should().Equal(1u, 2u, 3u, 6u, 7u, 8u, 9u, 10u);

        var col3 = layout.Cells.Single(c => c.Column == 3);
        var col6 = layout.Cells.Single(c => c.Column == 6);
        col6.Bounds.X.Should().BeApproximately(col3.Bounds.X + col3.Bounds.Width, 0.01);
    }

    [Fact]
    public void R95_BuildPage_HiddenRepeatTitleRowIsNotReprinted()
    {
        // Sibling to the interior-segment bug: sheet.PrintTitleRows is a raw repeat range that
        // PageContentRenderModelBuilder.BuildAxisIndexes re-walks independently of the pagination
        // plan's already-filtered title list, so it needs its own hidden-row check.
        var (workbook, sheet) = CreateBook();
        for (uint row = 1; row <= 8; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 8, 1));
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        sheet.HiddenRows.Add(1);

        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var context).Should().BeTrue();
        var layout = context.BuildPage(0);
        layout.Should().NotBeNull();

        layout!.Cells.Should().NotContain(c => c.Row == 1);
    }

    [Fact]
    public void R95_BuildPage_VisibleRepeatTitleRowIsStillReprinted()
    {
        // No-regression sibling: a NOT-hidden repeat title row must still be reprinted ahead of the
        // body, positioned above it -- the hidden-row check added to the title loop must not suppress
        // the ordinary (non-hidden) case.
        var (workbook, sheet) = CreateBook();
        for (uint row = 1; row <= 8; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 8, 1));
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);

        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var context).Should().BeTrue();
        var layout = context.BuildPage(0);
        layout.Should().NotBeNull();

        layout!.Cells.Should().ContainSingle(c => c.Row == 1);
        var titleCell = layout.Cells.Single(c => c.Row == 1);
        var bodyFirstCell = layout.Cells.Single(c => c.Row == 2);
        titleCell.Bounds.Y.Should().BeLessThan(bodyFirstCell.Bounds.Y);
    }

    [Fact]
    public void R95_BuildPage_NoHiddenRowsStillPrintsEveryRowContiguously()
    {
        // No-regression sibling for the core fix: with nothing hidden, PageAxisSegment.Indexes must
        // still yield the full contiguous range (unchanged behavior for the common case).
        var (workbook, sheet) = CreateBook();
        for (uint row = 1; row <= 6; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 1));

        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var context).Should().BeTrue();
        var layout = context.BuildPage(0);
        layout.Should().NotBeNull();

        var printedRows = layout!.Cells.Select(c => c.Row).Distinct().OrderBy(r => r).ToList();
        printedRows.Should().Equal(1u, 2u, 3u, 4u, 5u, 6u);
    }
}
