using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

// R127-services-sort-multiarea-1: WorkbookSession.SortSelectedRange (both overloads -- the
// Avalonia shell's Sort Ascending/Descending and Custom Sort entry points) used to read only
// SelectedRange -- the single "active" area of a Ctrl+click multi-area selection -- and pass just
// that one GridRange into SortCommand. With areas A1:A3 and C1:C3 selected (C1:C3
// active/last-clicked), Sort Ascending used to quietly reorder only column C's rows while column A
// was silently left untouched, unlike real Excel, which refuses Sort outright on a multiple
// selection ("This operation is not allowed on multiple selections. Select a single range and
// click the command again."). The fix adds TryCreateMultiAreaSortRejection, checked before either
// overload builds a SortCommand, mirroring this class's own CreateMultiRangeClipboardError refusal
// for multi-area Copy/Cut/Paste Special and the WPF host's identical
// TryRejectMultiAreaSort (see R127_MultiAreaSortRejectionTests in FreeX.App.Host.Tests).
public sealed class R127_WorkbookSessionMultiAreaSortRejectionTests
{
    [Fact]
    public void SortSelectedRange_Ascending_MultiAreaSelection_RejectsWithoutSortingEitherArea()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SetNumber(sheet, 1, 1, 30); SetNumber(sheet, 2, 1, 10); SetNumber(sheet, 3, 1, 20); // A1:A3
        SetNumber(sheet, 1, 3, 300); SetNumber(sheet, 2, 3, 100); SetNumber(sheet, 3, 3, 200); // C1:C3 -- active
        var areaA = new GridRange(Address(sheet, 1, 1), Address(sheet, 3, 1));
        var areaC = new GridRange(Address(sheet, 1, 3), Address(sheet, 3, 3));

        var session = CreateSession(workbook);
        session.SelectRanges(areaC, [areaA, areaC]);

        var result = session.SortSelectedRange(ascending: true);

        result.Success.Should().BeFalse("Excel refuses Sort outright on a multiple selection");
        result.ErrorMessage.Should().Contain("Sort").And.Contain("multiple selected ranges");
        // Before the fix, column C (the active area) got quietly sorted ascending to
        // 100/200/300 while column A stayed 30/10/20 -- neither area may change now.
        sheet.GetValue(1, 1).Should().Be(new NumberValue(30));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(10));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(20));
        sheet.GetValue(1, 3).Should().Be(new NumberValue(300), "the active area must also stay untouched, not just the non-active one");
        sheet.GetValue(2, 3).Should().Be(new NumberValue(100));
        sheet.GetValue(3, 3).Should().Be(new NumberValue(200));
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SortSelectedRange_CustomSortKeys_MultiAreaSelection_RejectsWithoutSortingEitherArea()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SetNumber(sheet, 1, 1, 30); SetNumber(sheet, 2, 1, 10); SetNumber(sheet, 3, 1, 20);
        SetNumber(sheet, 1, 3, 300); SetNumber(sheet, 2, 3, 100); SetNumber(sheet, 3, 3, 200);
        var areaA = new GridRange(Address(sheet, 1, 1), Address(sheet, 3, 1));
        var areaC = new GridRange(Address(sheet, 1, 3), Address(sheet, 3, 3));

        var session = CreateSession(workbook);
        session.SelectRanges(areaC, [areaA, areaC]);

        var sortKeys = new List<SortKey> { new(ColumnOffset: 0, Ascending: true) };
        var result = session.SortSelectedRange(sortKeys, new SortOptions(CaseSensitive: false, LeftToRight: false), hasHeaders: false);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Sort").And.Contain("multiple selected ranges");
        sheet.GetValue(1, 1).Should().Be(new NumberValue(30));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(10));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(20));
        sheet.GetValue(1, 3).Should().Be(new NumberValue(300));
        sheet.GetValue(2, 3).Should().Be(new NumberValue(100));
        sheet.GetValue(3, 3).Should().Be(new NumberValue(200));
    }

    // No-regression sibling: a plain SINGLE active-range Sort (the overwhelmingly common case -- no
    // Ctrl+click involved) must keep sorting exactly that one range, unaffected by the new
    // multi-area check.
    [Fact]
    public void SortSelectedRange_Ascending_SingleRangeSelection_StillSortsNormally()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SetNumber(sheet, 1, 1, 30); SetNumber(sheet, 2, 1, 10); SetNumber(sheet, 3, 1, 20);
        var areaA = new GridRange(Address(sheet, 1, 1), Address(sheet, 3, 1));

        var session = CreateSession(workbook);
        session.SelectRange(areaA);

        var result = session.SortSelectedRange(ascending: true);

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        sheet.GetValue(1, 1).Should().Be(new NumberValue(10));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(20));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(30));
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }

    private static void SetNumber(Sheet sheet, uint row, uint column, double value) =>
        sheet.SetCell(Address(sheet, row, column), new NumberValue(value));

    private static CellAddress Address(Sheet sheet, uint row, uint column) =>
        new(sheet.Id, row, column);
}
