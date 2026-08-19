using FluentAssertions;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// freex-conditional-format-F1: the Avalonia/shared portable PDF export path
/// (<see cref="PortablePdfPageContentPlanner"/> + <see cref="WorkbookPdfContentBuilder"/>) carried
/// only a matched conditional-format rule's fill color (<c>ConditionalFillColor</c>) -- font color,
/// bold, italic, per-edge border, and number format all silently reverted to the cell's raw,
/// unconditional style even though the on-screen grid (<c>ViewportConditionalFormatEvaluator.
/// MergeStyles</c>) and the interactive viewport apply all of them. These tests cover the new
/// <see cref="PortablePdfPageCell.ConditionalStyle"/> field and its consumption by both
/// <see cref="WorkbookPdfContentBuilder.BuildPageWithPageSetup"/> (the Avalonia/Skia + shared
/// page-setup-aware export path) and <see cref="WorkbookPdfContentBuilder.BuildPage"/> (the legacy
/// fixed-geometry path reachable from <c>PortablePdfDocumentExporter.CreateDocument</c> and, through
/// it, from <c>AvaloniaPdfDocumentExporter</c>'s Skia-unavailable fallback).
/// </summary>
public sealed class R151_PortablePdfConditionalFormatDifferentialStyleTests
{
    [Fact]
    public void CreatePlan_CellValueRuleWithFullDifferentialStyle_PopulatesConditionalStyleAndDisplayText()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);

        // Raw style is plain General/regular -- the CF rule below must win over all of it, not just fill.
        var cell = Cell.FromValue(new NumberValue(-150));
        sheet.SetCell(address, cell);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.LessThan,
            Value1 = "0",
            AppliesTo = new GridRange(address, address),
            FormatIfTrue = new CellStyle
            {
                Bold = true,
                Italic = true,
                FontColor = new CellColor(255, 0, 0),
                BorderTop = new CellBorder(BorderStyle.Thick, new CellColor(0, 0, 255)),
                NumberFormat = "0.00",
            },
        });

        var plan = CreateSinglePagePlan(workbook, sheet, "A1:A1");
        var pdfCell = plan.Cells.Single(c => c.Row == 1 && c.Column == 1);

        pdfCell.DisplayText.Should().Be("-150.00",
            "the matched CF rule's number format must override the raw General format, exactly like the grid");

        pdfCell.ConditionalStyle.Should().NotBeNull();
        var cf = pdfCell.ConditionalStyle!.Value;
        cf.Bold.Should().BeTrue("the CF rule's Bold must be carried, not silently dropped");
        cf.Italic.Should().BeTrue("the CF rule's Italic must be carried, not silently dropped");
        cf.FontColor.Should().Be(new CellColor(255, 0, 0), "the CF rule's font color must be carried");
        cf.BorderTop.Style.Should().Be(BorderStyle.Thick, "the CF rule's border must be carried");
        cf.BorderTop.Color.Should().Be(new CellColor(0, 0, 255));
        cf.NumberFormat.Should().Be("0.00", "the CF rule's number format must be carried alongside the fill");
    }

    [Fact]
    public void CreatePlan_FillOnlyRule_LeavesFontBoldItalicBorderAndNumberFormatUnset()
    {
        // Sibling no-regression: a CF rule that only sets a fill must not spuriously report
        // Bold/Italic/border/number-format as present -- ConditionalStyle's new fields must reflect
        // exactly what the rule specified, nothing more.
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromValue(new NumberValue(150));
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

        pdfCell.ConditionalFillColor.Should().Be(new CellColor(255, 0, 0));
        pdfCell.ConditionalStyle.Should().NotBeNull();
        var cf = pdfCell.ConditionalStyle!.Value;
        cf.Bold.Should().BeFalse();
        cf.Italic.Should().BeFalse();
        cf.BorderTop.Style.Should().Be(BorderStyle.None);
        cf.BorderRight.Style.Should().Be(BorderStyle.None);
        cf.BorderBottom.Style.Should().Be(BorderStyle.None);
        cf.BorderLeft.Style.Should().Be(BorderStyle.None);
        cf.NumberFormat.Should().BeNull();
    }

    [Fact]
    public void BuildWithPageSetup_CellValueRuleWithFullDifferentialStyle_AppearsInExportedPdfOps()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromValue(new NumberValue(-150));
        sheet.SetCell(address, cell);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.LessThan,
            Value1 = "0",
            AppliesTo = new GridRange(address, address),
            FormatIfTrue = new CellStyle
            {
                Bold = true,
                Italic = true,
                FontColor = new CellColor(255, 0, 0),
                BorderTop = new CellBorder(BorderStyle.Thick, new CellColor(0, 0, 255)),
                NumberFormat = "0.00",
            },
        });

        var doc = BuildDocument(workbook);
        var ops = doc.Pages[0].Ops;

        var textOp = ops.OfType<PdfText>().Should().ContainSingle(
            t => t.Text == "-150.00",
            "the exported PDF text must reflect the CF-driven number format, not the raw '-150'")
            .Which;
        textOp.Face.Should().Be(PdfFontFace.BoldItalic,
            "the CF rule's Bold+Italic must both reach the rendered PDF text face");
        textOp.Color.Should().Be(new PdfColor(255, 0, 0),
            "the CF rule's font color must reach the rendered PDF text, not the raw black");

        ops.OfType<PdfLine>().Should().Contain(
            l => l.Color == new PdfColor(0, 0, 255),
            "the CF rule's border must be drawn, not silently dropped");
    }

    [Fact]
    public void BuildWithPageSetup_SheetWithNoConditionalFormats_RendersRawStyleUnchanged()
    {
        // No-regression sibling at the renderer level: with no CF rules at all, bold/italic/border/
        // font-color/number-format must render exactly as the raw style dictates.
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        var rawStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00" });
        var cell = Cell.FromValue(new NumberValue(-150));
        cell.StyleId = rawStyle;
        sheet.SetCell(address, cell);

        var doc = BuildDocument(workbook);
        var ops = doc.Pages[0].Ops;

        var textOp = ops.OfType<PdfText>().Should().ContainSingle().Which;
        textOp.Text.Should().Be("-150.00");
        textOp.Face.Should().Be(PdfFontFace.Regular, "with no CF rules the raw (non-bold, non-italic) style must render unchanged");
        textOp.Color.Should().Be(PdfColor.Black);
        ops.OfType<PdfLine>().Should().BeEmpty("with no CF rules and no explicit border, no border lines should be drawn");
    }

    [Fact]
    public void BuildPage_LegacyPath_CellValueRuleWithFullDifferentialStyle_AppearsInExportedPdfOps()
    {
        // Sibling call site: BuildPage (the legacy fixed-geometry path) shares the same
        // PortablePdfPageCell.ConditionalStyle field and must not silently drop it either -- this path
        // is reachable in production from PortablePdfDocumentExporter.CreateDocument and, through it,
        // from AvaloniaPdfDocumentExporter's Skia-unavailable fallback (see R127B's identical coverage
        // for the plain, non-CF border case).
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromValue(new NumberValue(-150));
        sheet.SetCell(address, cell);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.LessThan,
            Value1 = "0",
            AppliesTo = new GridRange(address, address),
            FormatIfTrue = new CellStyle
            {
                Bold = true,
                FontColor = new CellColor(255, 0, 0),
                BorderBottom = new CellBorder(BorderStyle.Thick, new CellColor(0, 0, 255)),
                NumberFormat = "0.00",
            },
        });

        var exportPrintPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.SelectedRange,
                WorkbookExportPrintOutputKind.Pdf,
                SelectedRange: GridRange.Parse("A1:A1", sheet.Id)),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 10, ColumnsPerPage: 10),
            WorkbookExportPrintSurface.MacOs);
        var exportPlan = PortablePdfExportPlanner.CreatePlan(exportPrintPlan);
        exportPlan.IsReady.Should().BeTrue(exportPlan.StatusText);

        var options = new PortablePdfDocumentOptions();
        var page = WorkbookPdfContentBuilder.BuildPage(workbook, exportPlan, exportPlan.PageRequests.Single(), options);

        var textOp = page.Ops.OfType<PdfText>().Should().ContainSingle(t => t.Text == "-150.00").Which;
        textOp.Face.Should().Be(PdfFontFace.Bold, "the CF rule's Bold must reach the legacy path's rendered text");
        textOp.Color.Should().Be(new PdfColor(255, 0, 0));
        page.Ops.OfType<PdfLine>().Should().Contain(l => l.Color == new PdfColor(0, 0, 255),
            "the CF rule's border must be drawn on the legacy path too");
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
