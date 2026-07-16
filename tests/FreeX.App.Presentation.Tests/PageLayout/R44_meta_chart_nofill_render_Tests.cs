using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R44-meta-1: ChartModel.ChartAreaNoFill/ChartAreaNoLine/PlotAreaNoFill/PlotAreaNoLine round-tripped
/// through XLSX/.fxl/clone but were consumed nowhere -- every render consumer treated the resolved
/// <c>null</c> color the same whether the user had explicitly picked "No Fill"/"No Line" or simply
/// never configured a fill/border, so the explicit choice silently reverted to the opaque default.
///
/// These tests cover the two things testable from this test project (model resolution + the
/// PDF/print render-model built by <see cref="PageContentRenderModelBuilder"/>); the WPF
/// (GridView.DrawingObjects.cs / ChartRenderer.Axes.cs) and Avalonia (AvaloniaChartRenderer.cs)
/// interactive-grid consumers live in projects this test project does not reference and are fixed
/// via the same <see cref="ChartModel.IsChartAreaFillSuppressed"/>-style model helpers exercised here.
/// </summary>
public sealed class R44_meta_chart_nofill_render_Tests
{
    private static readonly FakeTextMeasurer Measurer = new();

    // ── Model-level resolution ─────────────────────────────────────────────

    [Fact]
    public void ChartAreaNoFill_IsChartAreaFillSuppressed_IsTrue()
    {
        var chart = new ChartModel { ChartAreaNoFill = true };

        chart.IsChartAreaFillSuppressed.Should().BeTrue();
        // The color resolver alone cannot distinguish "No Fill" from "nothing set" -- both are null.
        chart.ResolveChartAreaFillColor(WorkbookTheme.Office).Should().BeNull(
            "the reader clears ChartAreaFillColor/ChartAreaFillThemeColor whenever noFill is explicit");
    }

    [Fact]
    public void ChartAreaNoLine_IsChartAreaLineSuppressed_IsTrue()
    {
        var chart = new ChartModel { ChartAreaNoLine = true };
        chart.IsChartAreaLineSuppressed.Should().BeTrue();
    }

    [Fact]
    public void PlotAreaNoFill_IsPlotAreaFillSuppressed_IsTrue()
    {
        var chart = new ChartModel { PlotAreaNoFill = true };
        chart.IsPlotAreaFillSuppressed.Should().BeTrue();
    }

    [Fact]
    public void PlotAreaNoLine_IsPlotAreaLineSuppressed_IsTrue()
    {
        var chart = new ChartModel { PlotAreaNoLine = true };
        chart.IsPlotAreaLineSuppressed.Should().BeTrue();
    }

    // ── No-regression: unset / explicit-color cases must NOT be treated as suppressed ─────────────

    [Fact]
    public void NoFillFieldsUnset_SuppressionFlagsAreFalse()
    {
        var chart = new ChartModel();

        chart.IsChartAreaFillSuppressed.Should().BeFalse();
        chart.IsChartAreaLineSuppressed.Should().BeFalse();
        chart.IsPlotAreaFillSuppressed.Should().BeFalse();
        chart.IsPlotAreaLineSuppressed.Should().BeFalse();
    }

    [Fact]
    public void ExplicitChartAreaFillColor_IsNotTreatedAsSuppressed()
    {
        var chart = new ChartModel { ChartAreaFillColor = new CellColor(200, 20, 20) };

        chart.IsChartAreaFillSuppressed.Should().BeFalse();
        chart.ResolveChartAreaFillColor(WorkbookTheme.Office).Should().Be(new CellColor(200, 20, 20));
    }

    // ── PDF/print render-model (PageContentRenderModelBuilder) ─────────────

    [Fact]
    public void Build_ChartAreaNoLine_ResolvesToZeroOutlineThickness_SoPrintDrawsNoBorder()
    {
        var (workbook, sheet) = CreateWorkbook();
        PopulateChartSource(sheet);
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 12));

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Left = 0,
            Top = 0,
            Width = 200,
            Height = 150,
            ChartAreaNoLine = true,
            // Even if a stale border thickness/color is still present, the explicit "No Line" choice
            // must win -- the writer/reader always clear these alongside NoLine, but the renderer
            // must not silently re-derive a border from leftover fields either.
            ChartAreaBorderThickness = 2.5,
            ChartAreaBorderColor = new CellColor(0, 0, 0),
        };
        sheet.Charts.Add(chart);

        var layout = BuildFirstPage(workbook, sheet)!;
        var block = layout.Charts.Should().ContainSingle().Subject;

        // WorkbookPdfContentBuilder.AddStrokeRect is a no-op when lineWidth <= 0, so this fully
        // suppresses the printed chart-area border.
        block.OutlineThickness.Should().Be(0,
            "an explicit \"No Line\" chart area must print with no border at all, not the default outline");
    }

    [Fact]
    public void Build_ChartAreaWithoutNoLine_StillResolvesDefaultOutlineThickness()
    {
        var (workbook, sheet) = CreateWorkbook();
        PopulateChartSource(sheet);
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 12));

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Left = 0,
            Top = 0,
            Width = 200,
            Height = 150,
        };
        sheet.Charts.Add(chart);

        var layout = BuildFirstPage(workbook, sheet)!;
        var block = layout.Charts.Should().ContainSingle().Subject;

        // Sibling no-regression case: a chart that never opted into "No Line" keeps printing the
        // default 1.0pt outline exactly as before this fix.
        block.OutlineThickness.Should().Be(1.0);
    }

    [Fact]
    public void Build_ChartAreaWithExplicitBorderThicknessAndNoNoLine_UsesExplicitThickness()
    {
        var (workbook, sheet) = CreateWorkbook();
        PopulateChartSource(sheet);
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 12));

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Left = 0,
            Top = 0,
            Width = 200,
            Height = 150,
            ChartAreaBorderThickness = 3.0,
            ChartAreaBorderColor = new CellColor(10, 10, 10),
        };
        sheet.Charts.Add(chart);

        var layout = BuildFirstPage(workbook, sheet)!;
        var block = layout.Charts.Should().ContainSingle().Subject;

        block.OutlineThickness.Should().Be(3.0);
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
}
