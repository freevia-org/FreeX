using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using static FreeX.Core.IO.Tests.TextFileAdapterTestHelper;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R28-save-fileops-recovery-deep-1: CSV Save-As must export the workbook's active sheet, not
/// always the first sheet in tab order — those differ once the user has switched tabs.
/// </summary>
public sealed class R28_CsvActiveSheetTests
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

        var adapter = new CsvFileAdapter();
        var savedText = SaveToUtf8Text(adapter, workbook);

        savedText.Should().Be("active-sheet2\r\n");
    }

    [Fact]
    public void Save_FallsBackToFirstSheet_WhenActiveSheetIndexIsUnset()
    {
        // Sibling already-working case: a freshly built workbook with no ActiveSheetIndex
        // recorded must keep exporting the (only) first sheet, as before this fix.
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("only-sheet"));

        var adapter = new CsvFileAdapter();
        var savedText = SaveToUtf8Text(adapter, workbook);

        savedText.Should().Be("only-sheet\r\n");
    }

    [Fact]
    public void Save_FallsBackToFirstSheet_WhenActiveSheetIndexIsOutOfRange()
    {
        // Defensive sibling case: a stale/out-of-range ActiveSheetIndex (e.g. left over after
        // sheet removal elsewhere) must not throw or export nothing — fall back to sheet 0.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("fallback"));
        workbook.ActiveSheetIndex = 5;

        var adapter = new CsvFileAdapter();
        var savedText = SaveToUtf8Text(adapter, workbook);

        savedText.Should().Be("fallback\r\n");
    }
}
