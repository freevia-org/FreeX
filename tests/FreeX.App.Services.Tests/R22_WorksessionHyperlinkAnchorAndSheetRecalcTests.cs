using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R22-comments-hyperlinks-2: Insert Hyperlink over a multi-cell selection must only hyperlink the
/// anchor cell (range.Start), matching the WPF host's InsertLinkBtn_Click and real Excel -- fanning
/// the same displayText/target across every cell in the range silently clobbers each cell's own
/// distinct content.
///
/// R22-calc-engine-dependency-3: Moving or duplicating a sheet can change which sheets fall inside
/// a 3-D span reference (e.g. =SUM(Sheet1:Sheet3!A1)). Because MoveSheetCommand/DuplicateSheetCommand
/// report no AffectedCells, the normal post-command recalc is a no-op, so the workbook must be force-
/// recalculated after a successful Move/Duplicate -- matching the WPF host's RecalculateWorkbook()
/// call after MoveSheetCommand/DuplicateSheetCommand.
/// </summary>
public sealed class R22_WorksessionHyperlinkAnchorAndSheetRecalcTests
{
    [Fact]
    public void SetSelectedRangeHyperlink_OverMultiCellRange_OnlyHyperlinksAnchorCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(b1, new NumberValue(42));
        sheet.SetCell(c1, new TextValue("Distinct"));
        var session = CreateSession(workbook);
        session.SelectRange(new GridRange(a1, c1));
        var plan = HyperlinkDialogPlanner.Plan(
            "https://example.test",
            "Example",
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open example",
            "");

        var result = session.SetSelectedRangeHyperlink(plan);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(a1);
        sheet.GetValue(a1).Should().Be(new TextValue("Example"));
        sheet.Hyperlinks[a1].Should().Be("https://example.test");

        // B1 and C1 must retain their own distinct content: fanning the anchor's display
        // text/target across the whole selected range would silently overwrite them.
        sheet.GetValue(b1).Should().Be(new NumberValue(42));
        sheet.GetValue(c1).Should().Be(new TextValue("Distinct"));
        sheet.Hyperlinks.Should().NotContainKey(b1);
        sheet.Hyperlinks.Should().NotContainKey(c1);
        sheet.HyperlinkMetadata.Should().NotContainKey(b1);
        sheet.HyperlinkMetadata.Should().NotContainKey(c1);
    }

    [Fact]
    public void MoveActiveSheetTo_RecalculatesThreeDSpanFormulasAfterReorder()
    {
        var workbook = new Workbook("Book1");
        var host = workbook.AddSheet("Host");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet3 = workbook.AddSheet("Sheet3");
        var sheet4 = workbook.AddSheet("Sheet4");
        workbook.ActiveSheetIndex = 0;
        var hostA1 = new CellAddress(host.Id, 1, 1);
        host.SetCell(hostA1, Cell.FromFormula("SUM(Sheet2:Sheet3!A1)"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(1));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(2));
        sheet4.SetCell(new CellAddress(sheet4.Id, 1, 1), new NumberValue(100));

        var session = CreateSession(workbook);
        session.RecalculateWorkbook();
        host.GetValue(hostA1).Should().Be(new NumberValue(3));

        // Move Sheet4 (currently after Sheet3, outside the Sheet2:Sheet3 span) so it lands
        // between Sheet2 and Sheet3.
        session.SelectSheet(sheet4.Id);
        var result = session.MoveActiveSheetTo(2);

        result.Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Host", "Sheet2", "Sheet4", "Sheet3");

        // Sheet4 now falls inside the Sheet2:Sheet3 tab-order span, so the 3-D SUM must pick it
        // up. Without a post-move recalc the cached value would still show the stale pre-move 3.
        host.GetValue(hostA1).Should().Be(new NumberValue(103));
    }

    [Fact]
    public void DuplicateActiveSheet_RecalculatesThreeDSpanFormulasAfterDuplicate()
    {
        var workbook = new Workbook("Book1");
        var host = workbook.AddSheet("Host");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet3 = workbook.AddSheet("Sheet3");
        workbook.ActiveSheetIndex = 0;
        var hostA1 = new CellAddress(host.Id, 1, 1);
        host.SetCell(hostA1, Cell.FromFormula("SUM(Sheet2:Sheet3!A1)"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(1));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(2));

        var session = CreateSession(workbook);
        session.RecalculateWorkbook();
        host.GetValue(hostA1).Should().Be(new NumberValue(3));

        // Duplicating Sheet2 inserts the copy immediately after it, i.e. between Sheet2 and Sheet3.
        session.SelectSheet(sheet2.Id);
        var result = session.DuplicateActiveSheet();

        result.Success.Should().BeTrue();
        workbook.Sheets.Should().HaveCount(4);

        // The duplicate carries over Sheet2's A1 value (1) and now sits inside the Sheet2:Sheet3
        // span, so the 3-D SUM must include it. Without a post-duplicate recalc the cached value
        // would still show the stale pre-duplicate 3.
        host.GetValue(hostA1).Should().Be(new NumberValue(4));
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, workbook.Name, "Opened.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
