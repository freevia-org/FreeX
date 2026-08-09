using FluentAssertions;
using Free.Shared.Pdf;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R128-services-pdf-chart-plot-kinds-4: <c>WorkbookPdfContentBuilder.AddChartPlotOps</c> switched on
/// <see cref="SeriesGeometryKind"/> but only implemented <c>Columns</c>/<c>Bars</c> and <c>Line</c> --
/// every other kind the layout engine produces (<c>Area</c>, <c>ScatterPoints</c>, <c>PieSlices</c>,
/// <c>Bubbles</c>, <c>RadarPolyline</c>, <c>StockBars</c>, <c>BoxWhiskers</c>, <c>TreemapTiles</c>,
/// <c>SurfaceCells</c>) fell through the switch silently, so Pie/Scatter/Radar/Stock/Area/Bubble/
/// Box-and-Whisker/Treemap/Surface charts printed and exported to PDF as an empty bordered box --
/// only the chart-area fill/outline rectangle (drawn by the caller before <c>AddChartPlotOps</c> runs)
/// ever appeared. Both Avalonia File &gt; Export to PDF and File &gt; Print reach this same builder
/// (<c>MainWindow.Print.cs</c> documents Print deliberately reusing the PDF-export renderer), so the
/// gap affected both actions identically. The WPF host is unaffected: <c>PrintRenderer.DrawingObjects.cs</c>
/// draws charts via <c>ChartRenderer.Render</c>, the full on-screen renderer, not this switch.
///
/// These tests drive the real product entry point end-to-end
/// (<see cref="WorkbookExportPrintPlanner"/> -&gt; <see cref="PortablePdfExportPlanner"/> -&gt;
/// <see cref="WorkbookPdfContentBuilder"/>), matching the shape of the existing
/// <c>PortablePdfVectorDrawingTests</c> Column/Line coverage, and assert on the actual drawn ops --
/// never a hand-built <see cref="ChartLayout"/> model.
/// </summary>
public sealed class R128_PdfChartPlotAllKindsTests
{
    [Fact]
    public void BuildWithPageSetup_EmitsPieSlicePlotOpsFromSharedChartLayout()
    {
        var workbook = new Workbook { Name = "PieVectorEvidence.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        PopulateChartSource(sheet);
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = GridRange.Parse("A1:B4", sheet.Id),
            Title = "Pie vector chart",
            Left = 24,
            Top = 24,
            Width = 260,
            Height = 180,
        });
        var exportPlan = CreatePageSetupPdfPlan(workbook);

        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan);
        var ops = document.Pages.Should().ContainSingle().Subject.Ops;

        // THE FIX: a pie chart's three data points must each produce a filled wedge path (not just
        // the chart-area border rectangle from the caller).
        ops.OfType<PdfPath>()
            .Should()
            .HaveCount(3, "each of the three plotted slices should emit one filled wedge path")
            .And.OnlyContain(path => path.FillColor != null && path.Contours.Count == 1);
    }

    [Fact]
    public void BuildWithPageSetup_EmitsAreaChartFillAndStrokeOpsFromSharedChartLayout()
    {
        var workbook = new Workbook { Name = "AreaVectorEvidence.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        PopulateChartSource(sheet);
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Area,
            DataRange = GridRange.Parse("A1:B4", sheet.Id),
            Title = "Area vector chart",
            Left = 24,
            Top = 24,
            Width = 260,
            Height = 180,
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: new CellColor(91, 155, 213),
                    StrokeColor: new CellColor(47, 84, 150),
                    StrokeThickness: 2)
            ]
        });
        var exportPlan = CreatePageSetupPdfPlan(workbook);

        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan);
        var ops = document.Pages.Should().ContainSingle().Subject.Ops;

        // Translucent band fill (wrapped in an opacity group) plus a full-opacity stroke outline --
        // the area-chart sibling of the pie-slice fix above, proving the fix isn't a one-off special
        // case for pie geometry.
        ops.OfType<PdfOpacityGroup>()
            .SelectMany(group => group.Ops.OfType<PdfPath>())
            .Should()
            .Contain(path => path.FillColor == new PdfColor(91, 155, 213));
        ops.OfType<PdfPath>()
            .Should()
            .Contain(path => path.StrokeColor == new PdfColor(47, 84, 150) && path.FillColor == null);
    }

    [Fact]
    public void BuildWithPageSetup_ColumnAndLineChartPlotOpsAreUnaffectedByNewChartKinds()
    {
        // No-regression sibling: the two chart kinds AddChartPlotOps already handled (Columns/Bars via
        // AddChartBarOps, Line via AddChartLineOps) must keep emitting exactly the same ops after the
        // switch grew new cases alongside them.
        var workbook = new Workbook { Name = "ColumnLineRegression.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        PopulateChartSource(sheet);
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = GridRange.Parse("A1:B4", sheet.Id),
            Title = "Column regression chart",
            Left = 24,
            Top = 24,
            Width = 260,
            Height = 180,
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: new CellColor(91, 155, 213),
                    StrokeColor: new CellColor(47, 84, 150),
                    StrokeThickness: 0.75)
            ]
        });
        var exportPlan = CreatePageSetupPdfPlan(workbook);

        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan);
        var ops = document.Pages.Should().ContainSingle().Subject.Ops;

        ops.OfType<PdfFillRect>()
            .Where(rect => rect.Color == new PdfColor(91, 155, 213))
            .Should()
            .HaveCount(3, "the simple column chart should still emit one vector plot rectangle per data point");
    }

    private static PortablePdfExportPlan CreatePageSetupPdfPlan(Workbook workbook)
    {
        var printPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: 0));

        printPlan.IsReady.Should().BeTrue(printPlan.StatusText);
        return PortablePdfExportPlanner.CreatePlan(printPlan, workbook);
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
}
