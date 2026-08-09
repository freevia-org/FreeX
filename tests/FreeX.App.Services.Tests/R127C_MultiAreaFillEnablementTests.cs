using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

// R127C-fillcmds-multiarea-gate-2 (final closure pass on R127-fillcmds-multiarea-1 /
// R127B-fillcmds-multiarea-gate-1): WorkbookSession.CanFillSelectedRange used to check only the
// single "active" SelectedRange via CanFill(SelectedRange, direction), never
// GetCurrentSelectedRanges/SelectedRanges -- the same single-active-area bug pattern the earlier
// passes fixed in the execution gate (FillSelectedRange), just left standing in the enablement
// predicate. Because the Avalonia ribbon's Fill Cells split-button and its Down/Right/Up/Left/Series
// flyout items (MainWindow.cs UpdateSaveButton) all gate IsEnabled on CanFillSelectedRange, a
// Ctrl+click multi-area selection whose active area is too small to fill but whose disjoint sibling
// area qualifies rendered the ENTIRE Fill Cells ribbon control disabled -- unusable via mouse/ribbon
// -- even though the underlying FillSelectedRange execution path (already multi-area-safe) would
// have filled the sibling area correctly.
public sealed class R127C_MultiAreaFillEnablementTests
{
    // The exact repro from the finding: active area is a single cell (too small for any direction),
    // a disjoint sibling area qualifies for Fill Down. Before the fix, CanFillSelectedRange looked
    // only at the single-cell active area and reported false for every direction, even Down/Up,
    // even though the sibling area A1:A3 could be filled.
    [Fact]
    public void CanFillSelectedRange_ActiveAreaTooSmall_SiblingAreaQualifies_ReturnsTrue()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var areaA = new GridRange(Address(sheet, 1, 1), Address(sheet, 3, 1)); // A1:A3 -- qualifies for Down
        var tooSmallActive = new GridRange(Address(sheet, 1, 5), Address(sheet, 1, 5)); // E1:E1 -- active, single cell

        var session = CreateSession(workbook);
        session.SelectRanges(tooSmallActive, [areaA, tooSmallActive]);

        session.CanFillSelectedRange(FillCellsDirection.Down).Should().BeTrue(
            "the disjoint sibling area A1:A3 qualifies for Fill Down even though the active area does not");
        session.CanFillSelectedRange(FillCellsDirection.Up).Should().BeTrue(
            "the disjoint sibling area A1:A3 also qualifies for Fill Up");

        // Confirm the enablement predicate actually agrees with what execution would do: the fill
        // must succeed and must fill the sibling area, not silently no-op.
        var result = session.FillSelectedRange(FillCellsDirection.Down);
        result.Success.Should().BeTrue();
    }

    // Mirror for the horizontal directions with a wide sibling area.
    [Fact]
    public void CanFillSelectedRange_ActiveAreaTooSmall_SiblingAreaQualifiesForRight_ReturnsTrue()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var areaTop = new GridRange(Address(sheet, 1, 1), Address(sheet, 1, 3)); // A1:C1 -- qualifies for Right/Left
        var tooSmallActive = new GridRange(Address(sheet, 5, 1), Address(sheet, 5, 1)); // A5:A5 -- active, single cell

        var session = CreateSession(workbook);
        session.SelectRanges(tooSmallActive, [areaTop, tooSmallActive]);

        session.CanFillSelectedRange(FillCellsDirection.Right).Should().BeTrue();
        session.CanFillSelectedRange(FillCellsDirection.Left).Should().BeTrue();
    }

    // No-regression sibling: when NO area in a multi-area selection qualifies (both too small),
    // CanFillSelectedRange must still correctly report false -- the widened check must not become
    // an unconditional true.
    [Fact]
    public void CanFillSelectedRange_MultiAreaSelection_NoAreaQualifies_ReturnsFalse()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var tooSmall1 = new GridRange(Address(sheet, 1, 1), Address(sheet, 1, 1)); // A1:A1
        var tooSmall2 = new GridRange(Address(sheet, 1, 5), Address(sheet, 1, 5)); // E1:E1

        var session = CreateSession(workbook);
        session.SelectRanges(tooSmall2, [tooSmall1, tooSmall2]);

        session.CanFillSelectedRange(FillCellsDirection.Down).Should().BeFalse();
        session.CanFillSelectedRange(FillCellsDirection.Right).Should().BeFalse();
        session.CanFillSelectedRange(FillCellsDirection.Up).Should().BeFalse();
        session.CanFillSelectedRange(FillCellsDirection.Left).Should().BeFalse();
    }

    // No-regression sibling: the plain single active-range case (the overwhelmingly common case --
    // no Ctrl+click involved) must keep behaving exactly as before.
    [Fact]
    public void CanFillSelectedRange_SingleRangeSelection_UnaffectedByMultiAreaWidening()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var areaA = new GridRange(Address(sheet, 1, 1), Address(sheet, 3, 1)); // A1:A3

        var session = CreateSession(workbook);
        session.SelectRange(areaA);

        session.CanFillSelectedRange(FillCellsDirection.Down).Should().BeTrue();
        session.CanFillSelectedRange(FillCellsDirection.Up).Should().BeTrue();
        session.CanFillSelectedRange(FillCellsDirection.Right).Should().BeFalse();
        session.CanFillSelectedRange(FillCellsDirection.Left).Should().BeFalse();
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

    private static CellAddress Address(Sheet sheet, uint row, uint column) =>
        new(sheet.Id, row, column);
}
