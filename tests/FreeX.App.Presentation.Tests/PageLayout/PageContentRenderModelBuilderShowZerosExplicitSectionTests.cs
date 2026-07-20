using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R52-meta-1: PageContentRenderModelBuilder.FormatCellText (print/PDF/paginated export) must
/// mirror ViewportService.GetDisplayText's "explicit zero number-format section" exception --
/// when ShowZeros is off but the cell's own number format defines a third (zero) section (e.g.
/// "0;-0;\"zero\""), that section's own rendering governs and the cell must NOT be blanked, so
/// print/PDF output matches what the interactive grid shows.
/// </summary>
public sealed class PageContentRenderModelBuilderShowZerosExplicitSectionTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Build_ShowZerosFalseWithExplicitZeroFormatSection_PrintsZeroSectionText()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.ShowZeros = false;
        var style = new CellStyle { NumberFormat = "0;-0;\"zero\"" };
        var styleId = workbook.RegisterStyle(style);
        var cell = Cell.FromValue(new NumberValue(0));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.Cells.Single(c => c.Row == 1 && c.Column == 1).Text.Should().Be("zero");
    }

    /// <summary>Sibling no-regression: without an explicit zero section, ShowZeros=false still blanks.</summary>
    [Fact]
    public void Build_ShowZerosFalseWithoutExplicitZeroFormatSection_StillBlanksZeroValuedCell()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.ShowZeros = false;
        // Give the cell a fill so the (now-empty) cell block is still emitted by the
        // "skip fully-blank cells" optimization, letting the test observe the blanked text.
        var style = new CellStyle { FillColor = new CellColor(10, 20, 30), NumberFormat = "0.00" };
        var styleId = workbook.RegisterStyle(style);
        var cell = Cell.FromValue(new NumberValue(0));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.Cells.Single(c => c.Row == 1 && c.Column == 1).Text.Should().BeEmpty();
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
}
