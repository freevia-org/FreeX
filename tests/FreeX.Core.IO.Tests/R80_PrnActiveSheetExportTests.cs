using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using static FreeX.Core.IO.Tests.TextFileAdapterTestHelper;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R80-services-export-formats-5-2: PRN (.prn) Save-As must export the workbook's active sheet,
/// not always the first sheet in tab order — matching DelimitedTextWorkbookWriter's identical
/// active-sheet rule for CSV/TXT (see R28_CsvActiveSheetTests).
/// </summary>
public sealed class R80_PrnActiveSheetExportTests
{
    [Fact]
    public void Save_ExportsActiveSheet_NotFirstSheetInTabOrder()
    {
        var workbook = new Workbook("Book1");
        var first = workbook.AddSheet("Sheet1");
        first.SetCell(new CellAddress(first.Id, 1, 1), new TextValue("stale-sheet1"));

        var second = workbook.AddSheet("Sheet2");
        second.SetCell(new CellAddress(second.Id, 1, 1), new TextValue("active-sheet2"));

        workbook.ActiveSheetIndex = 1;

        var savedText = SaveToUtf8Text(new PrnFileAdapter(), workbook);

        savedText.Should().Contain("active-sheet2");
        savedText.Should().NotContain("stale-sheet1");
    }

    // No-regression sibling: a freshly built workbook with no ActiveSheetIndex recorded (or an
    // out-of-range one) must keep exporting the first sheet, matching the pre-existing behavior
    // locked in by Save_OnlyFirstSheetIsExported in PrnFileAdapterTests.
    [Fact]
    public void Save_FallsBackToFirstSheet_WhenActiveSheetIndexIsUnset()
    {
        var workbook = new Workbook("Book1");
        var first = workbook.AddSheet("Sheet1");
        first.SetCell(new CellAddress(first.Id, 1, 1), new TextValue("only-sheet"));
        workbook.AddSheet("Sheet2");

        var savedText = SaveToUtf8Text(new PrnFileAdapter(), workbook);

        savedText.Should().Contain("only-sheet");
    }
}
