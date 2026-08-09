using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeAlignmentCommandSourceTests
{

    [Fact]
    public void AlignmentCommandHandlers_RouteThroughStyleDiffsAndRepeatableMergeCommand()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        source.Should().Contain("ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Left))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Center))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Right))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Top))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Center))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Bottom))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(WrapText: IsRibbonCommandChecked(\"Wrap Text\")))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(IndentLevel: Math.Min(15, style.IndentLevel + 1)))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(IndentLevel: Math.Max(0, style.IndentLevel - 1)))");
        // R127-homeformatting-multiarea-merge-1: Merge & Center / Merge Cells / Merge Across /
        // Unmerge Cells now route through the multi-area-aware TryExecuteRepeatableCurrentRangesCommand/
        // TryExecuteRepeatableCurrentSelectionRangesCommand helpers (every disjoint area of a
        // Ctrl+click multi-area selection, not just the active SheetGrid.SelectedRange) instead of
        // the single-active-range-only TryExecuteRepeatableCurrentRangeCommand.
        source.Should().Contain("TryExecuteRepeatableCurrentRangesCommand(");
        source.Should().Contain("TryExecuteRepeatableCurrentSelectionRangesCommand(");
        source.Should().Contain("\"Merge & Center\"");
        source.Should().Contain("CreateMergeAndCenterCommand");
        source.Should().Contain("TryResolveMergeContentResolution(range, out var contentResolution)");
        // R127-homeformatting-multiarea-merge-2: the content-loss analysis must cover every disjoint
        // Ctrl+click area the merge will actually touch (GetCurrentSelectionRanges), not just the
        // single active `range` -- see TryResolveMergeContentResolution's own doc comment.
        source.Should().Contain("var ranges = GetCurrentSelectionRanges(range);");
        // R128-homeformatting-groupedsheet-merge-1: the content-loss analysis must ALSO cover every
        // grouped sheet the merge fans out to (CurrentGroupedEditSheetIds), unioning each sheet's
        // remapped-range entries via the CellMergePlanner.AnalyzeContent(IEnumerable<(Sheet,Ranges)>,bool)
        // overload -- not just the active sheet -- see TryResolveMergeContentResolution's own doc comment.
        source.Should().Contain("var targetSheetIds = CurrentGroupedEditSheetIds();");
        source.Should().Contain("CellMergePlanner.AnalyzeContent(sheetRanges, perRow)");
        source.Should().Contain("ShowMergeCellsContentWarningDialog(contentPlan)");
        source.Should().Contain("Content = \"Keep only first cell\"");
        source.Should().Contain("Content = \"Concatenate all cells\"");
        source.Should().Contain("Content = \"Cancel\"");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"MergeCellsContentWarningDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(keepFirstButton, \"MergeCellsKeepFirstButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(concatenateButton, \"MergeCellsConcatenateButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, \"MergeCellsCancelButton\");");
        source.Should().Contain("choice == MergeCellsWarningChoice.Cancel");
        source.Should().Contain("MergeCellContentResolution.ConcatenateAllCells");
        source.Should().Contain("MergeAcrossMenuItem_Click");
        source.Should().Contain("MergeCellsMenuItem_Click");
        source.Should().Contain("UnmergeCellsMenuItem_Click");
        source.Should().Contain("CellMergePlanner.CreateMergeCellsCommand(");
        source.Should().Contain("CellMergePlanner.CreateMergeAcrossCommand(");
        source.Should().Contain("CellMergePlanner.CreateMergeAndCenterCommands(");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: 0))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: 45))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: -45))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: 255))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: 90))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: -90))");
    }

}
