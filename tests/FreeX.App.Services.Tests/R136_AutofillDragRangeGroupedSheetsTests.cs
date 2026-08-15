using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R136-services-autofill-grouped-sheets-1 (src/FreeX.App.Services/WorkbookSession.cs,
/// AutofillDragRange).
///
/// Excel's Group Editing mode mirrors every edit made on the active sheet -- including a
/// fill-handle drag -- to every other grouped sheet. This session's own FillSelectedRange
/// (keyboard/menu Fill Down/Up/Left/Right) already fans out via CurrentGroupedEditSheetIds/
/// CreateGroupedSheetCommand, and so does FlashFillSelectedRange, but AutofillDragRange (the
/// fill-handle DRAG gesture) used to run a single AutofillCommand against ActiveSheet.Id only,
/// silently ignoring every other sheet in the group.
///
/// After the fix, AutofillDragRange fans out to CurrentGroupedEditSheetIds exactly like the
/// other fill paths, remapping the source/fill ranges onto each grouped sheet.
/// </summary>
public sealed class R136_AutofillDragRangeGroupedSheetsTests
{
    [Fact]
    public void AutofillDragRange_WithGroupedSheets_FansOutToEveryGroupedSheet()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;

        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryA2 = new CellAddress(summary.Id, 2, 1);
        var detailsA1 = new CellAddress(details.Id, 1, 1);
        var detailsA2 = new CellAddress(details.Id, 2, 1);
        var hiddenA1 = new CellAddress(hidden.Id, 1, 1);
        var hiddenA2 = new CellAddress(hidden.Id, 2, 1);

        summary.SetCell(summaryA1, new NumberValue(1));
        details.SetCell(detailsA1, new NumberValue(1));
        hidden.SetCell(hiddenA1, new NumberValue(1));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        session.SelectRange(new GridRange(summaryA1, summaryA1));
        session.SelectAllVisibleSheets();
        session.IsWorkbookGrouped.Should().BeTrue();

        var sourceRange = new GridRange(summaryA1, summaryA1);
        var fillRange = new GridRange(summaryA2, summaryA2);

        // ctrlHeld: true flips a single numeric source cell into series (increment) mode
        // (AutofillCommand.WantsSingleCellSeriesDefault) -- otherwise a lone number is just
        // copied verbatim. Either way the point under test is that every grouped sheet gets the
        // SAME transformation the active sheet gets, so make the transformation visible (1 -> 2).
        var result = session.AutofillDragRange(sourceRange, fillRange, ctrlHeld: true);

        result.Success.Should().BeTrue();
        summary.GetValue(summaryA2).Should().Be(new NumberValue(2),
            "the active sheet's own fill-handle drag must still autofill as before");
        details.GetValue(detailsA2).Should().Be(new NumberValue(2),
            "Excel's Group Editing mode mirrors a fill-handle drag to every other grouped sheet, " +
            "remapped onto that sheet's own cells");
        // A hidden sheet's tab cannot be Ctrl+clicked into a group in the first place (matching
        // Excel and this session's own GetSelectableSheetIds/CurrentGroupedEditSheetIds, which
        // both restrict grouped-edit fan-out to VISIBLE sheets) -- SelectAllVisibleSheets above
        // never added it to the group, so it must be left untouched, not fanned out to.
        (hidden.GetCell(hiddenA2)?.Value ?? BlankValue.Instance).Should().Be(
            BlankValue.Instance,
            "a hidden sheet is never part of a sheet group and must not be fanned out to");

        var undo = session.UndoLastEdit();
        undo.Success.Should().BeTrue();
        (summary.GetCell(summaryA2)?.Value ?? BlankValue.Instance).Should().Be(BlankValue.Instance);
        (details.GetCell(detailsA2)?.Value ?? BlankValue.Instance).Should().Be(BlankValue.Instance,
            "undo must revert the grouped fan-out as a single unit, not just the active sheet");
    }

    // Sibling no-regression: an ungrouped workbook (CurrentGroupedEditSheetIds returns just
    // [ActiveSheet.Id]) must behave exactly as before -- only the active sheet is touched, and a
    // sheet that merely happens to share the same cell layout is left completely alone.
    [Fact]
    public void AutofillDragRange_WithoutGroupedSheets_OnlyAffectsActiveSheet()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");

        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryA2 = new CellAddress(summary.Id, 2, 1);
        var detailsA2 = new CellAddress(details.Id, 2, 1);

        summary.SetCell(summaryA1, new NumberValue(1));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        session.SelectRange(new GridRange(summaryA1, summaryA1));
        session.IsWorkbookGrouped.Should().BeFalse();

        var sourceRange = new GridRange(summaryA1, summaryA1);
        var fillRange = new GridRange(summaryA2, summaryA2);

        var result = session.AutofillDragRange(sourceRange, fillRange, ctrlHeld: true);

        result.Success.Should().BeTrue();
        summary.GetValue(summaryA2).Should().Be(new NumberValue(2));
        (details.GetCell(detailsA2)?.Value ?? BlankValue.Instance).Should().Be(
            BlankValue.Instance,
            "without an active sheet group, a fill-handle drag must touch only the active sheet");
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
