using FluentAssertions;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R54-meta-2: <see cref="WorkbookPdfContentBuilder.BuildWithPageSetup"/>'s alignment-aware text
/// placement must not add the cell's <see cref="CellStyle.IndentLevel"/> to
/// <see cref="HorizontalAlignment.Fill"/>-aligned text. Excel's Format Cells indent stepper is
/// disabled for Fill, and both the on-screen GridView viewport (GridView.Rendering.cs's
/// DoesHorizontalAlignmentConsumeIndent excludes Fill) and the canonical
/// CellTextOrientationLayoutPlanner (Fill => cellRect.Left + 2, no indent term) render a
/// Fill-aligned cell flush-left with no indent, even when a leftover nonzero IndentLevel is present
/// on the style (e.g. carried over from a prior Left-aligned state or a loaded XLSX).
/// </summary>
public sealed class PdfCellFillAlignmentIndentTests
{
    [Fact]
    public void BuildWithPageSetup_FillAlignedCellWithIndent_IgnoresIndentLikeLeftFlushZeroIndent()
    {
        // Pre-fix, Fill falls into the shared default arm (`_ => x + 2*textScale + indentPt`), so a
        // nonzero IndentLevel shifts the Fill-aligned text rightward just like Left/General would.
        // Post-fix, Fill must land at the same flush-left x as a zero-indent Left/General cell,
        // matching the on-screen viewport and the canonical layout planner.
        var fillWithIndentX = BuildSingleCellPdfTextX(HorizontalAlignment.Fill, indentLevel: 3);
        var fillNoIndentX = BuildSingleCellPdfTextX(HorizontalAlignment.Fill, indentLevel: 0);

        fillWithIndentX.Should().BeApproximately(fillNoIndentX, 0.001,
            "Fill alignment must ignore IndentLevel and always render flush-left, " +
            "matching the on-screen GridView and CellTextOrientationLayoutPlanner");
    }

    [Fact]
    public void BuildWithPageSetup_LeftAlignedCellWithIndent_StillShiftsRightByIndent()
    {
        // Sibling no-regression check: excluding Fill from the indent-adding arm must not affect
        // Left (or General/default) alignment -- a nonzero indent must still shift that text right,
        // exactly as before this fix.
        var leftWithIndentX = BuildSingleCellPdfTextX(HorizontalAlignment.Left, indentLevel: 3);
        var leftNoIndentX = BuildSingleCellPdfTextX(HorizontalAlignment.Left, indentLevel: 0);

        leftWithIndentX.Should().BeGreaterThan(leftNoIndentX + 1,
            "Left alignment must still honor a nonzero IndentLevel by shifting the text rightward");
    }

    private static double BuildSingleCellPdfTextX(HorizontalAlignment alignment, int indentLevel)
    {
        var workbook = new Workbook("FillIndent");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ColumnWidths[1] = 40; // wide column -- much larger than the short cell text.

        var style = workbook.RegisterStyle(new CellStyle
        {
            HorizontalAlignment = alignment,
            IndentLevel = indentLevel,
        });
        var cell = Cell.FromValue(new TextValue("Hi"));
        cell.StyleId = style;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

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

        var op = doc.Pages[0].Ops.OfType<PdfText>().First(t => t.Text.Contains("Hi"));
        return op.X;
    }
}
