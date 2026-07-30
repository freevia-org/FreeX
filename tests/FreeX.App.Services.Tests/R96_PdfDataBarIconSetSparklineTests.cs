using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R96-render-cf-databar-iconset-1 / R96-render-sparkline-pdf-1: the Avalonia/portable PDF export path
/// (<see cref="PortablePdfPageContentPlanner"/> + <see cref="WorkbookPdfContentBuilder"/>) previously
/// carried only a cell's fill-color conditional format (R72) and completely dropped data-bar and
/// icon-set conditional formats, plus every in-cell sparkline, even though all three render correctly
/// on screen (ConditionalDataBarPanel.cs / ConditionalFormatIconGlyphFactory.cs / SparklineCellPanel.cs).
/// These tests drive the real product entry point -- <c>WorkbookExportPrintPlanner</c> →
/// <c>PortablePdfExportPlanner</c> → <see cref="WorkbookPdfContentBuilder.BuildWithPageSetup"/> -- the
/// same call chain <c>SkiaPdfDocumentExporter</c>/<c>AvaloniaPdfDocumentExporter</c> use, and assert ink
/// is actually emitted, not merely that state is readable.
/// </summary>
public sealed class R96_PdfDataBarIconSetSparklineTests
{
    [Fact]
    public void CreatePlan_DataBarRuleIsCarriedOnThePortablePdfPageCell()
    {
        var (workbook, sheet) = CreateWorkbook();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a1, new NumberValue(0));
        sheet.SetCell(a2, new NumberValue(100));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.DataBar,
            AppliesTo = new GridRange(a1, a2),
            DataBarMinThresholdType = CfThresholdType.AutoMin,
            DataBarMaxThresholdType = CfThresholdType.AutoMax,
        });

        var plan = CreateSinglePagePlan(workbook, sheet, "A1:A2");

        var minCell = plan.Cells.Single(c => c.Row == 1);
        var maxCell = plan.Cells.Single(c => c.Row == 2);
        // The minimum value legitimately resolves to a zero-length bar -- Excel (and
        // ConditionalFormatEvaluator.EvaluateDataBar, the same evaluator PageContentRenderModelBuilder's
        // print-preview path uses) reports that as "no bar" (null), not a bar of length zero.
        minCell.DataBar.Should().BeNull();
        maxCell.DataBar.Should().NotBeNull();
        maxCell.DataBar!.Value.EndFraction.Should().BeApproximately(1.0, 1e-9,
            "the max-value cell's bar should span the full width of the auto min/max range");
    }

    [Fact]
    public void BuildWithPageSetup_DataBarRuleEmitsAnExtraFillRectForTheBarInk()
    {
        var (workbook, sheet) = CreateWorkbook();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a1, new NumberValue(0));
        sheet.SetCell(a2, new NumberValue(100));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.DataBar,
            AppliesTo = new GridRange(a1, a2),
            DataBarMinThresholdType = CfThresholdType.AutoMin,
            DataBarMaxThresholdType = CfThresholdType.AutoMax,
        });

        var doc = BuildDocument(workbook);

        // Neither cell has a raw style fill or a matched fill-producing CF rule, so before this fix
        // zero fill rects would appear anywhere on the page; the data-bar rule must draw exactly one
        // fill rect (the max-value cell's bar; the min-value cell legitimately has none) in the rule's
        // configured bar color.
        var fillOps = doc.Pages[0].Ops.OfType<PdfFillRect>().ToList();
        fillOps.Should().ContainSingle(
                "the data-bar ink must be drawn as a fill rect or Print/PDF silently drops the bar the user sees on screen")
            .Which.Color.Should().Be(new PdfColor(99, 142, 198), "matching ConditionalFormat.DataBarColor's default");
    }

    [Fact]
    public void CreatePlan_IconSetRuleIsCarriedOnThePortablePdfPageCell()
    {
        var (workbook, sheet) = CreateWorkbook();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new NumberValue(0));
        sheet.SetCell(a2, new NumberValue(50));
        sheet.SetCell(a3, new NumberValue(100));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.IconSet,
            AppliesTo = new GridRange(a1, a3),
            IconSetStyle = "3TrafficLights1",
        });

        var plan = CreateSinglePagePlan(workbook, sheet, "A1:A3");

        var topCell = plan.Cells.Single(c => c.Row == 3);
        topCell.IconSet.Should().NotBeNull();
        topCell.IconSet!.Value.IconCount.Should().Be(3);
        topCell.IconSet!.Value.BucketIndex.Should().Be(2, "the maximum value should resolve to the best (highest) bucket");
    }

    [Fact]
    public void BuildWithPageSetup_IconSetRuleEmitsGlyphInk()
    {
        var (workbook, sheet) = CreateWorkbook();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new NumberValue(0));
        sheet.SetCell(a2, new NumberValue(50));
        sheet.SetCell(a3, new NumberValue(100));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.IconSet,
            AppliesTo = new GridRange(a1, a3),
            IconSetStyle = "3TrafficLights1",
        });

        var doc = BuildDocument(workbook);

        // "3TrafficLights1" always draws a filled ellipse per cell (FilledEllipse) -- this is the
        // glyph ink a viewer actually sees; before the fix, zero such ops existed anywhere.
        doc.Pages[0].Ops.OfType<PdfFillEllipse>().Should().NotBeEmpty(
            "the traffic-light icon-set glyph must be drawn as filled ellipses, not silently dropped");
    }

    [Fact]
    public void BuildWithPageSetup_LineSparklineEmitsLineSegmentInk()
    {
        var (workbook, sheet) = CreateWorkbook();
        var dataRow1 = new CellAddress(sheet.Id, 1, 1);
        var dataRow2 = new CellAddress(sheet.Id, 1, 2);
        var dataRow3 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(dataRow1, new NumberValue(1));
        sheet.SetCell(dataRow2, new NumberValue(9));
        sheet.SetCell(dataRow3, new NumberValue(3));

        var location = new CellAddress(sheet.Id, 2, 1);
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(dataRow1, dataRow3),
            Location = location,
            Kind = SparklineKind.Line,
        });

        var doc = BuildDocument(workbook, "A1:C2");

        doc.Pages[0].Ops.OfType<PdfLine>().Should().NotBeEmpty(
            "a line sparkline must be drawn as connected line segments in the exported PDF, matching what the interactive grid shows");
    }

    [Fact]
    public void BuildWithPageSetup_ColumnSparklineEmitsBarFillInk()
    {
        var (workbook, sheet) = CreateWorkbook();
        var dataRow1 = new CellAddress(sheet.Id, 1, 1);
        var dataRow2 = new CellAddress(sheet.Id, 1, 2);
        var dataRow3 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(dataRow1, new NumberValue(1));
        sheet.SetCell(dataRow2, new NumberValue(9));
        sheet.SetCell(dataRow3, new NumberValue(3));

        var location = new CellAddress(sheet.Id, 2, 1);
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(dataRow1, dataRow3),
            Location = location,
            Kind = SparklineKind.Column,
        });

        var doc = BuildDocument(workbook, "A1:C2");

        // The sparkline's anchor cell (row 2) has no display text/fill of its own, so any fill rects
        // that land inside that cell's row must be the sparkline's own column bars.
        var fillRectCount = doc.Pages[0].Ops.OfType<PdfFillRect>().Count();
        fillRectCount.Should().BeGreaterThan(0,
            "a column sparkline must draw its bars as fill rects, not silently disappear from the exported PDF");
    }

    [Fact]
    public void BuildWithPageSetup_SheetWithNoDataBarIconSetOrSparklineIsUnaffected()
    {
        // No-regression sibling: a perfectly ordinary sheet (plain fill/text CF, matching
        // PortablePdfConditionalFormatFillTests's existing coverage) must render byte-for-byte the
        // same op shape as before this change -- one base fill rect, no ellipses/paths/lines beyond
        // what vector-drawing objects (charts/pictures/text boxes) would otherwise add.
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        var rawStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(10, 20, 30) });
        var cell = Cell.FromValue(new NumberValue(200));
        cell.StyleId = rawStyle;
        sheet.SetCell(address, cell);

        var doc = BuildDocument(workbook, "A1:A1");

        var fillOps = doc.Pages[0].Ops.OfType<PdfFillRect>().ToList();
        fillOps.Should().ContainSingle()
            .Which.Color.Should().Be(new PdfColor(10, 20, 30));
        doc.Pages[0].Ops.OfType<PdfFillEllipse>().Should().BeEmpty();
        doc.Pages[0].Ops.OfType<PdfPath>().Should().BeEmpty();
    }

    private static PdfContentDocument BuildDocument(Workbook workbook, string? selectedRange = null)
    {
        var intent = selectedRange is null
            ? new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: 0)
            : new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.SelectedRange,
                WorkbookExportPrintOutputKind.Pdf,
                SelectedRange: GridRange.Parse(selectedRange, workbook.GetSheetAt(0)!.Id));

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        exportPlan.IsReady.Should().BeTrue(exportPlan.StatusText);

        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        pdfPlan.IsReady.Should().BeTrue(pdfPlan.StatusText);

        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);
        doc.Pages.Should().NotBeEmpty();
        return doc;
    }

    private static PortablePdfPageContentPlan CreateSinglePagePlan(Workbook workbook, Sheet sheet, string range)
    {
        var exportPrintPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.SelectedRange,
                WorkbookExportPrintOutputKind.Pdf,
                SelectedRange: GridRange.Parse(range, sheet.Id)),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 10, ColumnsPerPage: 10),
            WorkbookExportPrintSurface.MacOs);

        var exportPlan = PortablePdfExportPlanner.CreatePlan(exportPrintPlan);
        return PortablePdfPageContentPlanner.CreatePlan(workbook, exportPlan.PageRequests.Single());
    }

    private static (Workbook Workbook, Sheet Sheet) CreateWorkbook(string name = "Book1.xlsx")
    {
        var workbook = new Workbook { Name = name };
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }
}
