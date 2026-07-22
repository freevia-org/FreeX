using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R66-commands-clear-delete-6-1: Home&gt;Clear&gt;Clear Formats did not remove conditional-formatting
/// rules on the selection, even though Excel's Clear Formats does (CF is itself a form of formatting) --
/// the app's own Clear All already composed <c>ApplyStyleCommand(ClearFormatsDiff)</c> with
/// <c>ClearConditionalFormatsCommand</c>, but Clear Formats only ever ran the style half via a bare
/// <c>ApplyStyleDiff</c> call. The fix makes <c>MainWindow.ClearFormats()</c> a composite of both
/// commands, mirroring <c>ClearAllMenuItem_Click</c>.
/// </summary>
public sealed class R66_ClearFormatsRemovesConditionalFormatWiringTests
{
    [Fact]
    public void ClearFormats_RunsBothApplyStyleClearFormatsDiffAndClearConditionalFormatsCommand()
    {
        var source = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.HomeEditing.cs");
        var method = SourceMethodExtractor.ExtractMethodSource(source, "private void ClearFormats()");

        method.Should().Contain(
            "CellStyleDiffPlanner.ClearFormatsDiff()",
            "Clear Formats must still clear the selection's style, exactly as before the fix");
        method.Should().Contain(
            "new ClearConditionalFormatsCommand(",
            "Clear Formats must also remove conditional-formatting rules on the selection, matching Excel");
    }

    // Sibling no-regression: Clear All's own composite (which already included both commands) must be
    // untouched by this fix.
    [Fact]
    public void ClearAllMenuItemClick_StillRunsBothApplyStyleClearFormatsDiffAndClearConditionalFormatsCommand()
    {
        var source = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.HomeEditing.cs");
        var method = SourceMethodExtractor.ExtractMethodSource(
            source, "private void ClearAllMenuItem_Click(object sender, RoutedEventArgs e)");

        method.Should().Contain("CellStyleDiffPlanner.ClearFormatsDiff()");
        method.Should().Contain("new ClearConditionalFormatsCommand(");
    }
}
