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
        // The shared planner owns multi-area/grouped-sheet remapping and content analysis; the host
        // supplies only the workbook, target sheets, selected ranges, and Merge Across mode.
        source.Should().Contain("CellMergePlanner.CreateContentWarningPlan(");
        source.Should().Contain("CurrentGroupedEditSheetIds(),");
        source.Should().Contain("GetCurrentSelectionRanges(range),");
        source.Should().NotContain("CellMergePlanner.AnalyzeContent(sheetRanges, perRow)");
        source.Should().Contain("ShowMergeCellsContentWarningDialog(contentPlan)");
        source.Should().Contain("private MergeCellContentDecision ShowMergeCellsContentWarningDialog(MergeCellContentWarningPlan contentPlan)");
        source.Should().Contain("var choice = MergeCellContentChoice.Cancel;");
        source.Should().Contain("return CellMergePlanner.ResolveContentChoice(choice);");
        source.Should().Contain("if (!decision.ShouldProceed)");
        source.Should().Contain("contentResolution = decision.Resolution;");
        source.Should().NotContain("private enum MergeCellsWarningChoice");
        source.Should().Contain("Content = \"Keep only first cell\"");
        source.Should().Contain("Content = \"Concatenate all cells\"");
        source.Should().Contain("Content = \"Cancel\"");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"MergeCellsContentWarningDialog\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(keepFirstButton, \"MergeCellsKeepFirstButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(concatenateButton, \"MergeCellsConcatenateButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, \"MergeCellsCancelButton\");");
        source.Should().Contain("MergeAcrossMenuItem_Click");
        source.Should().Contain("MergeCellsMenuItem_Click");
        source.Should().Contain("UnmergeCellsMenuItem_Click");
        source.Should().Contain("CreateMergeCellsCommand(");
        source.Should().Contain("CellMergePlanner.CreateFormatCellsMergeCommands(");
        source.Should().Contain("CellMergePlanner.CreateMergeAndCenterCommands(");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: 0))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: 45))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: -45))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: 255))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: 90))");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(TextRotation: -90))");
    }

}
