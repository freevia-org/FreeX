using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class CellMergePlannerTests
{
    [Fact]
    public void IsSelectionMerged_DetectsOverlappingMergedRegion()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var merged = Range(sheet.Id, 2, 2, 3, 3);
        sheet.AddMergedRegion(merged);

        CellMergePlanner.IsSelectionMerged(sheet, Range(sheet.Id, 1, 1, 1, 1)).Should().BeFalse();
        CellMergePlanner.IsSelectionMerged(sheet, Range(sheet.Id, 3, 3, 4, 4)).Should().BeTrue();
    }

    [Fact]
    public void CreateMergeAndCenterCommands_MergesMultiCellRangeAndCentersSelection()
    {
        var sheetId = SheetId.New();
        var range = Range(sheetId, 1, 1, 2, 2);

        var commands = CellMergePlanner.CreateMergeAndCenterCommands(sheetId, range);

        commands.Should().HaveCount(2);
        commands[0].Should().BeOfType<MergeCellsCommand>();
        commands[1].Should().BeOfType<ApplyStyleCommand>();
    }

    [Fact]
    public void CreateMergeAndCenterCommands_ConcatenateWritesTopLeftBeforeMerging()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var range = Range(sheet.Id, 1, 1, 2, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("SUM(A1:B1)"));

        var commands = CellMergePlanner.CreateMergeAndCenterCommands(
            sheet,
            sheet.Id,
            range,
            MergeCellContentResolution.ConcatenateAllCells);

        commands.Should().HaveCount(3);
        commands[0].Should().BeOfType<EditCellsCommand>();
        commands[1].Should().BeOfType<MergeCellsCommand>();
        commands[2].Should().BeOfType<ApplyStyleCommand>();
    }

    [Fact]
    public void CreateMergeAndCenterCommands_SingleCellCentersWithoutMergeCommand()
    {
        var sheetId = SheetId.New();
        var range = Range(sheetId, 1, 1, 1, 1);

        var commands = CellMergePlanner.CreateMergeAndCenterCommands(sheetId, range);

        commands.Should().ContainSingle().Which.Should().BeOfType<ApplyStyleCommand>();
    }

    [Fact]
    public void CreateMergeCommands_MergesMultiCellRangeWithoutCenteringCommand()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var range = Range(sheet.Id, 1, 1, 2, 2);

        var commands = CellMergePlanner.CreateMergeCommands(sheet, sheet.Id, range, mergeCells: true);

        commands.Should().ContainSingle().Which.Should().BeOfType<MergeCellsCommand>();
    }

    [Fact]
    public void CreateMergeCommands_SingleCellMergeIsNoOp()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();

        var commands = CellMergePlanner.CreateMergeCommands(
            sheet,
            sheet.Id,
            Range(sheet.Id, 1, 1, 1, 1),
            mergeCells: true);

        commands.Should().BeEmpty();
    }

    [Fact]
    public void CreateMergeCommands_UnmergeTargetsOverlappingMergedRegions()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var merged = Range(sheet.Id, 1, 1, 2, 2);
        sheet.AddMergedRegion(merged);

        var commands = CellMergePlanner.CreateMergeCommands(
            sheet,
            sheet.Id,
            Range(sheet.Id, 2, 2, 2, 2),
            mergeCells: false);

        commands.Should().ContainSingle().Which.Should().BeOfType<UnmergeCellsCommand>();
    }

    [Fact]
    public void CreateMergeCommands_AllowUnmergeToggleFalse_LeavesAlreadyMergedRowMerged()
    {
        // R55-commands-merge-center-5-1: a Merge-Across per-row batch must never toggle an
        // already-correctly-merged row back off just because CreateMergeCommands is re-invoked for
        // that row too.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var alreadyMergedRow = Range(sheet.Id, 2, 1, 2, 3);
        sheet.AddMergedRegion(alreadyMergedRow);

        var commands = CellMergePlanner.CreateMergeCommands(
            sheet,
            sheet.Id,
            alreadyMergedRow,
            mergeCells: true,
            allowUnmergeToggle: false);

        commands.Should().NotContain(command => command is UnmergeCellsCommand);
        commands.Should().ContainSingle().Which.Should().BeOfType<MergeCellsCommand>();
    }

    [Fact]
    public void CreateMergeCommands_DefaultAllowUnmergeToggle_StillTogglesAlreadyMergedRangeOff()
    {
        // Sibling no-regression test: the direct Merge Cells / Merge & Center gesture (the default,
        // allowUnmergeToggle: true) must keep its Excel-parity toggle-to-unmerge behavior.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var alreadyMergedRow = Range(sheet.Id, 2, 1, 2, 3);
        sheet.AddMergedRegion(alreadyMergedRow);

        var commands = CellMergePlanner.CreateMergeCommands(
            sheet,
            sheet.Id,
            alreadyMergedRow,
            mergeCells: true);

        commands.Should().ContainSingle().Which.Should().BeOfType<UnmergeCellsCommand>();
    }

    [Fact]
    public void CreateUnmergeCommands_TargetsOverlappingMergedRegions()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var first = Range(sheet.Id, 1, 1, 2, 2);
        var second = Range(sheet.Id, 5, 5, 6, 6);
        sheet.AddMergedRegion(first);
        sheet.AddMergedRegion(second);

        var commands = CellMergePlanner.CreateUnmergeCommands(sheet, sheet.Id, Range(sheet.Id, 2, 2, 5, 5));

        commands.Should().HaveCount(2);
        commands.Should().OnlyContain(command => command is UnmergeCellsCommand);
    }

    [Fact]
    public void AnalyzeContent_WarnsWhenNonTopLeftContentWouldBeDiscarded()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var range = Range(sheet.Id, 1, 1, 2, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("first"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("second"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(true));

        var plan = CellMergePlanner.AnalyzeContent(sheet, range);

        plan.WouldLoseContent.Should().BeTrue();
        plan.Entries.Select(entry => entry.DisplayText).Should().Equal("first", "second", "TRUE");
        plan.ConcatenatedText.Should().Be("first second TRUE");
    }

    [Fact]
    public void AnalyzeContent_SkipsWarningWhenOnlyTopLeftHasContent()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var range = Range(sheet.Id, 1, 1, 2, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("first"));

        var plan = CellMergePlanner.AnalyzeContent(sheet, range);

        plan.WouldLoseContent.Should().BeFalse();
        plan.ConcatenatedText.Should().Be("first");
    }

    [Fact]
    public void AnalyzeContent_WarnsWhenOnlyNonTopLeftHasContent()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var range = Range(sheet.Id, 1, 1, 2, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("A1+A2"));

        var plan = CellMergePlanner.AnalyzeContent(sheet, range);

        plan.WouldLoseContent.Should().BeTrue();
        plan.ConcatenatedText.Should().Be("=A1+A2");
    }

    [Fact]
    public void AnalyzeContent_PerRow_SkipsWarningWhenEachRowsLeftmostHasContent()
    {
        // R55-commands-merge-center-5-2: for a Merge-Across batch (perRow: true), each row's own
        // leftmost cell is THAT row's top-left, so A1/A2/A3 all being the sole content in their own
        // row must not trigger the discard warning.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var range = Range(sheet.Id, 1, 1, 3, 3);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Mar"));

        var plan = CellMergePlanner.AnalyzeContent(sheet, range, perRow: true);

        plan.WouldLoseContent.Should().BeFalse();
    }

    [Fact]
    public void AnalyzeContent_PerRowFalse_StillWarnsForSameData()
    {
        // Sibling no-regression test: the direct Merge Cells / Merge & Center gesture (perRow: false,
        // including the parameterless overload) folds the WHOLE range into one merged cell, so only
        // range.Start survives -- A2/A3's content is genuinely lost and the warning must still fire.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var range = Range(sheet.Id, 1, 1, 3, 3);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Mar"));

        CellMergePlanner.AnalyzeContent(sheet, range, perRow: false).WouldLoseContent.Should().BeTrue();
        CellMergePlanner.AnalyzeContent(sheet, range).WouldLoseContent.Should().BeTrue();
    }

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
}
