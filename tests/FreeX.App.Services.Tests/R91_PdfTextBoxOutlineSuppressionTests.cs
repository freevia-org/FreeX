using FluentAssertions;
using Free.Shared.Pdf;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R91-commands-insert-object-5-1: the shared/portable PDF export path
/// (<see cref="WorkbookPdfContentBuilder"/>, used by every shell's Save-as-PDF and print-preview)
/// must honor a text box's explicit line suppression (<see cref="TextBoxModel.OutlineHasNoFill"/>)
/// instead of always stroking a border rectangle. Before this fix, <c>PageTextBoxBlock.Outline</c>
/// was a non-nullable color that <c>PageTextBoxLayoutPlanner</c> always populated, so
/// <c>WorkbookPdfContentBuilder</c> emitted a <see cref="PdfStrokeRect"/> unconditionally.
/// </summary>
public sealed class R91_PdfTextBoxOutlineSuppressionTests
{
    [Fact]
    public void BuildWithPageSetup_SuppressedLine_EmitsNoStrokeRectForTextBox()
    {
        var workbook = new Workbook { Name = "SuppressedLine.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        var probeColor = new CellColor(160, 110, 20);
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 6, 4),
            Text = "No line",
            Width = 140,
            Height = 48,
            HasFill = false,
            OutlineColor = probeColor,
            OutlineHasNoFill = true
        });
        var exportPlan = CreatePageSetupPdfPlan(workbook);

        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan);
        var ops = document.Pages.Should().ContainSingle().Subject.Ops;

        ops.OfType<PdfStrokeRect>().Should().NotContain(rect => rect.Color == new PdfColor(160, 110, 20),
            "an explicitly line-suppressed text box must not be stroked with its outline color");
    }

    /// <summary>No-regression sibling: an ordinary bordered text box (OutlineHasNoFill=false, the
    /// far more common authored case) must still emit its stroke rectangle in PDF export.</summary>
    [Fact]
    public void BuildWithPageSetup_AuthoredLine_StillEmitsStrokeRectForTextBox()
    {
        var workbook = new Workbook { Name = "AuthoredLine.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        var probeColor = new CellColor(160, 110, 20);
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 6, 4),
            Text = "Bordered",
            Width = 140,
            Height = 48,
            HasFill = false,
            OutlineColor = probeColor
        });
        var exportPlan = CreatePageSetupPdfPlan(workbook);

        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan);
        var ops = document.Pages.Should().ContainSingle().Subject.Ops;

        ops.OfType<PdfStrokeRect>().Should().Contain(rect => rect.Color == new PdfColor(160, 110, 20));
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
}
