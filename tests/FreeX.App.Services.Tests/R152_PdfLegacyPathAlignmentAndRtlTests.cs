using System.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// shared-localization-rtl-F1: <see cref="WorkbookPdfContentBuilder.BuildPage"/> -- the legacy
/// fixed-geometry sibling of <see cref="WorkbookPdfContentBuilder.BuildPageWithPageSetup"/> -- never
/// called <see cref="FreeX.Core.Calc.CellTextOrientationLayoutPlanner"/> at all: every cell's text was
/// drawn at a fixed <c>x + 4</c> offset regardless of <c>style.HorizontalAlignment</c> or whether the
/// content was numeric, and its one RTL-aware call site (<c>DrawConditionalIconSet</c>) hardcoded
/// <c>isRightToLeft: false</c> even when <c>Sheet.IsRightToLeft</c> was true. This path is not dead
/// code: it is the exclusive builder behind <see cref="WorkbookPdfContentBuilder.Build"/>, which
/// <see cref="PortablePdfDocumentExporter"/>'s <c>CreateDocument</c> unconditionally calls, and
/// <see cref="PortablePdfDocumentExporter.Save"/> is the documented Skia-unavailable fallback wired
/// into the Avalonia shell's Save-As-PDF command (<c>AvaloniaPdfDocumentExporter.Save</c>).
/// </summary>
public sealed class R152_PdfLegacyPathAlignmentAndRtlTests
{
    [Fact]
    public void BuildPage_NumericGeneralAlignment_RightAlignsTextInsteadOfFlushLeft()
    {
        var workbook = new Workbook("Numbers");
        var sheet = workbook.AddSheet("Sheet1");
        // General alignment (the CellStyle default) on numeric content must resolve to Right, matching
        // Excel and the on-screen viewport (CellTextOrientationLayoutPlanner.
        // ResolveEffectiveHorizontalAlignment) -- before the fix this legacy path never consulted
        // HorizontalAlignment at all and drew every cell flush-left.
        var cell = Cell.FromValue(new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var exportPlan = CreateExportPlan(workbook, sheet);
        var options = new PortablePdfDocumentOptions();

        var document = WorkbookPdfContentBuilder.Build(workbook, exportPlan, options);
        document.Pages.Should().NotBeEmpty();

        var textOp = document.Pages[0].Ops.OfType<PdfText>().Should().ContainSingle(t => t.Text == "42").Which;

        // gridLeft = options.MarginPoints (36); a flush-left draw would land at exactly x + 4 = 40.
        // A right-aligned numeric value in a ~118pt-wide single column must land well to the right of
        // that flush-left position.
        var flushLeftX = options.MarginPoints + 4;
        textOp.X.Should().BeGreaterThan(flushLeftX + 20,
            "a General-aligned numeric cell must render right-aligned on the legacy PDF path too, " +
            "matching the page-setup-aware path and the on-screen viewport, instead of flush-left");
    }

    [Fact]
    public void BuildPage_TextGeneralAlignment_StillRendersFlushLeft()
    {
        // No-regression sibling: General-aligned TEXT content (as opposed to numeric content above)
        // must still resolve to Left and render flush-left, both before and after the fix -- proving
        // the alignment fix only changes numeric/date General cells, not text ones.
        var workbook = new Workbook("Words");
        var sheet = workbook.AddSheet("Sheet1");
        var cell = Cell.FromValue(new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var exportPlan = CreateExportPlan(workbook, sheet);
        var options = new PortablePdfDocumentOptions();

        var document = WorkbookPdfContentBuilder.Build(workbook, exportPlan, options);
        document.Pages.Should().NotBeEmpty();

        var textOp = document.Pages[0].Ops.OfType<PdfText>().Should().ContainSingle(t => t.Text == "Region").Which;

        textOp.X.Should().Be(options.MarginPoints + 4,
            "General-aligned text content resolves to Left, and must still render at the legacy path's " +
            "existing flush-left offset unchanged");
    }

    [Fact]
    public void BuildPage_IconSetOnRightToLeftSheet_MirrorsGlyphTowardTheCellsRightEdge()
    {
        // The legacy path's one RTL-aware call site (DrawConditionalIconSet) hardcoded
        // isRightToLeft: false, so an icon-set glyph on an RTL sheet always rendered pinned to the
        // cell's LEFT edge (ConditionalIconCellLayoutPlanner.CalculateCellLayout mirrors the glyph to
        // the right edge when isRightToLeft is true). Compare the same rule/value on an LTR sheet
        // against an RTL sheet: the RTL glyph must land further right than the LTR glyph.
        var ltrEllipse = SingleIconSetEllipseX(isRightToLeft: false);
        var rtlEllipse = SingleIconSetEllipseX(isRightToLeft: true);

        rtlEllipse.Should().BeGreaterThan(ltrEllipse,
            "an icon-set glyph on a right-to-left sheet must mirror toward the cell's right edge on the " +
            "legacy PDF path too, instead of always rendering as if the sheet were left-to-right");
    }

    private static double SingleIconSetEllipseX(bool isRightToLeft)
    {
        var workbook = new Workbook("Icons");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsRightToLeft = isRightToLeft;

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

        var exportPlan = CreateExportPlan(workbook, sheet);
        var options = new PortablePdfDocumentOptions();

        var document = WorkbookPdfContentBuilder.Build(workbook, exportPlan, options);
        document.Pages.Should().NotBeEmpty();

        // "3TrafficLights1" always draws a filled ellipse per cell -- take the top-value cell's glyph.
        var ellipses = document.Pages[0].Ops.OfType<PdfFillEllipse>().ToList();
        ellipses.Should().NotBeEmpty("the traffic-light icon-set glyph must be drawn as filled ellipses");
        return ellipses.Min(e => e.X);
    }

    private static PortablePdfExportPlan CreateExportPlan(Workbook workbook, Sheet sheet)
    {
        var printPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: 0),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 5),
            WorkbookExportPrintSurface.MacOs);

        printPlan.IsReady.Should().BeTrue(printPlan.StatusText);
        return PortablePdfExportPlanner.CreatePlan(printPlan);
    }
}
