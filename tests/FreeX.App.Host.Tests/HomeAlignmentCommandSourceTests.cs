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
        source.Should().Contain("ApplyStyleDiffWithWrapGrowth(new StyleDiff(WrapText: IsRibbonCommandChecked(\"Wrap Text\")))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(IndentLevel: Math.Min(15, style.IndentLevel + 1)))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(IndentLevel: Math.Max(0, style.IndentLevel - 1)))");
        // Merge & Center delegates to the multi-area-aware workbook session; the other merge
        // variants use the shared selection-range execution path.
        source.Should().Contain("_session.MergeAndCenterSelectedRange(contentResolution)");
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
        // remapped-range entries via the shared grouped-content planner -- not just the active sheet.
        source.Should().Contain("CellMergePlanner.AnalyzeGroupedContent(");
        source.Should().Contain("CurrentGroupedEditSheetIds(),");
        source.Should().Contain("ShowMergeCellsContentWarningDialog(contentPlan)");
        source.Should().Contain("MergeCellsContentWarningPlanner.Create(");
        source.Should().Contain("presentation.Action(MergeCellsContentWarningAction.KeepFirstCell)");
        source.Should().Contain("presentation.Action(MergeCellsContentWarningAction.ConcatenateAllCells)");
        source.Should().Contain("presentation.Action(MergeCellsContentWarningAction.Cancel)");
        source.Should().Contain("Content = keepFirstAction.Label");
        source.Should().Contain("Content = concatenateAction.Label");
        source.Should().Contain("Content = cancelAction.Label");
        source.Should().Contain("presentation.DialogAutomationId");
        source.Should().NotContain("Merging cells can discard cell contents.");
        source.Should().NotContain("Keep only first cell");
        source.Should().NotContain("new MergeCellsContentWarningDialog(");
        source.Should().Contain("CellMergePlanner.ResolveContentChoice(");
        source.Should().Contain("return decision.ShouldProceed;");
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
