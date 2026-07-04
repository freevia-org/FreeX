using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// Regression coverage for H19 (K-print review group): <see cref="WorksheetPrintRenderPlanner.TryBuild"/>
/// must exclude manually hidden, AutoFilter-hidden, and outline-group-collapsed rows/columns from the
/// assembled print page plan, matching the on-screen grid (<see cref="Sheet.IsRowEffectivelyHidden"/> /
/// <see cref="Sheet.IsColEffectivelyHidden"/>) and Excel's own print behavior.
/// </summary>
public sealed class WorksheetPrintRenderPlannerHiddenRowsTests
{
    private static (Workbook Workbook, Sheet Sheet) CreateBook()
    {
        var workbook = new Workbook("Hidden rows print");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));

    [Fact]
    public void TryBuild_ExcludesManuallyHiddenRowsFromPrintedPage()
    {
        var (_, sheet) = CreateBook();
        for (uint row = 1; row <= 10; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.PrintArea = Range(sheet.Id, 1, 1, 10, 1);
        sheet.HiddenRows.Add(5);
        sheet.HiddenRows.Add(6);

        WorksheetPrintRenderPlanner.TryBuild(sheet, printRangeOverride: null, ignorePrintArea: false, out var plan)
            .Should().BeTrue();

        var printedRows = plan.Pages.SelectMany(page => page.Rows).ToList();
        printedRows.Should().NotContain(5u);
        printedRows.Should().NotContain(6u);
        printedRows.Should().Contain([1u, 2u, 3u, 4u, 7u, 8u, 9u, 10u]);
    }

    [Fact]
    public void TryBuild_ExcludesFilterHiddenAndGroupHiddenRows()
    {
        var (_, sheet) = CreateBook();
        for (uint row = 1; row <= 6; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.PrintArea = Range(sheet.Id, 1, 1, 6, 1);
        sheet.FilterHiddenRows.Add(2);
        sheet.GroupHiddenRows.Add(4);

        WorksheetPrintRenderPlanner.TryBuild(sheet, printRangeOverride: null, ignorePrintArea: false, out var plan)
            .Should().BeTrue();

        var printedRows = plan.Pages.SelectMany(page => page.Rows).ToList();
        printedRows.Should().NotContain(2u);
        printedRows.Should().NotContain(4u);
        printedRows.Should().Contain([1u, 3u, 5u, 6u]);
    }

    [Fact]
    public void TryBuild_ExcludesHiddenColumnsFromPrintedPage()
    {
        var (_, sheet) = CreateBook();
        for (uint col = 1; col <= 6; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));
        sheet.PrintArea = Range(sheet.Id, 1, 1, 1, 6);
        sheet.HiddenCols.Add(3);
        sheet.GroupHiddenCols.Add(4);

        WorksheetPrintRenderPlanner.TryBuild(sheet, printRangeOverride: null, ignorePrintArea: false, out var plan)
            .Should().BeTrue();

        var printedColumns = plan.Pages.SelectMany(page => page.Columns).ToList();
        printedColumns.Should().NotContain(3u);
        printedColumns.Should().NotContain(4u);
        printedColumns.Should().Contain([1u, 2u, 5u, 6u]);
    }

    [Fact]
    public void TryBuild_HiddenRepeatTitleRowIsNotReprintedOnAnyPage()
    {
        var (_, sheet) = CreateBook();
        for (uint row = 1; row <= 8; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.PrintArea = Range(sheet.Id, 2, 1, 8, 1);
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        sheet.HiddenRows.Add(1);

        WorksheetPrintRenderPlanner.TryBuild(sheet, printRangeOverride: null, ignorePrintArea: false, out var plan)
            .Should().BeTrue();

        plan.Pages.Should().NotBeEmpty();
        plan.Pages.SelectMany(page => page.RowPlan.TitleRows).Should().NotContain(1u);
        plan.Pages.SelectMany(page => page.Rows).Should().NotContain(1u);
    }
}
