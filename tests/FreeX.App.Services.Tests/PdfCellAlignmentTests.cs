using FluentAssertions;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R53-fix-one-path-miss-twin-sweep-4: the Avalonia/Skia PDF export path
/// (<see cref="WorkbookPdfContentBuilder.BuildWithPageSetup"/>) must honor each cell's
/// <see cref="CellStyle.HorizontalAlignment"/> the same way the on-screen GridView viewport does,
/// instead of hardcoding every cell's text draw-op at the column's flush-left position regardless
/// of alignment.
/// </summary>
public sealed class PdfCellAlignmentTests
{
    [Fact]
    public void BuildWithPageSetup_RightAlignedCell_RendersFartherRightThanLeftAlignedCell()
    {
        // Same numeric value, same wide column, only the alignment differs.
        var leftX = BuildSingleCellPdfTextX(HorizontalAlignment.Left, new NumberValue(42));
        var rightX = BuildSingleCellPdfTextX(HorizontalAlignment.Right, new NumberValue(42));

        // Pre-fix, both draw ops land at the identical flush-left x (colX + 2*textScale) because
        // HorizontalAlignment is never consulted. Post-fix, the right-aligned cell's text must sit
        // much farther right, near the (much wider) column's right edge.
        rightX.Should().BeGreaterThan(leftX + 20,
            "a right-aligned cell must render its text near the cell's right edge in a wide column, " +
            "not flush-left like a left-aligned cell");
    }

    [Fact]
    public void BuildWithPageSetup_GeneralAlignedText_StillMatchesExplicitLeftAlignment()
    {
        // Sibling no-regression check: General alignment on TEXT content resolves to Left (Excel's
        // "General" rule), so it must still land at the exact same x as an explicit Left alignment --
        // the alignment-aware fix must not change the default flush-left behavior for ordinary text.
        var generalX = BuildSingleCellPdfTextX(HorizontalAlignment.General, new TextValue("Hi"));
        var leftX = BuildSingleCellPdfTextX(HorizontalAlignment.Left, new TextValue("Hi"));

        generalX.Should().BeApproximately(leftX, 0.001,
            "General alignment on text content must resolve to Left, matching explicit Left alignment");
    }

    private static double BuildSingleCellPdfTextX(HorizontalAlignment alignment, ScalarValue value)
    {
        var workbook = new Workbook("Align");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ColumnWidths[1] = 40; // wide column -- much larger than the short cell text.

        var style = workbook.RegisterStyle(new CellStyle { HorizontalAlignment = alignment });
        var cell = Cell.FromValue(value);
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

        var expectedFragment = value switch
        {
            NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TextValue t => t.Value,
            _ => throw new NotSupportedException()
        };

        var op = doc.Pages[0].Ops.OfType<PdfText>().First(t => t.Text.Contains(expectedFragment));
        return op.X;
    }
}
