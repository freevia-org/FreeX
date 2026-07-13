using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R22-merged-cells-view-state-1: Merge &amp; Center did not toggle off (unmerge) when invoked a
/// second time on a selection that already IS (or is fully covered by) an existing merged region —
/// it instead built a <see cref="MergeCellsCommand"/> that unconditionally rejects any overlap with
/// "Range overlaps an existing merged region.", exactly mirroring Excel's own error for a genuine
/// conflicting merge instead of Excel's documented toggle gesture (select the merged cell, click
/// Merge &amp; Center again -> unmerges). Verifies <see cref="CellMergePlanner.CreateMergeAndCenterCommands"/>
/// now detects the toggle case and emits an <see cref="UnmergeCellsCommand"/>, while a genuine
/// partial-overlap conflict (selection straddles merge boundaries without being covered by any single
/// region) still surfaces the original overlap rejection.
/// </summary>
public sealed class R22_merge_center_toggle_Tests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void MergeAndCenter_SecondClickOnMergedSelection_UnmergesInsteadOfErroring()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 2),  // B2
            new CellAddress(sheet.Id, 2, 4)); // D2

        // First click: merges (and centers) B2:D2, exactly like the ribbon "Merge & Center" button.
        var firstClickCommands = CellMergePlanner.CreateMergeAndCenterCommands(
            sheet, sheet.Id, range, MergeCellContentResolution.KeepFirstCell);
        foreach (var command in firstClickCommands)
            command.Apply(ctx).Success.Should().BeTrue();

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(range);

        // Second click on the same (now-merged) selection — GridView selection auto-expands a click
        // inside a merged cell to the merge's exact bounds, so the selected range is again B2:D2.
        var secondClickCommands = CellMergePlanner.CreateMergeAndCenterCommands(
            sheet, sheet.Id, range, MergeCellContentResolution.KeepFirstCell);

        secondClickCommands.Should().ContainSingle().Which.Should().BeOfType<UnmergeCellsCommand>();

        var outcome = secondClickCommands[0].Apply(ctx);
        outcome.Success.Should().BeTrue();
        sheet.MergedRegions.Should().BeEmpty();
    }

    [Fact]
    public void MergeAndCenter_SelectionFullyInsideBiggerMerge_UnmergesWholeCoveringRegion()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var bigMerge = new GridRange(
            new CellAddress(sheet.Id, 2, 2),  // B2
            new CellAddress(sheet.Id, 4, 4)); // D4
        sheet.AddMergedRegion(bigMerge);

        // Selection is only the top-left sub-cell of the bigger merge (e.g. a non-auto-expanded
        // selection path) — Excel treats the merged cell as atomic, so Merge & Center here must
        // still toggle off the WHOLE covering region, not error.
        var innerSelection = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 2, 2));

        var commands = CellMergePlanner.CreateMergeAndCenterCommands(
            sheet, sheet.Id, innerSelection, MergeCellContentResolution.KeepFirstCell);

        commands.Should().ContainSingle().Which.Should().BeOfType<UnmergeCellsCommand>();
        commands[0].Apply(ctx).Success.Should().BeTrue();
        sheet.MergedRegions.Should().BeEmpty();
    }

    [Fact]
    public void MergeAndCenter_PartialOverlapWithExistingMerge_StillRejectsAsConflict()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var existingMerge = new GridRange(
            new CellAddress(sheet.Id, 2, 2),  // B2
            new CellAddress(sheet.Id, 2, 3)); // C2
        sheet.AddMergedRegion(existingMerge);

        // Genuine partial overlap: the selection C2:D2 starts INSIDE the existing B2:C2 merge (shares
        // C2) and extends out past its right boundary to D2, so neither range contains the other. This
        // is a real conflict Excel rejects — distinct from a selection that fully CONTAINS the existing
        // merge (e.g. B2:D2), which Excel instead absorbs (see MergeCellsCommand's containment path).
        var straddlingRange = new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 2, 4));

        var commands = CellMergePlanner.CreateMergeAndCenterCommands(
            sheet, sheet.Id, straddlingRange, MergeCellContentResolution.KeepFirstCell);

        var mergeCommand = commands.OfType<MergeCellsCommand>().Should().ContainSingle().Which;
        var outcome = mergeCommand.Apply(ctx);
        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Range overlaps an existing merged region.");

        // Nothing changed: the original merge is untouched and no new one was added.
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(existingMerge);
    }
}
