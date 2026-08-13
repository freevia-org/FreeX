using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class SpellCheckWorkflowPlannerTests
{
    [Fact]
    public void ReviewSpellCheckCommand_RefreshesEditorStateAroundDialogReplacements()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        source.Split("RefreshSpellCheckEditorState(issue.Address);").Length.Should().BeGreaterThanOrEqualTo(3);
        source.Should().Contain("dialog.Result.Action is SpellCheckSessionAction.Change or SpellCheckSessionAction.ChangeAll");
        source.Should().Contain("private void RefreshSpellCheckEditorState(CellAddress address)");
        source.Should().Contain("HideInlineEditor(commit: false);");
        source.Should().Contain("ClearFormulaRangeEntryState();");
        source.Should().Contain("SetFormulaBarSelectionText(FormatFormulaBarText(sheet?.GetCell(address), address));");
    }
}
