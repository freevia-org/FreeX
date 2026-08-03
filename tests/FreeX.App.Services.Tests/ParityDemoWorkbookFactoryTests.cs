using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Guards the shared demo workbook both shells render in <c>--parity-capture</c> mode: the WPF host adopts it
/// via <c>AdoptWorkbookForParityCapture</c> and the Avalonia shell builds its session from it, so the
/// <c>grid.demo</c> comparison reflects only rendering differences, not divergent content.
/// </summary>
public sealed class ParityDemoWorkbookFactoryTests
{
    [Fact]
    public void Create_BuildsDeterministicSingleSheetDemo()
    {
        var workbook = ParityDemoWorkbookFactory.Create();

        workbook.Name.Should().Be(ParityDemoWorkbookFactory.WorkbookName);
        var sheet = workbook.Sheets.Single();
        sheet.Name.Should().Be(ParityDemoWorkbookFactory.SheetName);

        // Header row is text; first data row mixes text + numbers.
        sheet.GetValue(1, 1).Should().Be(new TextValue("Region"));
        sheet.GetValue(1, 5).Should().Be(new TextValue("Revenue"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("North"));
        sheet.GetValue(2, 3).Should().Be(new NumberValue(120));
        sheet.GetValue(2, 4).Should().Be(new NumberValue(9.5));
        sheet.GetValue(2, 5).Should().Be(new NumberValue(1140));

        // The empty "Total" cells stay blank (the CSV has empty Product/Price columns on the last row).
        sheet.GetValue(9, 1).Should().Be(new TextValue("Total"));
        sheet.GetValue(9, 2).Should().Be(BlankValue.Instance);
        sheet.GetValue(9, 3).Should().Be(new NumberValue(1024));
    }

    [Fact]
    public void Create_HeaderRowIsBold()
    {
        var workbook = ParityDemoWorkbookFactory.Create();
        var sheet = workbook.Sheets.Single();

        var headerCell = sheet.GetCell(1, 1);
        headerCell.Should().NotBeNull();
        headerCell!.StyleId.Should().NotBe(StyleId.Default, "the header row carries a registered bold style");
        workbook.GetStyle(headerCell.StyleId).Bold.Should().BeTrue();
    }

    [Fact]
    public void Create_IsByteStableAcrossCalls()
    {
        // Two independent builds must produce identical cell content so the WPF and Avalonia captures agree.
        var first = ParityDemoWorkbookFactory.Create().Sheets.Single();
        var second = ParityDemoWorkbookFactory.Create().Sheets.Single();

        for (uint row = 1; row <= 9; row++)
            for (uint col = 1; col <= 5; col++)
                first.GetValue(row, col).Should().Be(second.GetValue(row, col), $"cell ({row},{col}) must be stable");
    }

    [Fact]
    public void Create_UsesTheSamePageSetupDefaultsForBothParityHosts()
    {
        var sheet = ParityDemoWorkbookFactory.Create().Sheets.Single();

        sheet.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
        sheet.PaperSize.Should().Be(WorksheetPaperSize.Letter);
        sheet.PageMargins.Should().Be(WorksheetPageMargins.Normal);
        sheet.HeaderMargin.Should().Be(0.3);
        sheet.FooterMargin.Should().Be(0.3);
        sheet.ScaleToFit.Should().Be(new WorksheetScaleToFit(90, null, null));
        sheet.PageOrder.Should().Be(WorksheetPageOrder.OverThenDown);
        sheet.PrintArea.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 9, 7)));
        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, 1));
        sheet.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(1, 1));
    }
}
