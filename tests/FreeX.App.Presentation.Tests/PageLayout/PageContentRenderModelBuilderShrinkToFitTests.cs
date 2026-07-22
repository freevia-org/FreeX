using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R66-meta-3: the r65 shrink-to-fit fix (suppress the '#' width-overflow indicator when
/// style.ShrinkToFit is set, mirroring ViewportService.GetDisplayText / R65_ShrinkToFitOverflowIndicatorTests)
/// only reached ViewportService (the interactive grid) -- PageContentRenderModelBuilder.FormatCellText
/// (PDF export + Avalonia Print Preview) still passed its width-taking NumberFormatter.FormatWithColor
/// call without suppressWidthOverflowIndicator, so a narrow-column shrink-to-fit numeric printed/exported
/// as "######" instead of the real number (which GridView's font-shrink pass would otherwise fit).
/// </summary>
public sealed class PageContentRenderModelBuilderShrinkToFitTests
{
    private static readonly FakeTextMeasurer Measurer = new();
    private const string NineDigitNumber = "123456789";

    [Fact]
    public void Build_NarrowColumnWithShrinkToFit_PrintsRealNumber_NotHashes()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.ColumnWidths[1] = 2; // narrow enough that the 9-digit number cannot fit

        // Format "0" forces plain integer digits (never Excel's General-format scientific
        // fallback), so the overflow decision is purely a function of column width.
        var style = new CellStyle { ShrinkToFit = true, NumberFormat = "0" };
        var styleId = workbook.RegisterStyle(style);
        var cell = Cell.FromValue(new NumberValue(123456789));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.Cells.Single(c => c.Row == 1 && c.Column == 1).Text.Should().Be(NineDigitNumber);
    }

    /// <summary>Sibling no-regression: the same narrow column WITHOUT ShrinkToFit still shows '######'.</summary>
    [Fact]
    public void Build_NarrowColumnWithoutShrinkToFit_StillPrintsHashes()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.ColumnWidths[1] = 2;

        var style = new CellStyle { ShrinkToFit = false, NumberFormat = "0" };
        var styleId = workbook.RegisterStyle(style);
        var cell = Cell.FromValue(new NumberValue(123456789));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var layout = BuildFirstPage(workbook, sheet)!;

        var text = layout.Cells.Single(c => c.Row == 1 && c.Column == 1).Text;
        text.Should().NotBe(NineDigitNumber);
        text.Should().MatchRegex("^#+$");
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
