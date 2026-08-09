using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

// R127-fillcmds-multiarea-1: WorkbookSession.FillSelectedRange (the Avalonia shell's Fill
// Down/Up/Left/Right entry point) used to read only SelectedRange -- the single "active" area of a
// Ctrl+click multi-area selection -- and pass just that one GridRange into FillCellsCommand. With
// areas A1:A3 and C1:C3 selected (C1:C3 active/last-clicked), Fill Down used to fill C2:C3 from C1
// and silently leave A2:A3 untouched, unlike real Excel, which fills every disjoint area of a
// multi-area selection independently from its own edge in one Fill Down action. The fix routes
// FillSelectedRange through the same SelectionStyleCommandPlanner.ResolveRanges/CreateRangeCommand
// choke point this session's own R124/R126 multi-area Group/Ungroup and Row Height/Column Width
// fixes already use, mirroring the WPF host's ExecuteFillCells fix.
public sealed class R127_WorkbookSessionMultiAreaFillTests
{
    [Fact]
    public void FillSelectedRange_MultiAreaSelection_FillsEveryDisjointAreaFromItsOwnEdge()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SetNumber(sheet, 1, 1, 10); // A1
        SetNumber(sheet, 1, 3, 20); // C1
        var areaA = new GridRange(Address(sheet, 1, 1), Address(sheet, 3, 1)); // A1:A3
        var areaC = new GridRange(Address(sheet, 1, 3), Address(sheet, 3, 3)); // C1:C3 -- active/last-clicked

        var session = CreateSession(workbook);
        session.SelectRanges(areaC, [areaA, areaC]);

        var result = session.FillSelectedRange(FillCellsDirection.Down);

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        // Before the fix, only column C (the active area) was filled down from C1; column A was
        // silently left untouched.
        sheet.GetValue(2, 1).Should().Be(new NumberValue(10), "A2 in the disjoint area must also be filled down");
        sheet.GetValue(3, 1).Should().Be(new NumberValue(10), "A3 in the disjoint area must also be filled down");
        sheet.GetValue(2, 3).Should().Be(new NumberValue(20), "C2 (the active area) must be filled down");
        sheet.GetValue(3, 3).Should().Be(new NumberValue(20), "C3 (the active area) must be filled down");
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void FillSelectedRange_MultiAreaSelection_FillRightFillsEveryDisjointArea()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SetNumber(sheet, 1, 1, 10); // A1
        SetNumber(sheet, 3, 1, 20); // A3
        var areaTop = new GridRange(Address(sheet, 1, 1), Address(sheet, 1, 3)); // A1:C1
        var areaBottom = new GridRange(Address(sheet, 3, 1), Address(sheet, 3, 3)); // A3:C3 -- active

        var session = CreateSession(workbook);
        session.SelectRanges(areaBottom, [areaTop, areaBottom]);

        var result = session.FillSelectedRange(FillCellsDirection.Right);

        result.Success.Should().BeTrue();
        sheet.GetValue(1, 2).Should().Be(new NumberValue(10), "B1 in the disjoint area must also be filled right");
        sheet.GetValue(1, 3).Should().Be(new NumberValue(10), "C1 in the disjoint area must also be filled right");
        sheet.GetValue(3, 2).Should().Be(new NumberValue(20), "B3 (the active area) must be filled right");
        sheet.GetValue(3, 3).Should().Be(new NumberValue(20), "C3 (the active area) must be filled right");
    }

    // No-regression sibling: a plain SINGLE active-range Fill Down (the overwhelmingly common case
    // -- no Ctrl+click involved) must keep filling exactly that one range, unaffected by routing
    // the command construction through the ranges-aware plumbing.
    [Fact]
    public void FillSelectedRange_SingleRangeSelection_StillFillsOnlyThatRange()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SetNumber(sheet, 1, 1, 10); // A1
        SetNumber(sheet, 1, 3, 99); // C1 -- outside the selection, must stay untouched
        var areaA = new GridRange(Address(sheet, 1, 1), Address(sheet, 3, 1)); // A1:A3

        var session = CreateSession(workbook);
        session.SelectRange(areaA);

        var result = session.FillSelectedRange(FillCellsDirection.Down);

        result.Success.Should().BeTrue();
        sheet.GetValue(2, 1).Should().Be(new NumberValue(10));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(10));
        sheet.GetValue(2, 3).Should().BeOfType<BlankValue>();
        sheet.GetValue(3, 3).Should().BeOfType<BlankValue>();
    }

    // Combination: one disjoint area is too small to fill in the requested direction (a single row
    // for Fill Down). Excel just leaves that area alone rather than erroring out the whole
    // multi-area fill -- the qualifying area must still get filled.
    [Fact]
    public void FillSelectedRange_MultiAreaSelection_SkipsAreaTooSmallToFillButFillsTheRest()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SetNumber(sheet, 1, 1, 10); // A1 (only row of the too-small area)
        SetNumber(sheet, 1, 3, 20); // C1
        var tooSmall = new GridRange(Address(sheet, 1, 1), Address(sheet, 1, 1)); // A1:A1 -- one row
        var areaC = new GridRange(Address(sheet, 1, 3), Address(sheet, 3, 3)); // C1:C3 -- active, qualifies

        var session = CreateSession(workbook);
        session.SelectRanges(areaC, [tooSmall, areaC]);

        var result = session.FillSelectedRange(FillCellsDirection.Down);

        result.Success.Should().BeTrue();
        sheet.GetValue(2, 3).Should().Be(new NumberValue(20));
        sheet.GetValue(3, 3).Should().Be(new NumberValue(20));
        sheet.GetValue(1, 1).Should().Be(new NumberValue(10), "the too-small area has no target cells and must be left exactly as seeded");
    }

    // When NO area qualifies at all (the ordinary single too-small-range case), FillSelectedRange
    // must still fail with the same message FillCellsCommand itself reports -- not silently
    // "succeed" as a no-op, which would regress today's error feedback.
    [Fact]
    public void FillSelectedRange_SingleAreaTooSmallToFill_ReportsFailureNotSilentNoOp()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SetNumber(sheet, 1, 1, 10);
        var tooSmall = new GridRange(Address(sheet, 1, 1), Address(sheet, 1, 1)); // one row: can't Fill Down

        var session = CreateSession(workbook);
        session.SelectRange(tooSmall);

        var result = session.FillSelectedRange(FillCellsDirection.Down);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("at least one target cell");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
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
