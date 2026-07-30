using System.Collections.Generic;
using FluentAssertions;
using Free.Shared.Pdf;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Sibling coverage for the round-95 HIGH finding on the real PDF export entry point: a hidden row in
/// the INTERIOR of a print range must not shift a worksheet object anchored below it. Drives the actual
/// product pipeline -- <see cref="WorkbookExportPrintPlanner.CreatePlanFromPageSetup"/> +
/// <see cref="PortablePdfExportPlanner.CreatePlan"/> + <see cref="WorkbookPdfContentBuilder.BuildWithPageSetup"/>
/// -- so it exercises <c>WorkbookPdfContentBuilder</c>'s own plan-to-segment step (now delegating to
/// <see cref="PagePaginationPlanner.BuildSegments(System.Collections.Generic.IReadOnlyList{PrintPageRowPlan})"/>
/// instead of an independent lossy copy) end to end.
/// </summary>
public sealed class R95_PdfHiddenInteriorRowsTextBoxPositionTests
{
    [Fact]
    public void R95_BuildWithPageSetup_HiddenInteriorRowsDoNotShiftTextBoxAnchoredBelowThem()
    {
        var workbook = new Workbook { Name = "HiddenRowsPdf.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 10; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.PrintArea = GridRange.Parse("A1:A10", sheet.Id);
        // Rows 4 and 5 sit in the MIDDLE of the print range, not at either edge.
        sheet.HiddenRows.Add(4);
        sheet.HiddenRows.Add(5);
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 1),
            Text = "Above the hidden rows",
            Width = 100,
            Height = 20,
            FillColor = new CellColor(255, 0, 0),
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 6, 1),
            Text = "Below the hidden rows",
            Width = 100,
            Height = 20,
            FillColor = new CellColor(0, 255, 0),
        });

        var exportPlan = CreatePageSetupPdfPlan(workbook);
        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan);
        var ops = document.Pages.Should().ContainSingle().Subject.Ops;

        // Text-box fills are emitted via AddFillRect with FillAlpha < 255 (PageTextBoxLayoutPlanner's
        // fixed 242/255 fill alpha, matching the desktop print renderer), so AddFillRect wraps each one
        // in a PdfOpacityGroup instead of adding a bare PdfFillRect to the page. Flatten those groups so
        // this assertion actually finds the text boxes' fill rects rather than always seeing an empty
        // top-level PdfFillRect collection regardless of the hidden-row behaviour under test.
        var fillRects = FlattenFillRects(ops);

        var aboveBox = fillRects.Should().ContainSingle(r => r.Color == new PdfColor(255, 0, 0)).Subject;
        var belowBox = fillRects.Should().ContainSingle(r => r.Color == new PdfColor(0, 255, 0)).Subject;

        // Row 3 and row 6 are nominally 3 rows apart on the sheet; with rows 4/5 correctly excluded
        // from the print range's interior, only ONE printed row of vertical gap should separate their
        // anchored blocks. PDF's y-axis points up, so the higher-on-page (row 3) box has the larger Y.
        // Pre-fix, the two hidden rows were silently reinstated between them, tripling the gap to
        // three row heights.
        const double oneRowHeightPt = 20.0 * (72.0 / 96.0);
        (aboveBox.Y - belowBox.Y).Should().BeApproximately(oneRowHeightPt, 0.5);
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

    /// <summary>
    /// Recursively collects every <see cref="PdfFillRect"/> in <paramref name="ops"/>, descending into
    /// <see cref="PdfOpacityGroup"/> wrappers (which is how a translucent fill -- e.g. a text box's
    /// FillAlpha &lt; 255 -- reaches the page) so callers see the same fills a renderer would paint.
    /// </summary>
    private static List<PdfFillRect> FlattenFillRects(IReadOnlyList<PdfDrawOp> ops)
    {
        var result = new List<PdfFillRect>();
        CollectFillRects(ops, result);
        return result;
    }

    private static void CollectFillRects(IReadOnlyList<PdfDrawOp> ops, List<PdfFillRect> result)
    {
        foreach (var op in ops)
        {
            switch (op)
            {
                case PdfFillRect fillRect:
                    result.Add(fillRect);
                    break;
                case PdfOpacityGroup group:
                    CollectFillRects(group.Ops, result);
                    break;
            }
        }
    }
}
