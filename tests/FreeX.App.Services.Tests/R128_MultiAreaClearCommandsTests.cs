using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

// R128-cellscmds-multiarea-clear-2: same bug class as R127 (R127_MultiAreaStyleAndClearContentsTests),
// applied to Home>Clear>Clear All/Formats/Comments and Notes/Hyperlinks and the worksheet right-click
// "Remove Hyperlink" item. WorkbookSession.ClearSelectedRangeAll/ClearSelectedRangeFormats/
// ClearSelectedRangeComments/ClearSelectedRangeHyperlinks/RemoveSelectedRangeHyperlinks all used to
// build their command against only the single active SelectedRange, silently ignoring every other
// disjoint area of a Ctrl+click multi-area selection (SelectedRanges) -- unlike Excel, and unlike the
// WPF host's ClearAllMenuItem_Click/ClearFormatsMenuItem_Click/ClearCommentsMenuItem_Click/
// ClearHyperlinksMenuItem_Click/RemoveHyperlinkMenuItem_Click, which already resolve every disjoint
// area via TryExecuteRepeatableCurrentSelectionRangesCommand/GetCurrentSelectionRanges.
public sealed class R128_MultiAreaClearCommandsTests
{
    [Fact]
    public void ClearSelectedRangeAll_MultiAreaSelection_ClearsEveryDisjointArea()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.SetCell(a1, new NumberValue(11));
        sheet.SetCell(b5, new NumberValue(22));
        sheet.Comments[a1] = "note-a1";
        sheet.Comments[b5] = "note-b5";
        sheet.Hyperlinks[a1] = "https://example.com/a1";
        sheet.Hyperlinks[b5] = "https://example.com/b5";
        var session = CreateSession(workbook);

        var rangeA1 = new GridRange(a1, a1);
        var rangeB5 = new GridRange(b5, b5);
        session.SelectRanges(rangeB5, [rangeA1, rangeB5]);
        session.SetSelectedRangeBold(true).Success.Should().BeTrue();
        // Restore the multi-area selection the Bold apply above preserved, then re-select to be
        // explicit about what Clear All is about to run against.
        session.SelectRanges(rangeB5, [rangeA1, rangeB5]);

