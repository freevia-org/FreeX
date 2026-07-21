using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class WorksheetPrintRenderPlannerTests
{
    private static (Workbook Workbook, Sheet Sheet) CreateBook()
    {
        var workbook = new Workbook("Print planner");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));

    [Fact]
    public void TryBuild_EmptySheetProducesOneBlankPage()
    {
        // Excel's Print Preview always shows exactly one blank, paper-sized page (with margins and
        // any configured header/footer) for a sheet with no content at all, rather than no page.
        var (_, sheet) = CreateBook();

        WorksheetPrintRenderPlanner.TryBuild(sheet, printRangeOverride: null, ignorePrintArea: false, out var plan)
            .Should().BeTrue();

        plan.Pages.Should().ContainSingle();
        plan.PrintRanges.Should().ContainSingle()
            .Which.Should().Be(Range(sheet.Id, 1, 1, 1, 1));
    }

    [Fact]
    public void TryBuild_EmptySheetWithIgnorePrintAreaAlsoProducesOneBlankPage()
    {
        // Sibling of TryBuild_EmptySheetProducesOneBlankPage: the ignorePrintArea path routes through
        // the same used-range fallback and must not regress to the old "no page" behavior either.
        var (_, sheet) = CreateBook();
        sheet.PrintArea = Range(sheet.Id, 1, 1, 2, 2);

        WorksheetPrintRenderPlanner.TryBuild(sheet, printRangeOverride: null, ignorePrintArea: true, out var plan)
            .Should().BeTrue();

        plan.Pages.Should().ContainSingle();
        plan.PrintRanges.Should().ContainSingle()
            .Which.Should().Be(Range(sheet.Id, 1, 1, 1, 1));
    }

    [Fact]
    public void BuildMetrics_UsesWorksheetPaperSetupAndMargins()
    {
        var (_, sheet) = CreateBook();
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PageMargins = new WorksheetPageMargins(0.25, 0.75, 0.5, 1.0);
        sheet.HeaderMargin = 0.3;
        sheet.FooterMargin = 0.4;

        var metrics = WorksheetPrintRenderPlanner.BuildMetrics(sheet);

        metrics.PageWidth.Should().BeApproximately(11.0 * PagePaginationPlanner.Dpi, 0.01);
        metrics.PageHeight.Should().BeApproximately(8.5 * PagePaginationPlanner.Dpi, 0.01);
        metrics.MarginLeft.Should().Be(0.25 * PagePaginationPlanner.Dpi);
        metrics.MarginRight.Should().Be(0.75 * PagePaginationPlanner.Dpi);
        metrics.MarginTop.Should().Be(0.5 * PagePaginationPlanner.Dpi);
        metrics.MarginBottom.Should().Be(1.0 * PagePaginationPlanner.Dpi);
        metrics.HeaderMargin.Should().Be(0.3 * PagePaginationPlanner.Dpi);
        metrics.FooterMargin.Should().Be(0.4 * PagePaginationPlanner.Dpi);
    }

    [Fact]
    public void TryBuild_UsesSameSheetOverrideBeforeConfiguredPrintAreas()
    {
        var (_, sheet) = CreateBook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Configured"));
        sheet.SetCell(new CellAddress(sheet.Id, 40, 20), new TextValue("Override"));
        sheet.PrintArea = Range(sheet.Id, 1, 1, 1, 1);
        var overrideRange = Range(sheet.Id, 40, 20, 40, 20);

        WorksheetPrintRenderPlanner.TryBuild(sheet, overrideRange, ignorePrintArea: false, out var plan)
            .Should().BeTrue();

        plan.PrintRanges.Should().Equal(overrideRange);
        plan.Viewport.MaxRow.Should().Be(40);
        plan.Viewport.MaxColumn.Should().Be(20);
        plan.Pages.Should().ContainSingle();
        plan.Pages[0].PrintRange.Should().Be(overrideRange);
    }

    [Fact]
    public void TryBuild_IgnoresForeignOverrideAndUsesConfiguredAreasForThisSheet()
    {
        var workbook = new Workbook("Print planner");
        var sheet = workbook.AddSheet("Sheet1");
        var other = workbook.AddSheet("Other");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Local"));
        other.SetCell(new CellAddress(other.Id, 5, 5), new TextValue("Foreign"));
        var configured = Range(sheet.Id, 1, 1, 2, 2);
        sheet.PrintArea = configured;

        var foreignOverride = Range(other.Id, 5, 5, 6, 6);

        WorksheetPrintRenderPlanner.TryBuild(sheet, foreignOverride, ignorePrintArea: false, out var plan)
            .Should().BeTrue();

        plan.PrintRanges.Should().Equal(configured);
    }

    [Fact]
    public void TryBuild_IgnorePrintAreaFallsBackToUsedRange()
    {
        var (_, sheet) = CreateBook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Inside"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 80), new TextValue("Outside"));
        sheet.PrintArea = Range(sheet.Id, 1, 1, 1, 1);

        WorksheetPrintRenderPlanner.TryBuild(sheet, printRangeOverride: null, ignorePrintArea: true, out var plan)
            .Should().BeTrue();

        plan.PrintRanges.Should().ContainSingle()
            .Which.Should().Be(Range(sheet.Id, 1, 1, 1, 80));
        plan.Pages.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public void TryBuild_MultiAreaPrintPlanKeepsAreaBoundariesAndGlobalPageNumbers()
    {
        var (_, sheet) = CreateBook();
        sheet.FirstPageNumber = 7;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Area 1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("Area 2"));
        var firstArea = Range(sheet.Id, 1, 1, 2, 3);
        var secondArea = Range(sheet.Id, 1, 5, 2, 7);
        sheet.SetPrintAreas([firstArea, secondArea]);

        WorksheetPrintRenderPlanner.TryBuild(sheet, printRangeOverride: null, ignorePrintArea: false, out var plan)
            .Should().BeTrue();

        plan.FirstPageNumber.Should().Be(7);
        plan.AreaPlans.Should().HaveCount(2);
        plan.PrintRanges.Should().Equal(firstArea, secondArea);
        plan.Pages.Should().HaveCount(2);
        plan.Pages.Select(page => page.PageNumber).Should().Equal(7, 8);
        plan.Pages.Select(page => page.AreaIndex).Should().Equal(0, 1);
        plan.Pages.Select(page => page.PrintRange).Should().Equal(firstArea, secondArea);
    }

    [Fact]
    public void TryBuild_ViewportExtendsToRepeatTitleRowsAndColumns()
    {
        var (_, sheet) = CreateBook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Body"));
        sheet.PrintArea = Range(sheet.Id, 1, 1, 2, 3);
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 12);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 9);

        WorksheetPrintRenderPlanner.TryBuild(sheet, printRangeOverride: null, ignorePrintArea: false, out var plan)
            .Should().BeTrue();

        plan.Viewport.MaxRow.Should().Be(12);
        plan.Viewport.MaxColumn.Should().Be(9);
        plan.Viewport.RequestHeight.Should().Be(12 * WorksheetPrintViewportPlan.ExtentMultiplier);
        plan.Viewport.RequestWidth.Should().Be(9 * WorksheetPrintViewportPlan.ExtentMultiplier);
    }

    [Fact]
    public void TryBuild_PageOrderAndManualBreaksShapeRendererPages()
    {
        var (_, sheet) = CreateBook();
        sheet.PageOrder = WorksheetPageOrder.OverThenDown;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.PrintArea = Range(sheet.Id, 1, 1, 8, 8);
        sheet.RowPageBreaks.Add(5);
        sheet.ColumnPageBreaks.Add(5);

        WorksheetPrintRenderPlanner.TryBuild(sheet, printRangeOverride: null, ignorePrintArea: false, out var plan)
            .Should().BeTrue();

        plan.Pages.Should().HaveCount(4);
        plan.Pages.Select(page => (page.RowPlan.BodyRows[0], page.ColumnPlan.BodyColumns[0]))
            .Should().Equal((1u, 1u), (1u, 5u), (5u, 1u), (5u, 5u));
        plan.Pages.Select(page => page.PageNumber).Should().Equal(1, 2, 3, 4);
    }
}
