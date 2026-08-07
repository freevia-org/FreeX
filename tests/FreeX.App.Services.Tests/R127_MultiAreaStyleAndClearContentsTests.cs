using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

// R127-cellscmds-multiarea-style-1: same bug class as R126 (R126_MultiAreaRowColumnSizingTests),
// applied to the much larger surface of Home-tab style commands (Bold/Italic/Underline/Alignment/
// Number Format/Font+Fill Color/Indent/Cell Styles/Wrap Text -- all routed through
// WorkbookSession.ApplySelectedRangeStyle) and Delete/Clear Contents (ClearSelectedRangeContents).
// Both used to build their command against only the single active SelectedRange, silently ignoring
// every other disjoint area of a Ctrl+click multi-area selection (SelectedRanges) -- unlike Excel,
// and unlike the WPF host's TryExecuteRepeatableApplyStyle/TryExecuteRepeatableCurrentSelectionRangesCommand,
// which already resolve every disjoint area via SelectionStyleCommandPlanner.ResolveRanges/
// CreateApplyStyleCommand/CreateRangeCommand.
public sealed class R127_MultiAreaStyleAndClearContentsTests
{
    [Fact]
    public void SetSelectedRangeBold_MultiAreaSelection_AppliesToEveryDisjointArea()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(b5, new NumberValue(2));
        var session = CreateSession(workbook);

        // Ctrl+click A1 then B5 (disjoint): SelectedRange is the active/last-clicked area (B5),
        // SelectedRanges holds both -- exactly what Avalonia's AddAdditionalCellSelection produces.
        var rangeA1 = new GridRange(a1, a1);
        var rangeB5 = new GridRange(b5, b5);
        session.SelectRanges(rangeB5, [rangeA1, rangeB5]);

        var result = session.SetSelectedRangeBold(true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Before the fix, only B5 (the active area) got Bold; A1 was silently left unformatted.
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).Bold.Should().BeTrue(
            "A1's disjoint area must also be bolded, matching Excel's Ctrl+click multi-area formatting");
        workbook.GetStyle(sheet.GetCell(b5)!.StyleId).Bold.Should().BeTrue(
            "B5 (the active area) must be bolded");
    }

    [Fact]
    public void SetSelectedRangeFillColor_MultiAreaSelection_AppliesToEveryDisjointArea()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(c3, new NumberValue(2));
        var session = CreateSession(workbook);

        var rangeA1 = new GridRange(a1, a1);
        var rangeC3 = new GridRange(c3, c3);
        session.SelectRanges(rangeC3, [rangeA1, rangeC3]);

        var fillColor = new CellColor(255, 0, 0);
        var result = session.SetSelectedRangeFillColor(fillColor);

        result.Success.Should().BeTrue(result.ErrorMessage);
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).FillColor.Should().Be(fillColor);
        workbook.GetStyle(sheet.GetCell(c3)!.StyleId).FillColor.Should().Be(fillColor);
    }

    [Fact]
    public void ClearSelectedRangeContents_MultiAreaSelection_ClearsEveryDisjointArea()
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

        var result = session.ClearSelectedRangeContents();

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Before the fix, only B5 (the active area) was cleared; A1 silently kept its value.
        sheet.GetValue(a1).Should().Be(BlankValue.Instance, "A1's disjoint area must also be cleared, matching Excel's Delete/Clear Contents");
        sheet.GetValue(b5).Should().Be(BlankValue.Instance, "B5 (the active area) must be cleared");
    }

    // No-regression sibling: a plain single active-range Bold (no Ctrl+click multi-area selection)
    // must keep applying to exactly that one range, unaffected by routing the command construction
    // through the ranges-aware plumbing.
    [Fact]
    public void SetSelectedRangeBold_SingleActiveRange_StillAppliesOnlyToThatRange_NoRegression()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(b5, new NumberValue(2));
        var session = CreateSession(workbook);

        session.SelectRange(new GridRange(a1, a1));
        session.SelectedRanges.Count.Should().BeLessThanOrEqualTo(1);

        var result = session.SetSelectedRangeBold(true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).Bold.Should().BeTrue();
        // B5 was never selected -- must remain untouched.
        workbook.GetStyle(sheet.GetCell(b5)!.StyleId).Bold.Should().NotBe(true);
    }

    // No-regression sibling for Clear Contents: a plain single active-range clear must keep
    // clearing exactly that one range.
    [Fact]
    public void ClearSelectedRangeContents_SingleActiveRange_StillClearsOnlyThatRange_NoRegression()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.SetCell(a1, new NumberValue(11));
        sheet.SetCell(b5, new NumberValue(22));
        var session = CreateSession(workbook);

        session.SelectRange(new GridRange(a1, a1));
        session.SelectedRanges.Count.Should().BeLessThanOrEqualTo(1);

        var result = session.ClearSelectedRangeContents();

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetValue(a1).Should().Be(BlankValue.Instance);
        // B5 was never selected -- must keep its value.
        sheet.GetCell(b5)!.Value.Should().Be(new NumberValue(22));
    }

    // Excel parity: applying a style command to a multi-area selection leaves the multi-area
    // selection intact afterwards (the same shape a follow-up format command should target), rather
    // than collapsing down to just the last-applied area -- mirroring the R126 row/column-sizing fix
    // (WorkbookSession.ExecuteSizingCommand).
    [Fact]
    public void SetSelectedRangeBold_MultiAreaSelection_PreservesMultiAreaSelectionAfterApplying()
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

        session.SetSelectedRangeBold(true).Success.Should().BeTrue();

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
