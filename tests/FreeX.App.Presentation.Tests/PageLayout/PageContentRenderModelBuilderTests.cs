using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageContentRenderModelBuilderTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Build_ProducesCellBlocksWithText()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));

        var layout = BuildFirstPage(workbook, sheet);

        layout.Should().NotBeNull();
        layout!.Cells.Should().Contain(c => c.Row == 1 && c.Column == 1 && c.Text == "Hello");
        layout.Cells.Should().Contain(c => c.Row == 2 && c.Column == 2 && c.Text == "42");
    }

    [Fact]
    public void Build_NumberCellIsRightAlignedByGeneralAlignment()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("x"));

        var layout = BuildFirstPage(workbook, sheet);

        layout!.Cells.Single(c => c.Column == 1).Alignment.Should().Be(PageTextAlignment.Right);
        layout.Cells.Single(c => c.Column == 2).Alignment.Should().Be(PageTextAlignment.Left);
    }

    [Fact]
    public void Build_CellFillIsResolvedToPresentationRgb()
    {
        var (workbook, sheet) = CreateWorkbook();
        var style = new CellStyle { FillColor = new CellColor(10, 20, 30) };
        var styleId = workbook.RegisterStyle(style);
        var cell = Cell.FromValue(new TextValue("filled"));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var layout = BuildFirstPage(workbook, sheet);

        var block = layout!.Cells.Single(c => c.Row == 1 && c.Column == 1);
        block.Fill.Should().Be(new PresentationRgb(10, 20, 30));
    }

    [Fact]
    public void Build_CellBordersAreResolvedPerEdge()
    {
        var (workbook, sheet) = CreateWorkbook();
        var style = new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thin, new CellColor(1, 2, 3)),
            BorderBottom = new CellBorder(BorderStyle.Thick, new CellColor(4, 5, 6)),
        };
        var styleId = workbook.RegisterStyle(style);
        var cell = Cell.FromValue(new TextValue("bordered"));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var layout = BuildFirstPage(workbook, sheet);

        var block = layout!.Cells.Single(c => c.Row == 1 && c.Column == 1);
        block.Borders.Top.Should().Be(new PageBorderEdge(BorderStyle.Thin, new PresentationRgb(1, 2, 3)));
        block.Borders.Bottom.Should().Be(new PageBorderEdge(BorderStyle.Thick, new PresentationRgb(4, 5, 6)));
        block.Borders.Left.IsVisible.Should().BeFalse();
        block.Borders.Right.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void Build_FontReflectsStyle()
    {
        var (workbook, sheet) = CreateWorkbook();
        var style = new CellStyle
        {
            FontName = "Arial",
            FontSize = 14,
            Bold = true,
            Italic = true,
            FontColor = new CellColor(9, 8, 7),
        };
        var styleId = workbook.RegisterStyle(style);
        var cell = Cell.FromValue(new TextValue("styled"));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var layout = BuildFirstPage(workbook, sheet);

        var font = layout!.Cells.Single(c => c.Row == 1 && c.Column == 1).Font;
        font.FontFamily.Should().Be("Arial");
        font.FontSize.Should().Be(14);
        font.Bold.Should().BeTrue();
        font.Italic.Should().BeTrue();
        font.Color.Should().Be(new PresentationRgb(9, 8, 7));
    }

    [Fact]
    public void Build_GridlinesToggle()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("b"));

        sheet.PrintGridlines = false;
        BuildFirstPage(workbook, sheet)!.GridLines.Should().BeEmpty();

        sheet.PrintGridlines = true;
        var layout = BuildFirstPage(workbook, sheet)!;
        layout.GridLines.Should().NotBeEmpty();
        // 3 columns + 1 and 3 rows + 1 = 4 vertical + 4 horizontal lines.
        layout.GridLines.Should().HaveCount(8);
    }

    [Fact]
    public void Build_HeadingsToggle()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("b"));

        sheet.PrintHeadings = false;
        var noHeadings = BuildFirstPage(workbook, sheet)!;
        noHeadings.ColumnHeadings.Should().BeEmpty();
        noHeadings.RowHeadings.Should().BeEmpty();

        sheet.PrintHeadings = true;
        var headings = BuildFirstPage(workbook, sheet)!;
        headings.ColumnHeadings.Select(h => h.Label).Should().ContainInOrder("A", "B");
        headings.RowHeadings.Select(h => h.Label).Should().Contain("1");
    }

    [Fact]
    public void Build_MarginInsetIsAppliedToPrintableArea()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins = new WorksheetPageMargins(1.0, 0.75, 0.5, 0.25);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a"));

        var layout = BuildFirstPage(workbook, sheet)!;

        const double dpi = 96.0;
        layout.PageBounds.Width.Should().BeApproximately(8.5 * dpi, 0.001);
        layout.PageBounds.Height.Should().BeApproximately(11.0 * dpi, 0.001);
        layout.PrintableArea.Left.Should().BeApproximately(1.0 * dpi, 0.001);
        layout.PrintableArea.Top.Should().BeApproximately(0.5 * dpi, 0.001);
        layout.PrintableArea.Right.Should().BeApproximately((8.5 - 0.75) * dpi, 0.001);
        layout.PrintableArea.Bottom.Should().BeApproximately((11.0 - 0.25) * dpi, 0.001);
    }

    [Fact]
    public void Build_LandscapeSwapsPageDimensions()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a"));

        var layout = BuildFirstPage(workbook, sheet)!;

        const double dpi = 96.0;
        layout.PageBounds.Width.Should().BeApproximately(11.0 * dpi, 0.001);
        layout.PageBounds.Height.Should().BeApproximately(8.5 * dpi, 0.001);
    }

    [Fact]
    public void Build_HeaderFooterTokensAreSubstituted()
    {
        var (workbook, sheet) = CreateWorkbook("Budget.xlsx");
        sheet.Name = "Sheet1";
        sheet.PageHeader = new WorksheetHeaderFooter("&F", "Page &P of &N", "&A");
        sheet.PageFooter = new WorksheetHeaderFooter("&D", "", "");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a"));

        var date = new DateTime(2026, 6, 16);
        var pagePlan = Paginate(sheet);
        var layout = PageContentRenderModelBuilder.Build(workbook, sheet, pagePlan, 0, Measurer, date)!;

        layout.HeaderRuns.Should().Contain(r => r.Text == "Budget.xlsx" && r.Alignment == PageTextAlignment.Left);
        layout.HeaderRuns.Should().Contain(r => r.Text == "Page 1 of 1" && r.Alignment == PageTextAlignment.Center);
        layout.HeaderRuns.Should().Contain(r => r.Text == "Sheet1" && r.Alignment == PageTextAlignment.Right);
        layout.FooterRuns.Should().Contain(r =>
            r.Text == date.ToString("d", System.Globalization.CultureInfo.CurrentCulture));
    }

    [Fact]
    public void Build_BracketedHeaderFooterTokensAreSubstituted()
    {
        var (workbook, sheet) = CreateWorkbook("Report.xlsx");
        sheet.Name = "Data";
        sheet.PageHeader = new WorksheetHeaderFooter("&[File]", "&[Page]/&[Pages]", "&[Tab]");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a"));

        var pagePlan = Paginate(sheet);
        var layout = PageContentRenderModelBuilder.Build(
            workbook, sheet, pagePlan, 0, Measurer, new DateTime(2026, 1, 1))!;

        layout.HeaderRuns.Select(r => r.Text).Should().Contain("Report.xlsx");
        layout.HeaderRuns.Select(r => r.Text).Should().Contain("1/1");
        layout.HeaderRuns.Select(r => r.Text).Should().Contain("Data");
    }

    [Fact]
    public void Build_PageNumberTokenReflectsLaterPage()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.PaperSize = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.PageFooter = new WorksheetHeaderFooter("", "&P of &N", "");
        // Many rows so pagination produces multiple pages.
        for (uint row = 1; row <= 200; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var pagePlan = Paginate(sheet);
        pagePlan.PageCount.Should().BeGreaterThan(1);

        var lastIndex = pagePlan.PageCount - 1;
        var layout = PageContentRenderModelBuilder.Build(
            workbook, sheet, pagePlan, lastIndex, Measurer, new DateTime(2026, 1, 1))!;

        layout.PageNumber.Should().Be(pagePlan.PageCount);
        layout.FooterRuns.Should().Contain(r => r.Text == $"{pagePlan.PageCount} of {pagePlan.PageCount}");
    }

    [Fact]
    public void Build_FirstPageNumberOffsetsPageNumbering()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.FirstPageNumber = 5;
        sheet.PageFooter = new WorksheetHeaderFooter("", "&P", "");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a"));

        var pagePlan = Paginate(sheet);
        var layout = PageContentRenderModelBuilder.Build(
            workbook, sheet, pagePlan, 0, Measurer, new DateTime(2026, 1, 1))!;

        layout.PageNumber.Should().Be(5);
        layout.FooterRuns.Should().Contain(r => r.Text == "5");
    }

    [Fact]
    public void Build_MergedCellEmittedOnceWithSpanningBounds()
    {
        var (workbook, sheet) = CreateWorkbook();
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchor, new TextValue("merged"));
        sheet.AddMergedRegion(new GridRange(anchor, new CellAddress(sheet.Id, 1, 3)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("below"));

        var layout = BuildFirstPage(workbook, sheet)!;

        var mergedBlocks = layout.Cells.Where(c => c.Row == 1 && c.Column >= 1 && c.Column <= 3).ToList();
        mergedBlocks.Should().ContainSingle();
        var block = mergedBlocks[0];
        block.Column.Should().Be(1);
        block.Text.Should().Be("merged");
        // The anchor block spans all three columns of the merge.
        block.Bounds.Width.Should().BeApproximately(layout.GridBounds.Width / 3 * 3, 0.001);
    }

    [Fact]
    public void Build_PrintErrorValueBlankReplacesErrors()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.PrintErrorValue = WorksheetPrintErrorValue.Blank;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new ErrorValue("#DIV/0!"));

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.Cells.Should().NotContain(c => c.Text == "#DIV/0!");
    }

    [Fact]
    public void Build_PrintErrorValueDashReplacesErrors()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.PrintErrorValue = WorksheetPrintErrorValue.Dash;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new ErrorValue("#REF!"));

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.Cells.Single(c => c.Row == 1 && c.Column == 1).Text.Should().Be("--");
    }

    [Fact]
    public void Build_TextOriginIsVerticallyCentered()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hi"));

        var layout = BuildFirstPage(workbook, sheet)!;

        var block = layout.Cells.Single(c => c.Row == 1 && c.Column == 1);
        // x origin is the cell left + 2px inset (mirrors the desktop renderer).
        block.TextOrigin.X.Should().BeApproximately(block.Bounds.Left + 2, 0.001);
        // y origin is centered inside the cell, so strictly inside its vertical bounds.
        block.TextOrigin.Y.Should().BeGreaterThanOrEqualTo(block.Bounds.Top);
        block.TextOrigin.Y.Should().BeLessThan(block.Bounds.Bottom);
    }

    [Fact]
    public void Build_IncludesTextBoxBlocksFromSharedPlanner()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Anchor"));
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Text = "Printable note",
            Width = 96,
            Height = 42,
            FillColor = new CellColor(200, 220, 240),
            OutlineColor = new CellColor(20, 70, 120)
        });

        var layout = BuildFirstPage(workbook, sheet)!;

        var block = layout.TextBoxes.Should().ContainSingle().Subject;
        block.Text.Should().Be("Printable note");
        block.Fill.Should().Be(new PresentationRgb(200, 220, 240));
        block.Outline.Should().Be(new PresentationRgb(20, 70, 120));
        block.TextBounds.Left.Should().BeApproximately(block.Bounds.Left + PageTextBoxLayoutPlanner.TextInset, 0.001);
        block.TextBounds.Top.Should().BeApproximately(block.Bounds.Top + PageTextBoxLayoutPlanner.TextInset, 0.001);
    }

    [Fact]
    public void Build_IncludesVisibleChartBlocksWithSelectableTextOverlays()
    {
        var (workbook, sheet) = CreateWorkbook();
        PopulateChartSource(sheet);
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 20, 8));
        var chart = CreatePrintedChart(sheet, "Printable chart title", left: 24, top: 24);
        chart.XAxisTitle = "Printable month axis";
        chart.YAxisTitle = "Printable sales axis";
        chart.ChartAreaFillColor = new CellColor(245, 250, 255);
        chart.ChartAreaBorderColor = new CellColor(20, 70, 120);
        sheet.Charts.Add(chart);

        var layout = BuildFirstPage(workbook, sheet)!;

        var block = layout.Charts.Should().ContainSingle().Subject;
        block.Id.Should().Be(chart.Id);
        block.Fill.Should().Be(new PresentationRgb(245, 250, 255));
        block.Outline.Should().Be(new PresentationRgb(20, 70, 120));
        // Bounds.Left is NOT gridLeft + the raw anchor-space chart.Left=24: chart anchors live in
        // ChartAnchorGeometry's width-in-chars*8 space, while the printed grid uses
        // ColumnWidthPixelMapper's width*7+5 space (see ChartAnchorGeometry.ConvertColumnOffsetToGridSpace).
        // With the fixture's default column width of 8.43 chars, one column is 8.43*8 = 67.44px in
        // anchor space and Math.Round(8.43*7+5) = 64px in grid space; the chart's anchor offset of 24
        // anchor-space px is 24/67.44 = 35.5871...% of the way across column 1, which lands at the same
        // 35.5871...% of column 1's 64px grid-space width = 22.7758...px. Rows use the identical
        // convention in both spaces (ConvertRowOffsetToGridSpace is an identity aside from hidden-row
        // skipping), so Top keeps the raw anchor-space offset of 24.
        var expectedGridLeftOffset = 24 / (8.43 * 8) * Math.Round(8.43 * 7 + 5);
        block.Bounds.Left.Should().BeApproximately(layout.GridBounds.Left + expectedGridLeftOffset, 0.001);
        block.Bounds.Top.Should().BeApproximately(layout.GridBounds.Top + 24, 0.001);
        block.TextOverlays.Select(overlay => overlay.Text).Should().Contain(
            ["Printable chart title", "Printable month axis", "Printable sales axis"]);
        block.TextOverlays.Single(overlay => overlay.Text == "Printable sales axis")
            .RotationDegrees.Should().Be(-90);
    }

    [Fact]
    public void Build_FiltersHiddenAndOffPageChartsAndSuppressesClippedChartTextOverlays()
    {
        var (workbook, sheet) = CreateWorkbook();
        PopulateChartSource(sheet);
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 20, 8));

        var visible = CreatePrintedChart(sheet, "Visible chart", left: 24, top: 24);
        var hidden = CreatePrintedChart(sheet, "Hidden chart", left: 24, top: 24);
        hidden.IsVisible = false;
        var offPage = CreatePrintedChart(sheet, "Off-page chart", left: 10000, top: 10000);
        var clipped = CreatePrintedChart(sheet, "Clipped chart", left: 650, top: 24);
        sheet.Charts.Add(visible);
        sheet.Charts.Add(hidden);
        sheet.Charts.Add(offPage);
        sheet.Charts.Add(clipped);

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.Charts.Select(chart => chart.Id).Should().Contain(visible.Id);
        layout.Charts.Select(chart => chart.Id).Should().Contain(clipped.Id);
        layout.Charts.Select(chart => chart.Id).Should().NotContain(hidden.Id);
        layout.Charts.Select(chart => chart.Id).Should().NotContain(offPage.Id);
        layout.Charts.Single(chart => chart.Id == visible.Id).TextOverlays
            .Select(overlay => overlay.Text).Should().Contain("Visible chart");
        layout.Charts.Single(chart => chart.Id == clipped.Id).TextOverlays.Should().BeEmpty();
    }

    [Fact]
    public void Build_OutOfRangePageReturnsNull()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a"));
        var pagePlan = Paginate(sheet);

        PageContentRenderModelBuilder.Build(workbook, sheet, pagePlan, -1, Measurer).Should().BeNull();
        PageContentRenderModelBuilder.Build(workbook, sheet, pagePlan, pagePlan.PageCount, Measurer).Should().BeNull();
    }

    [Fact]
    public void Build_ShowFormulasEmitsFormulaText()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.ShowFormulas = true;
        var cell = Cell.FromFormula("A1+B1");
        cell.Value = new NumberValue(3);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.Cells.Single(c => c.Row == 1 && c.Column == 1).Text.Should().Be("=A1+B1");
    }

    [Fact]
    public void Build_NullArguments_Throw()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a"));
        var pagePlan = Paginate(sheet);

        var act1 = () => PageContentRenderModelBuilder.Build(null!, sheet, pagePlan, 0, Measurer);
        var act2 = () => PageContentRenderModelBuilder.Build(workbook, sheet, pagePlan, 0, null!);
        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Production-path wiring test: a sheet with tall rows (60px each) fits fewer rows per page than
    /// the same sheet with default rows (20px). PrintPreviewPaginationContext.TryCreate now calls the
    /// real-size Paginate overload, so page count must reflect the actual row heights.
    /// </summary>
    [Fact]
    public void TryCreate_TallRowsProduceMorePagesThanDefaultRows()
    {
        // Arrange: 60 rows of data — with default 20px rows all fit on 1 A4-narrow page (51 per page).
        // With 60px tall rows only ~17 rows fit per page, so 60 rows → 4 pages.
        const int rowCount = 60;
        const uint tallRowHeightPx = 60;

        var (workbook, sheetDefault) = CreateWorkbook();
        sheetDefault.PaperSize = WorksheetPaperSize.A4;
        sheetDefault.PageOrientation = WorksheetPageOrientation.Portrait;
        sheetDefault.PageMargins = WorksheetPageMargins.Narrow;
        // DefaultRowHeight stays at 20px (the Sheet default).
        for (uint r = 1; r <= rowCount; r++)
            sheetDefault.SetCell(new CellAddress(sheetDefault.Id, r, 1), new NumberValue(r));

        var (_, sheetTall) = CreateWorkbook();
        sheetTall.PaperSize = WorksheetPaperSize.A4;
        sheetTall.PageOrientation = WorksheetPageOrientation.Portrait;
        sheetTall.PageMargins = WorksheetPageMargins.Narrow;
        // All rows are 60px tall (3× the default).
        for (uint r = 1; r <= rowCount; r++)
        {
            sheetTall.RowHeights[r] = tallRowHeightPx;
            sheetTall.SetCell(new CellAddress(sheetTall.Id, r, 1), new NumberValue(r));
        }

        // Act: use PrintPreviewPaginationContext (the production path for the print preview).
        var defaultCreated = PrintPreviewPaginationContext.TryCreate(workbook, sheetDefault, Measurer, out var defaultCtx);
        var tallCreated = PrintPreviewPaginationContext.TryCreate(workbook, sheetTall, Measurer, out var tallCtx);

        // Assert: both contexts are valid, and tall rows produce more pages.
        defaultCreated.Should().BeTrue();
        tallCreated.Should().BeTrue();
        tallCtx.PageCount.Should().BeGreaterThan(defaultCtx.PageCount,
            "60px rows are 3× taller than default 20px rows so fewer rows fit per page and more pages are needed");
        // With 60px rows: ~17 rows/page → 60 rows → 4 pages (ceil(60/17)=4). Default: 1 page (60 < 51 is false → 2 pages actually).
        defaultCtx.PageCount.Should().Be(2, "60 rows at default 20px height: ~51 per page → 2 row pages");
        tallCtx.PageCount.Should().BeGreaterThanOrEqualTo(3, "60 rows at 60px height: ~17 per page → at least 3 row pages");
    }

    private static PageContentLayout? BuildFirstPage(Workbook workbook, Sheet sheet) =>
        PageContentRenderModelBuilder.Build(workbook, sheet, Paginate(sheet), 0, Measurer, new DateTime(2026, 1, 1));

    private static PagePaginationResult Paginate(Sheet sheet)
    {
        var printRange = sheet.PrintArea ?? sheet.GetUsedRange()
            ?? new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        return PagePaginationPlanner.Paginate(
            printRange,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.PaperSize,
            sheet.PageOrientation,
            sheet.PageMargins,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks);
    }

    private static (Workbook Workbook, Sheet Sheet) CreateWorkbook(string name = "Book1.xlsx")
    {
        var workbook = new Workbook { Name = name };
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    private static void PopulateChartSource(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(14));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(11));
    }

    private static ChartModel CreatePrintedChart(Sheet sheet, string title, double left, double top) =>
        new()
        {
            Type = ChartType.Column,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            Title = title,
            Left = left,
            Top = top,
            Width = 260,
            Height = 180,
        };
}
