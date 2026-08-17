using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using static FreeX.Core.IO.Tests.TextFileAdapterTestHelper;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R139-csv-legacy-1: SYLK (.slk) and DIF (.dif) Save-As must export the workbook's active sheet,
/// not always the first sheet in tab order — matching DelimitedTextWorkbookWriter/PrnFileAdapter's
/// identical active-sheet rule for CSV/TXT/PRN (see R28_CsvActiveSheetTests, R80_PrnActiveSheetExportTests).
/// LossyFormatFeatureLossPlanner already tells the user "only the current worksheet's data will be
/// saved" for multi-sheet .slk/.dif workbooks — these adapters must honor that promise.
/// </summary>
public sealed class R139_SlkDifActiveSheetExportTests
{
    [Fact]
    public void Slk_Save_ExportsActiveSheet_NotFirstSheetInTabOrder()
    {
        var workbook = new Workbook("Book1");
        var first = workbook.AddSheet("Sheet1");
        first.SetCell(new CellAddress(first.Id, 1, 1), new TextValue("stale-sheet1"));

        var second = workbook.AddSheet("Sheet2");
        second.SetCell(new CellAddress(second.Id, 1, 1), new TextValue("active-sheet2"));

        workbook.ActiveSheetIndex = 1;

        var savedText = SaveToUtf8Text(new SlkFileAdapter(), workbook);

        savedText.Should().Contain("active-sheet2");
        savedText.Should().NotContain("stale-sheet1");
    }

    // No-regression sibling: a freshly built workbook with no ActiveSheetIndex recorded (or an
    // out-of-range one) must keep exporting the first sheet.
    [Fact]
    public void Slk_Save_FallsBackToFirstSheet_WhenActiveSheetIndexIsUnset()
    {
        var workbook = new Workbook("Book1");
        var first = workbook.AddSheet("Sheet1");
        first.SetCell(new CellAddress(first.Id, 1, 1), new TextValue("only-sheet"));
        workbook.AddSheet("Sheet2");

        var savedText = SaveToUtf8Text(new SlkFileAdapter(), workbook);

        savedText.Should().Contain("only-sheet");
    }

    [Fact]
    public void Dif_Save_ExportsActiveSheet_NotFirstSheetInTabOrder()
    {
        var workbook = new Workbook("Book1");
        var first = workbook.AddSheet("Sheet1");
        first.SetCell(new CellAddress(first.Id, 1, 1), new TextValue("stale-sheet1"));

        var second = workbook.AddSheet("Sheet2");
        second.SetCell(new CellAddress(second.Id, 1, 1), new TextValue("active-sheet2"));

        workbook.ActiveSheetIndex = 1;

        var savedText = SaveToUtf8Text(new DifFileAdapter(), workbook);

        savedText.Should().Contain("active-sheet2");
        savedText.Should().NotContain("stale-sheet1");
    }

    // No-regression sibling: a freshly built workbook with no ActiveSheetIndex recorded (or an
    // out-of-range one) must keep exporting the first sheet.
    [Fact]
    public void Dif_Save_FallsBackToFirstSheet_WhenActiveSheetIndexIsUnset()
    {
        var workbook = new Workbook("Book1");
        var first = workbook.AddSheet("Sheet1");
        first.SetCell(new CellAddress(first.Id, 1, 1), new TextValue("only-sheet"));
        workbook.AddSheet("Sheet2");

        var savedText = SaveToUtf8Text(new DifFileAdapter(), workbook);

        savedText.Should().Contain("only-sheet");
    }
}