        var result = session.ClearSelectedRangeAll();

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Before the fix, only B5 (the active area) was cleared; A1 silently kept its value, style,
        // comment, and hyperlink.
        sheet.GetValue(a1).Should().Be(BlankValue.Instance, "A1's disjoint area must also be cleared, matching Excel's Clear All");
        sheet.GetValue(b5).Should().Be(BlankValue.Instance, "B5 (the active area) must be cleared");
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).Bold.Should().NotBe(true, "A1's format must also be cleared");
        workbook.GetStyle(sheet.GetCell(b5)!.StyleId).Bold.Should().NotBe(true);
        sheet.Comments.Should().NotContainKey(a1, "A1's comment must also be cleared");
        sheet.Comments.Should().NotContainKey(b5);
        sheet.Hyperlinks.Should().NotContainKey(a1, "A1's hyperlink must also be cleared");
        sheet.Hyperlinks.Should().NotContainKey(b5);
    }

    [Fact]
    public void ClearSelectedRangeAll_SingleActiveRange_StillClearsOnlyThatRange_NoRegression()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.SetCell(a1, new NumberValue(11));
        sheet.SetCell(b5, new NumberValue(22));
        sheet.Comments[b5] = "note-b5";
        sheet.Hyperlinks[b5] = "https://example.com/b5";
        var session = CreateSession(workbook);

        session.SelectRange(new GridRange(a1, a1));
        session.SelectedRanges.Count.Should().BeLessThanOrEqualTo(1);

        var result = session.ClearSelectedRangeAll();

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetValue(a1).Should().Be(BlankValue.Instance);
        // B5 was never selected -- must keep its value, comment, and hyperlink.
        sheet.GetCell(b5)!.Value.Should().Be(new NumberValue(22));
        sheet.Comments.Should().ContainKey(b5);
        sheet.Hyperlinks.Should().ContainKey(b5);
    }

    [Fact]
    public void ClearSelectedRangeFormats_MultiAreaSelection_ClearsEveryDisjointArea()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.SetCell(a1, new NumberValue(11));
        sheet.SetCell(b5, new NumberValue(22));
        var session = CreateSession(workbook);

        var rangeA1 = new GridRange(a1, a1);
        var rangeB5 = new GridRange(b5, b5);
        session.SelectRanges(rangeB5, [rangeA1, rangeB5]);
        session.SetSelectedRangeBold(true).Success.Should().BeTrue();
        session.SelectRanges(rangeB5, [rangeA1, rangeB5]);

        var result = session.ClearSelectedRangeFormats();

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Before the fix, only B5 (the active area) had its format cleared; A1 silently stayed bold.
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).Bold.Should().NotBe(true, "A1's format must also be cleared");
        workbook.GetStyle(sheet.GetCell(b5)!.StyleId).Bold.Should().NotBe(true);
        // Clear Formats must not touch contents.
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(11));
        sheet.GetCell(b5)!.Value.Should().Be(new NumberValue(22));
    }

    [Fact]
    public void ClearSelectedRangeFormats_SingleActiveRange_StillClearsOnlyThatRange_NoRegression()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(b5, new NumberValue(2));
        var session = CreateSession(workbook);

        session.SelectRanges(new GridRange(b5, b5), [new GridRange(a1, a1), new GridRange(b5, b5)]);
        session.SetSelectedRangeBold(true).Success.Should().BeTrue();

        session.SelectRange(new GridRange(a1, a1));
        session.SelectedRanges.Count.Should().BeLessThanOrEqualTo(1);

        var result = session.ClearSelectedRangeFormats();

        result.Success.Should().BeTrue(result.ErrorMessage);
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).Bold.Should().NotBe(true);
        // B5 was never selected for this call -- must keep its bold format.
        workbook.GetStyle(sheet.GetCell(b5)!.StyleId).Bold.Should().BeTrue();
    }

    [Fact]
    public void ClearSelectedRangeComments_MultiAreaSelection_ClearsEveryDisjointArea()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.Comments[a1] = "note-a1";
        sheet.Comments[b5] = "note-b5";
        var session = CreateSession(workbook);

        var rangeA1 = new GridRange(a1, a1);
        var rangeB5 = new GridRange(b5, b5);
        session.SelectRanges(rangeB5, [rangeA1, rangeB5]);

        var result = session.ClearSelectedRangeComments();

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Before the fix, only B5 (the active area) lost its comment; A1's comment silently survived.
        sheet.Comments.Should().NotContainKey(a1, "A1's disjoint area must also have its comment cleared");
        sheet.Comments.Should().NotContainKey(b5);
    }

    [Fact]
    public void ClearSelectedRangeComments_SingleActiveRange_StillClearsOnlyThatRange_NoRegression()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.Comments[a1] = "note-a1";
        sheet.Comments[b5] = "note-b5";
        var session = CreateSession(workbook);

        session.SelectRange(new GridRange(a1, a1));
        session.SelectedRanges.Count.Should().BeLessThanOrEqualTo(1);

        var result = session.ClearSelectedRangeComments();

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.Comments.Should().NotContainKey(a1);
        sheet.Comments.Should().ContainKey(b5, "B5 was never selected -- must keep its comment");
    }

    [Fact]
    public void ClearSelectedRangeHyperlinks_MultiAreaSelection_ClearsEveryDisjointArea()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.Hyperlinks[a1] = "https://example.com/a1";
        sheet.Hyperlinks[b5] = "https://example.com/b5";
        var session = CreateSession(workbook);

        var rangeA1 = new GridRange(a1, a1);
        var rangeB5 = new GridRange(b5, b5);
        session.SelectRanges(rangeB5, [rangeA1, rangeB5]);

        // ClearSelectedRangeHyperlinks is the worksheet right-click "Remove Hyperlink" item's
        // format-preserving handler (see MainWindow.cs's context-menu dispatch).
        var result = session.ClearSelectedRangeHyperlinks();

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Before the fix, only B5 (the active area) lost its hyperlink; A1's hyperlink silently
        // survived.
        sheet.Hyperlinks.Should().NotContainKey(a1, "A1's disjoint area must also have its hyperlink cleared");
        sheet.Hyperlinks.Should().NotContainKey(b5);
    }

    [Fact]
    public void ClearSelectedRangeHyperlinks_SingleActiveRange_StillClearsOnlyThatRange_NoRegression()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.Hyperlinks[a1] = "https://example.com/a1";
        sheet.Hyperlinks[b5] = "https://example.com/b5";
        var session = CreateSession(workbook);

        session.SelectRange(new GridRange(a1, a1));
        session.SelectedRanges.Count.Should().BeLessThanOrEqualTo(1);

        var result = session.ClearSelectedRangeHyperlinks();

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.Hyperlinks.Should().NotContainKey(a1);
        sheet.Hyperlinks.Should().ContainKey(b5, "B5 was never selected -- must keep its hyperlink");
    }

    [Fact]
    public void RemoveSelectedRangeHyperlinks_MultiAreaSelection_ClearsEveryDisjointArea()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.Hyperlinks[a1] = "https://example.com/a1";
        sheet.Hyperlinks[b5] = "https://example.com/b5";
        var session = CreateSession(workbook);

        var rangeA1 = new GridRange(a1, a1);
        var rangeB5 = new GridRange(b5, b5);
        session.SelectRanges(rangeB5, [rangeA1, rangeB5]);

        // RemoveSelectedRangeHyperlinks is the ribbon Home>Clear>Clear Hyperlinks entry point (the
        // format-STRIPPING handler -- see MainWindow.cs's "Clear Hyperlinks" menu/flyout wiring).
        var result = session.RemoveSelectedRangeHyperlinks();

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Before the fix, only B5 (the active area) lost its hyperlink; A1's hyperlink silently
        // survived.
        sheet.Hyperlinks.Should().NotContainKey(a1, "A1's disjoint area must also have its hyperlink removed");
        sheet.Hyperlinks.Should().NotContainKey(b5);
    }

    [Fact]
    public void RemoveSelectedRangeHyperlinks_SingleActiveRange_StillClearsOnlyThatRange_NoRegression()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.Hyperlinks[a1] = "https://example.com/a1";
        sheet.Hyperlinks[b5] = "https://example.com/b5";
        var session = CreateSession(workbook);

        session.SelectRange(new GridRange(a1, a1));
        session.SelectedRanges.Count.Should().BeLessThanOrEqualTo(1);

        var result = session.RemoveSelectedRangeHyperlinks();

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.Hyperlinks.Should().NotContainKey(a1);
        sheet.Hyperlinks.Should().ContainKey(b5, "B5 was never selected -- must keep its hyperlink");
    }

    // Excel parity: clearing a multi-area selection leaves the multi-area selection intact
    // afterwards, rather than collapsing down to just the last-cleared area -- mirroring R127's
    // ClearSelectedRangeContents fix.
    [Fact]
    public void ClearSelectedRangeAll_MultiAreaSelection_PreservesMultiAreaSelectionAfterClearing()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(b5, new NumberValue(2));
        var session = CreateSession(workbook);

        var rangeA1 = new GridRange(a1, a1);
        var rangeB5 = new GridRange(b5, b5);
        session.SelectRanges(rangeB5, [rangeA1, rangeB5]);

        session.ClearSelectedRangeAll().Success.Should().BeTrue();

        session.SelectedRanges.Should().HaveCount(2);
        session.SelectedRanges.Should().Contain(rangeA1);
        session.SelectedRanges.Should().Contain(rangeB5);
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
}
