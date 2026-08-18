using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// Regression coverage for the printed/exported counterpart of the r141 chart hidden-data leak. The
/// on-screen chart reads <c>ViewportService.BuildChartDataCells</c>, which OMITS cells in hidden rows/
/// columns when <see cref="ChartModel.ShowDataInHiddenRowsAndColumns"/> is off. The print/PDF chart
/// text overlays (<see cref="PrintChartTextOverlayPlanner"/>) instead seed their lookup from a
/// PAGE-scoped cell lookup and only overlay the chart data cells on top, so anything the chart data
/// cells omit falls through to the page's un-filtered real value:
///
/// <list type="bullet">
/// <item>the portable page-model path (<see cref="PageContentRenderModelBuilder"/>, used by the
/// Avalonia print preview and by <c>WorkbookPdfContentBuilder</c>'s PDF export on BOTH platforms)
/// passed <c>chartDataCells: null</c> and a lookup that added every cell of the chart's DataRange
/// unconditionally -- so plain (non-merged) hidden rows/columns leaked into printed data labels,
/// tick labels and legend entries;</item>
/// <item>the WPF path (<c>PrintRenderer.ChartTextOverlays</c>) seeds from <c>ViewportModel.Cells</c>,
/// which deliberately retains hidden merge-ANCHOR rows -- the exact r141 cell class.</item>
/// </list>
///
/// Both are exercised below with real production types (real <see cref="Workbook"/>/<see cref="Sheet"/>,
/// real <see cref="ViewportService"/>), not a hand-built lookup.
/// </summary>
public sealed class PageContentRenderModelBuilderChartHiddenDataOverlayTests
{
    private const string HiddenCategory = "Zeta";
    private const double HiddenValue = 777777;
    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Build_PrintedChartOverlaysOmitHiddenRowDataWhenChartHidesIt()
    {
        var (workbook, sheet) = CreateChartWorkbook(showDataInHiddenRowsAndColumns: false);
        sheet.HiddenRows.Add(3); // the row carrying HiddenCategory / HiddenValue

        var texts = BuildOverlayTexts(workbook, sheet);

        texts.Should().NotBeEmpty("the printed chart must still plan its own overlays");
        texts.Should().NotContain(text => text.Contains(HiddenCategory, StringComparison.Ordinal));
        texts.Should().NotContain(text => text.Contains("777777", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_PrintedChartOverlaysKeepHiddenRowDataWhenChartShowsIt()
    {
        var (workbook, sheet) = CreateChartWorkbook(showDataInHiddenRowsAndColumns: true);
        sheet.HiddenRows.Add(3);

        var texts = BuildOverlayTexts(workbook, sheet);

        texts.Should().Contain(text => text.Contains(HiddenCategory, StringComparison.Ordinal));
    }

    [Fact]
    public void Build_PrintedChartOverlaysOmitHiddenColumnDataWhenChartHidesIt()
    {
        var (workbook, sheet) = CreateChartWorkbook(showDataInHiddenRowsAndColumns: false);
        sheet.HiddenCols.Add(1); // the whole category column

        var texts = BuildOverlayTexts(workbook, sheet);

        texts.Should().NotBeEmpty();
        // Only the CATEGORY column is hidden here, so the (visible) value column still plots -- the
        // category text must fall back to the ordinal placeholder instead of the hidden cell text,
        // exactly as ViewportService.BuildChartDataCells's per-cell row/col predicate implies.
        texts.Should().NotContain(text => text.Contains(HiddenCategory, StringComparison.Ordinal));
        texts.Should().NotContain(text => text.Contains("Jan", StringComparison.Ordinal));
        texts.Should().Contain("1");
    }

    [Fact]
    public void Build_WpfPrintPathOverlaysOmitHiddenMergeAnchorRowData()
    {
        // The WPF print renderer seeds PrintChartTextOverlayPlanner from ViewportModel.Cells and
        // ViewportModel.ChartDataCells. A hidden merge-ANCHOR row with a still-visible remainder is
        // deliberately kept in ViewportModel.Cells (ViewportService.BuildRowMetrics), so it reaches
        // the planner as a real page cell.
        //
        // Two independent mechanisms now keep it out of the printed overlays, and this test guards
        // both ends of that: ViewportService.BuildChartDataCells claims the anchor's key with a BLANK
        // placeholder (the r141 fix, which exists precisely so a page-cell fallback cannot re-admit
        // the value), and BuildCellLookup filters hidden cells out of the page seed. The placeholder
        // alone covers merge anchors only -- the plain hidden row/column cases above are the ones that
        // fail without the seed filter -- so this case is defense in depth against either mechanism
        // regressing on its own.
        var (workbook, sheet) = CreateChartWorkbook(showDataInHiddenRowsAndColumns: false);
        sheet.HiddenRows.Add(3);
        sheet.ReplaceMergedRegions(
        [
            new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 4, 1)),
            new GridRange(new CellAddress(sheet.Id, 3, 2), new CellAddress(sheet.Id, 4, 2))
        ]);

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(TopRow: 1, LeftCol: 1, AvailableHeight: 2000, AvailableWidth: 2000));

        // Precondition: the leak source really is present in the page-scoped seed.
        var pageCellLookup = viewport.Cells.ToDictionary(cell => (cell.Row, cell.Col));
        pageCellLookup.Should().ContainKey((3u, 1u), "the hidden merge-anchor row stays in ViewportModel.Cells");
        pageCellLookup[(3u, 1u)].DisplayText.Should().Be(HiddenCategory,
            "the page seed carries the hidden anchor's REAL text -- that is the leak source");
        viewport.ChartDataCells.Where(cell => cell.Row == 3)
            .Should().OnlyContain(cell => cell.DisplayText == "",
                "ViewportService exposes hidden merge anchors only as blank placeholders, never as their real value");

        var texts = PrintChartTextOverlayPlanner.Build(
                sheet.Charts[0],
                workbook.Theme,
                new LayoutRect(0, 0, 380, 210),
                viewport.ChartDataCells,
                pageCellLookup,
                (text, fontSize) => new PrintChartOverlayTextMetrics(text.Length * fontSize * 0.6, text.Length * fontSize * 0.6),
                sheet)
            .Select(overlay => overlay.Text)
            .ToList();

        texts.Should().NotBeEmpty();
        texts.Should().NotContain(text => text.Contains(HiddenCategory, StringComparison.Ordinal));
        texts.Should().NotContain(text => text.Contains("777777", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> BuildOverlayTexts(Workbook workbook, Sheet sheet)
    {
        var layout = PageContentRenderModelBuilder.Build(
            workbook,
            sheet,
            Paginate(sheet),
            0,
            Measurer,
            new DateTime(2026, 1, 1));
        layout.Should().NotBeNull();
        return layout!.Charts
            .SelectMany(chart => chart.TextOverlays)
            .Select(overlay => overlay.Text)
            .ToList();
    }

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

    private static (Workbook Workbook, Sheet Sheet) CreateChartWorkbook(bool showDataInHiddenRowsAndColumns)
    {
        var workbook = new Workbook { Name = "Book1.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue(HiddenCategory));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(HiddenValue));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(11));
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 20, 8));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Title = "Printed chart",
            Left = 24,
            Top = 24,
            Width = 380,
            Height = 210,
            ShowLegend = true,
            LegendPosition = ChartLegendPosition.Right,
            ShowDataLabels = true,
            ShowDataLabelCategoryName = true,
            ShowDataLabelValue = true,
            ShowDataInHiddenRowsAndColumns = showDataInHiddenRowsAndColumns
        });

        return (workbook, sheet);
    }
}
