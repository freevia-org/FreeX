using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R72-render-cf-visual-4-1: the Avalonia/portable PDF export path (<see cref="PortablePdfPageContentPlanner"/>
/// + <see cref="WorkbookPdfContentBuilder"/>) previously built every cell purely from its raw
/// <see cref="StyleId"/> and never evaluated the sheet's <see cref="Sheet.ConditionalFormats"/>, so a
/// color-scale or cell-highlight fill that renders correctly on screen and in print preview
/// (<c>PageContentRenderModelBuilder</c>, the WPF/Skia-shared render model) was silently dropped from the
/// portable PDF. These tests cover the fill-only CF overlay added to <see cref="PortablePdfPageContentPlanner.CreatePlan(Workbook, PortablePdfExportPageRequest)"/>
/// and consumed by <see cref="WorkbookPdfContentBuilder"/>.
/// </summary>
public sealed class PortablePdfConditionalFormatFillTests
{
    [Fact]
    public void CreatePlan_CellValueHighlightRuleOverridesRawStyleFillInConditionalFillColor()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);

        // Raw (unconditional) style is plain white -- the CF rule below must win over it.
        var rawStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 255, 255) });
        var cell = Cell.FromValue(new NumberValue(150));
        cell.StyleId = rawStyle;
        sheet.SetCell(address, cell);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            AppliesTo = new GridRange(address, address),
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) },
        });

        var plan = CreateSinglePagePlan(workbook, sheet, "A1:A1");

        var pdfCell = plan.Cells.Single(c => c.Row == 1 && c.Column == 1);
        pdfCell.StyleId.Should().Be(rawStyle, "the cell's raw style is unchanged -- only the resolved fill is overridden");
        pdfCell.ConditionalFillColor.Should().Be(new CellColor(255, 0, 0),
            "the matched CF highlight rule's fill must be carried on the cell, not the raw white style's fill");
    }

    [Fact]
    public void CreatePlan_CellValueRuleDoesNotApplyWhenConditionNotMet()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);

        var cell = Cell.FromValue(new NumberValue(50)); // does not satisfy ">100"
        sheet.SetCell(address, cell);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            AppliesTo = new GridRange(address, address),
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) },
        });

        var plan = CreateSinglePagePlan(workbook, sheet, "A1:A1");

        var pdfCell = plan.Cells.Single(c => c.Row == 1 && c.Column == 1);
        pdfCell.ConditionalFillColor.Should().BeNull("a non-matching CF rule must not contribute a fill override");
    }

    [Fact]
    public void CreatePlan_ColorScaleRuleInterpolatesFillFromRangeValues()
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
            RuleType = CfRuleType.ColorScale,
            AppliesTo = new GridRange(a1, a3),
            UseThreeColorScale = false,
            MinColor = new RgbColor(0, 0, 0),
            MaxColor = new RgbColor(255, 255, 255),
            MinThresholdType = CfThresholdType.Min,
            MaxThresholdType = CfThresholdType.Max,
        });

        var plan = CreateSinglePagePlan(workbook, sheet, "A1:A3");

        plan.Cells.Single(c => c.Row == 1).ConditionalFillColor.Should().Be(new CellColor(0, 0, 0));
        plan.Cells.Single(c => c.Row == 3).ConditionalFillColor.Should().Be(new CellColor(255, 255, 255));
        plan.Cells.Single(c => c.Row == 2).ConditionalFillColor.Should().Be(new CellColor(128, 128, 128),
            "the middle value must resolve to the interpolated midpoint between the scale's min and max colors, " +
            "matching the print-preview path's color-scale evaluation (PageContentRenderModelBuilderConditionalFormattingTests)");
    }

    [Fact]
    public void CreatePlan_SheetWithNoConditionalFormatsLeavesFillNull()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        var rawStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(10, 20, 30) });
        var cell = Cell.FromValue(new NumberValue(200));
        cell.StyleId = rawStyle;
        sheet.SetCell(address, cell);

        var plan = CreateSinglePagePlan(workbook, sheet, "A1:A1");

        var pdfCell = plan.Cells.Single(c => c.Row == 1 && c.Column == 1);
        pdfCell.ConditionalFillColor.Should().BeNull("a sheet with no conditional formats must be unaffected by the CF overlay");
        pdfCell.StyleId.Should().Be(rawStyle);
    }

    [Fact]
    public void BuildWithPageSetup_CellValueHighlightRuleFillAppearsInExportedPdfOps()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);

        var rawStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 255, 255) });
        var cell = Cell.FromValue(new NumberValue(150));
        cell.StyleId = rawStyle;
        sheet.SetCell(address, cell);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            AppliesTo = new GridRange(address, address),
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) },
        });

        var doc = BuildDocument(workbook);

        var fillOps = doc.Pages[0].Ops.OfType<PdfFillRect>().ToList();
        fillOps.Should().ContainSingle(
            "exactly one cell fill rect is expected for a single-cell sheet")
            .Which.Color.Should().Be(new PdfColor(255, 0, 0),
                "the exported PDF fill must reflect the matched CF highlight rule, not the cell's raw white style");
    }

    [Fact]
    public void BuildWithPageSetup_SheetWithNoConditionalFormatsRendersRawStyleFillUnchanged()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        var rawStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(10, 20, 30) });
        var cell = Cell.FromValue(new NumberValue(200));
        cell.StyleId = rawStyle;
        sheet.SetCell(address, cell);

        var doc = BuildDocument(workbook);

        var fillOps = doc.Pages[0].Ops.OfType<PdfFillRect>().ToList();
        fillOps.Should().ContainSingle()
            .Which.Color.Should().Be(new PdfColor(10, 20, 30),
                "with no conditional formats on the sheet, the raw style's fill must render exactly as before this fix");
    }

    private static PdfContentDocument BuildDocument(Workbook workbook)
    {
        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

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
