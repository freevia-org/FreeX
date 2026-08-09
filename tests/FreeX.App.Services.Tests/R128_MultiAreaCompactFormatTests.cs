using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

// R128-services-multiarea-compactformat-1: WorkbookSession.ApplySelectedRangeCompactFormat is the
// shared apply routine behind the Avalonia shell's Border-preset gallery (Home > Borders),
// the Format Cells dialog's OK button, and the Lock/Unlock Cell toggle. It used to build its
// style/border/font-size/merge commands purely from the single active SelectedRange, silently
// ignoring every other disjoint area of a Ctrl+click multi-area selection (SelectedRanges) --
// unlike Excel, and unlike the already-fixed sibling ApplySelectedRangeStyle
// (R127-cellscmds-multiarea-style-1), which routes through GetSelectionSizingRanges()/
// SelectionStyleCommandPlanner so every disjoint area gets touched. The WPF host never had this bug:
// its own ApplyRangeBorderPreset (MainWindow.HomeFormatting.cs) and ApplyFormatCellsDialogResult
// (MainWindow.CellsCommands.cs) both already enumerate GetCurrentSelectionRanges() across every area.
public sealed class R128_MultiAreaCompactFormatTests
{
    [Fact]
    public void ApplySelectedRangeCompactFormat_MultiAreaSelection_AppliesStyleAndBorderToEveryDisjointArea()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(c3, new NumberValue(2));
        var session = CreateSession(workbook);

        // Ctrl+click A1 then C3 (disjoint): SelectedRange is the active/last-clicked area (C3),
        // SelectedRanges holds both -- exactly what Avalonia's AddAdditionalCellSelection produces.
        var rangeA1 = new GridRange(a1, a1);
        var rangeC3 = new GridRange(c3, c3);
        session.SelectRanges(rangeC3, [rangeA1, rangeC3]);

        var result = session.ApplySelectedRangeCompactFormat(
            new StyleDiff(Bold: true),
            CellBorderPreset.Outside);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var expectedBorder = new CellBorder(BorderStyle.Thin, CellColor.Black);

        // Before the fix, only C3 (the active area) got Bold + the border preset; A1 was silently
        // left untouched.
        var a1Style = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        a1Style.Bold.Should().BeTrue("A1's disjoint area must also be bolded, matching Excel's Ctrl+click multi-area formatting");
        a1Style.BorderTop.Should().Be(expectedBorder);
        a1Style.BorderLeft.Should().Be(expectedBorder);
        a1Style.BorderRight.Should().Be(expectedBorder);
        a1Style.BorderBottom.Should().Be(expectedBorder);

        var c3Style = workbook.GetStyle(sheet.GetCell(c3)!.StyleId);
        c3Style.Bold.Should().BeTrue("C3 (the active area) must be bolded");
        c3Style.BorderTop.Should().Be(expectedBorder);
        c3Style.BorderLeft.Should().Be(expectedBorder);
        c3Style.BorderRight.Should().Be(expectedBorder);
        c3Style.BorderBottom.Should().Be(expectedBorder);
    }

    [Fact]
    public void ApplySelectedRangeCompactFormat_MultiAreaSelection_MergesEveryDisjointArea()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var e1 = new CellAddress(sheet.Id, 1, 5);
        var session = CreateSession(workbook);

        var rangeAB = new GridRange(a1, b1);
        var rangeDE = new GridRange(d1, e1);
        session.SelectRanges(rangeDE, [rangeAB, rangeDE]);

        var result = session.ApplySelectedRangeCompactFormat(
            new StyleDiff(),
            borderPreset: null,
            mergeCells: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.MergedRegions.Should().Contain(rangeAB, "the non-active disjoint area A1:B1 must also merge");
        sheet.MergedRegions.Should().Contain(rangeDE, "the active area D1:E1 must merge");
    }

    // Excel parity: applying a compact-format command to a multi-area selection leaves the multi-area
    // selection intact afterwards, rather than collapsing down to just the last-applied area --
    // mirroring the already-fixed ApplySelectedRangeStyle (R127-cellscmds-multiarea-style-1).
    [Fact]
    public void ApplySelectedRangeCompactFormat_MultiAreaSelection_PreservesMultiAreaSelectionAfterApplying()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        var session = CreateSession(workbook);

        var rangeA1 = new GridRange(a1, a1);
        var rangeC3 = new GridRange(c3, c3);
        session.SelectRanges(rangeC3, [rangeA1, rangeC3]);

        session.ApplySelectedRangeCompactFormat(new StyleDiff(Bold: true), borderPreset: null)
            .Success.Should().BeTrue();

        session.SelectedRanges.Should().HaveCount(2);
        session.SelectedRanges.Should().Contain(rangeA1);
        session.SelectedRanges.Should().Contain(rangeC3);
    }

    // No-regression sibling: the plain single-range (no Ctrl+click) path -- by far the most common
    // call shape, exercised extensively by WorkbookSessionCompactFormatTests -- must keep applying
    // style, border preset, font size (with its row-height grow) and merge as a single composite,
    // undoable edit exactly as before.
    [Fact]
    public void ApplySelectedRangeCompactFormat_SingleAreaSelection_StillAppliesStyleBorderFontSizeAndMergeAsOneEdit()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(workbook);
        session.SelectRange(new GridRange(a1, b2));

        var result = session.ApplySelectedRangeCompactFormat(
            new StyleDiff(Bold: true, FontSize: 24),
            CellBorderPreset.Outside,
            mergeCells: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        session.CanUndo.Should().BeTrue();
        var mergedRange = new GridRange(a1, b2);
        sheet.MergedRegions.Should().Contain(mergedRange);
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).Bold.Should().BeTrue();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).FontSize.Should().Be(24);
        sheet.RowHeights[1].Should().Be(37);

        var undo = session.UndoLastEdit();
        undo.Success.Should().BeTrue();
        sheet.MergedRegions.Should().NotContain(mergedRange);
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
