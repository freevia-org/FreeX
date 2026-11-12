using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Excel's "Print Active Sheets" prints every sheet in the selected tab group. FreeX used to print
/// only the sheet on screen; the renderer now takes the group as a sheet filter.
/// </summary>
public sealed class PrintRendererSheetGroupTests
{
    [Fact]
    public void RenderWorkbook_WithSheetFilter_RendersOnlyTheGroupedSheetsInWorkbookOrder()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Grouped print");
            var first = AddSheetWithText(workbook, "First");
            var second = AddSheetWithText(workbook, "Second");
            var third = AddSheetWithText(workbook, "Third");

            var everything = PrintRenderer.RenderWorkbook(workbook, new ViewportService());
            var grouped = PrintRenderer.RenderWorkbook(
                workbook,
                new ViewportService(),
                sheetFilter: new HashSet<SheetId> { third.Id, first.Id });

            everything.Pages.Count.Should().Be(3);
            grouped.Pages.Count.Should().Be(2, "only the two grouped sheets print, not the one left out");
            _ = second;
        });
    }

    [Fact]
    public void RenderWorkbook_WithSheetFilter_NeverPrintsAHiddenSheetEvenIfGrouped()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Grouped print");
            var visible = AddSheetWithText(workbook, "Visible");
            var hidden = AddSheetWithText(workbook, "Hidden");
            hidden.IsHidden = true;

            var grouped = PrintRenderer.RenderWorkbook(
                workbook,
                new ViewportService(),
                sheetFilter: new HashSet<SheetId> { visible.Id, hidden.Id });

            grouped.Pages.Count.Should().Be(1);
        });
    }

    private static Sheet AddSheetWithText(Workbook workbook, string name)
    {
        var sheet = workbook.AddSheet(name);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(name));
        return sheet;
    }
}
