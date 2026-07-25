using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R87-commands-merge-cells-5-1: <c>CreateFormatCellsMergeCommands</c>'s <c>ConcatenateAllCells</c>
/// branch used to route through <c>CreateMergeAndCenterCommands</c>'s 4-arg overload, which had no
/// <c>allowUnmergeToggle</c> parameter at all and unconditionally applied the toggle-to-unmerge
/// gesture. A Merge Across per-row batch (which always passes <c>allowUnmergeToggle: false</c> so an
/// already-correctly-merged row is left merged rather than toggled off) would therefore silently
/// UNMERGE an already-merged row whenever the user picked "Concatenate All Cells" in the
/// content-loss dialog -- while the default <c>KeepFirstCell</c> resolution (via
/// <c>CreateMergeCommands</c>, already covered by <see cref="CellMergePlannerTests.CreateFormatCellsMergeCommands_AllowUnmergeToggleFalse_LeavesAlreadyMergedRowMerged"/>)
/// correctly honored the flag. This is the same gap for the Concatenate resolution.
/// </summary>
public sealed class R87_MergeAcrossConcatenatePreservesAlreadyMergedRowTests
{
    [Fact]
    public void CreateFormatCellsMergeCommands_ConcatenateAllCells_AllowUnmergeToggleFalse_LeavesAlreadyMergedRowMerged()
    {
        // Row 1 (A1:C1) is already merged -- the Merge Across per-row batch re-invokes this method
        // for row 1 too, and must leave it merged instead of unmerging it.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var alreadyMergedRow = Range(sheet.Id, 1, 1, 1, 3);
        sheet.AddMergedRegion(alreadyMergedRow);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Jan Feb Mar"));

        var commands = CellMergePlanner.CreateFormatCellsMergeCommands(
            sheet,
            sheet.Id,
            alreadyMergedRow,
            mergeCells: true,
            contentResolution: MergeCellContentResolution.ConcatenateAllCells,
            allowUnmergeToggle: false);

        commands.Should().NotContain(command => command is UnmergeCellsCommand);
        commands.Should().Contain(command => command is MergeCellsCommand);
    }

    /// <summary>
    /// No-regression sibling: the direct Merge &amp; Center / Merge Cells gesture with Concatenate
    /// (the default, <c>allowUnmergeToggle: true</c>) must keep its Excel-parity toggle-to-unmerge
    /// behavior for an already-fully-covered selection.
    /// </summary>
    [Fact]
    public void CreateFormatCellsMergeCommands_ConcatenateAllCells_DefaultAllowUnmergeToggle_StillTogglesAlreadyMergedRangeOff()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var alreadyMergedRow = Range(sheet.Id, 1, 1, 1, 3);
        sheet.AddMergedRegion(alreadyMergedRow);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Jan Feb Mar"));

        var commands = CellMergePlanner.CreateFormatCellsMergeCommands(
            sheet,
            sheet.Id,
            alreadyMergedRow,
            mergeCells: true,
            contentResolution: MergeCellContentResolution.ConcatenateAllCells);

        commands.Should().ContainSingle().Which.Should().BeOfType<UnmergeCellsCommand>();
    }

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));
}
