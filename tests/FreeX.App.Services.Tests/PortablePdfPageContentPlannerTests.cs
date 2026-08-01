using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class PortablePdfPageContentPlannerTests
{
    [Fact]
    public void CreatePlan_BuildsSemanticGridForRequestedPdfPage()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Summary");
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Q3"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(42));

        var exportPlan = CreateExportPlan(
            workbook,
            sheet,
            GridRange.Parse("A1:E6", sheet.Id),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 3, ColumnsPerPage: 3));

        var plan = PortablePdfPageContentPlanner.CreatePlan(workbook, exportPlan, exportPageNumber: 5);

        plan.IsReady.Should().BeTrue();
        plan.Status.Should().Be(PortablePdfPageContentPlanStatus.Ready);
        plan.PageRequest.Should().BeSameAs(exportPlan.PageRequests[4]);
        plan.RowCount.Should().Be(3);
        plan.ColumnCount.Should().Be(3);
        plan.StatusText.Should().Be("Ready to render portable PDF page 5: 3 rows, 3 columns, 9 cells.");
        plan.Rows.Should().Equal(
            new PortablePdfPageRow(1, PortablePdfPageAxisRole.Title),
            new PortablePdfPageRow(4, PortablePdfPageAxisRole.Body),
            new PortablePdfPageRow(5, PortablePdfPageAxisRole.Body));
        plan.Columns.Should().Equal(
            new PortablePdfPageColumn(1, PortablePdfPageAxisRole.Title),
            new PortablePdfPageColumn(4, PortablePdfPageAxisRole.Body),
            new PortablePdfPageColumn(5, PortablePdfPageAxisRole.Body));
        plan.Cells.Should().HaveCount(9);
        plan.Cells.Single(cell => cell.Row == 1 && cell.Column == 1)
            .Should()
            .BeEquivalentTo(new PortablePdfPageCell(1, 1, "Region", StyleId.Default, true, true));
        plan.Cells.Single(cell => cell.Row == 1 && cell.Column == 4)
            .Should()
            .BeEquivalentTo(new PortablePdfPageCell(1, 4, "Q3", StyleId.Default, true, false));
        plan.Cells.Single(cell => cell.Row == 4 && cell.Column == 1)
            .Should()
            .BeEquivalentTo(new PortablePdfPageCell(4, 1, "North", StyleId.Default, false, true));
        plan.Cells.Single(cell => cell.Row == 4 && cell.Column == 4)
            .DisplayText.Should().Be("42");
        plan.Cells.Single(cell => cell.Row == 5 && cell.Column == 5)
            .DisplayText.Should().Be("");
        plan.Cells.Should().NotContain(cell => cell.Row == 2 || cell.Row == 3 || cell.Column == 2 || cell.Column == 3);
    }

    [Fact]
    public void CreatePlan_UsesCellAndStyleOnlyStyleIdsAndFormattedDisplayText()
    {
        var workbook = new Workbook("Budget");
        var currencyStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var boldStyle = workbook.RegisterStyle(new CellStyle { Bold = true });
        var sheet = workbook.AddSheet("Summary");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));
        sheet.GetCell(1, 1)!.StyleId = currencyStyle;
        sheet.SetStyleOnly(1, 2, boldStyle);

        var exportPlan = CreateExportPlan(
            workbook,
            sheet,
            GridRange.Parse("A1:B1", sheet.Id),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 10, ColumnsPerPage: 10));

        var plan = PortablePdfPageContentPlanner.CreatePlan(workbook, exportPlan.PageRequests.Single());

        plan.IsReady.Should().BeTrue();
        plan.Cells.Should().Equal(
            new PortablePdfPageCell(1, 1, "$42.00", currencyStyle, false, false),
            new PortablePdfPageCell(1, 2, "", boldStyle, false, false));
    }

    [Fact]
    public void CreatePlan_UsesFormulaTextWhenSheetShowsFormulas()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Summary");
        sheet.ShowFormulas = true;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromFormula("B1+1"));
        sheet.GetCell(1, 1)!.Value = new NumberValue(5);

        var exportPlan = CreateExportPlan(
            workbook,
            sheet,
            GridRange.Parse("A1:A1", sheet.Id),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 10, ColumnsPerPage: 10));

        var plan = PortablePdfPageContentPlanner.CreatePlan(workbook, exportPlan, exportPageNumber: 1);

        plan.Cells.Should().ContainSingle()
            .Which.DisplayText.Should().Be("=B1+1");
    }

    [Fact]
    public void CreatePlan_ReturnsUnavailableStatusWhenPageNumberIsMissing()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Summary");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var exportPlan = CreateExportPlan(
            workbook,
            sheet,
            GridRange.Parse("A1:A1", sheet.Id),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 10, ColumnsPerPage: 10));

        var plan = PortablePdfPageContentPlanner.CreatePlan(workbook, exportPlan, exportPageNumber: 99);

        plan.IsReady.Should().BeFalse();
        plan.Status.Should().Be(PortablePdfPageContentPlanStatus.PageRequestUnavailable);
        plan.PageRequest.Should().BeNull();
        plan.Rows.Should().BeEmpty();
        plan.Columns.Should().BeEmpty();
        plan.Cells.Should().BeEmpty();
        plan.StatusText.Should().Be("Portable PDF page 99 is not present in the export plan.");
    }

    [Fact]
    public void R112_NarrowNumericColumn_AppliesWidthOverflowIndicator()
    {
        // Column A is narrowed to exactly 2.0 character units (19px -> 2 estimated characters),
        // matching ViewportService.GetColumnWidthPixels/EstimateCharacterWidth's own conversion.
        // A currency-formatted 42 renders as "$42.00" (6 characters) -- too wide for the 2-character
        // column -- so Excel (and the sibling grid/print paths) show the '##' overflow indicator
        // instead of the raw digits.
        var workbook = new Workbook("Budget");
        var currencyStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var sheet = workbook.AddSheet("Summary");
        sheet.ColumnWidths[1] = 2.0;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));
        sheet.GetCell(1, 1)!.StyleId = currencyStyle;

        var exportPlan = CreateExportPlan(
            workbook,
            sheet,
            GridRange.Parse("A1:A1", sheet.Id),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 10, ColumnsPerPage: 10));

        var plan = PortablePdfPageContentPlanner.CreatePlan(workbook, exportPlan.PageRequests.Single());

        plan.Cells.Should().ContainSingle()
            .Which.DisplayText.Should().Be("##");
    }

    [Fact]
    public void R112_NarrowNumericColumnWithShrinkToFit_DoesNotApplyWidthOverflowIndicator()
    {
        // Sibling/no-regression: Excel never shows the '#' overflow indicator when the cell's own
        // Format Cells > Alignment > Shrink to Fit is on (the font shrinks instead) -- mirroring
        // ViewportService.GetDisplayText and PageContentRenderModelBuilder.FormatCellText's identical
        // suppressWidthOverflowIndicator: style.ShrinkToFit wiring.
        var workbook = new Workbook("Budget");
        var currencyStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00", ShrinkToFit = true });
        var sheet = workbook.AddSheet("Summary");
        sheet.ColumnWidths[1] = 2.0;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));
        sheet.GetCell(1, 1)!.StyleId = currencyStyle;

        var exportPlan = CreateExportPlan(
            workbook,
            sheet,
            GridRange.Parse("A1:A1", sheet.Id),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 10, ColumnsPerPage: 10));

        var plan = PortablePdfPageContentPlanner.CreatePlan(workbook, exportPlan.PageRequests.Single());

        plan.Cells.Should().ContainSingle()
            .Which.DisplayText.Should().Be("$42.00");
    }

    [Fact]
    public void R112_NarrowTextColumn_NeverAppliesWidthOverflowIndicator()
    {
        // No-regression: the '#' overflow indicator is scoped strictly to numeric/date-time values
        // (NumberFormatter.FormatWithColor). A text value in the same narrow column keeps overflowing
        // visually into the neighbor cell exactly as before -- WorkbookPdfContentBuilder's draw path
        // deliberately lets a too-wide right-aligned/left-aligned text value bleed, which is correct
        // Excel behavior for text (unlike numbers/dates).
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Summary");
        sheet.ColumnWidths[1] = 2.0;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("HelloWorld"));

        var exportPlan = CreateExportPlan(
            workbook,
            sheet,
            GridRange.Parse("A1:A1", sheet.Id),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 10, ColumnsPerPage: 10));

        var plan = PortablePdfPageContentPlanner.CreatePlan(workbook, exportPlan.PageRequests.Single());

        plan.Cells.Should().ContainSingle()
            .Which.DisplayText.Should().Be("HelloWorld");
    }

    private static PortablePdfExportPlan CreateExportPlan(
        Workbook workbook,
        Sheet sheet,
        GridRange selectedRange,
        WorkbookExportPrintPageCapacity pageCapacity)
    {
        var exportPrintPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.SelectedRange,
                WorkbookExportPrintOutputKind.Pdf,
                SelectedRange: selectedRange),
            pageCapacity,
            WorkbookExportPrintSurface.MacOs);

        return PortablePdfExportPlanner.CreatePlan(exportPrintPlan);
    }
}
