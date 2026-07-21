using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Excel's right-click "Remove Hyperlink" removes only the link and keeps the cell's visible
/// hyperlink formatting (blue/underline); only the ribbon's Home&gt;Clear&gt;Remove Hyperlinks
/// command strips that formatting (resetting Underline/DoubleUnderline/FontColor via
/// RemoveHyperlinksCommand). These source-contract checks pin the WPF host's worksheet
/// context-menu routing so a future edit can't silently reattach the format-stripping command
/// to the right-click path again.
/// </summary>
public sealed class WorksheetContextMenuRemoveHyperlinkRoutingSourceTests
{
    [Fact]
    public void RemoveHyperlinksContextMenuAction_RoutesToFormatPreservingClearHyperlinksHandler()
    {
        var source = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Host", "MainWindow.WorksheetContextMenu.cs");

        var caseIndex = source.IndexOf("case WorksheetContextMenuAction.RemoveHyperlinks:", System.StringComparison.Ordinal);
        caseIndex.Should().BeGreaterThan(-1, "the RemoveHyperlinks context-menu action must still be handled");

        var breakIndex = source.IndexOf("break;", caseIndex, System.StringComparison.Ordinal);
        breakIndex.Should().BeGreaterThan(caseIndex);

        var caseBody = source[caseIndex..breakIndex];

        caseBody.Should().Contain(
            "ClearHyperlinksMenuItem_Click",
            "right-click Remove Hyperlink must route to the format-preserving Clear Hyperlinks handler, matching Excel");
        caseBody.Should().NotContain(
            "RemoveHyperlinks()",
            "the right-click Remove Hyperlink action must not call the format-stripping RemoveHyperlinks() helper");
    }

    [Fact]
    public void RemoveHyperlinksCommand_StripFormattingBehaviorRemainsReservedForRibbonClearHyperlinks()
    {
        // The ribbon's Home>Clear>Remove Hyperlinks path (and its pinned unit test
        // HyperlinkCommandTests.RemoveHyperlinksCommand_RemovesHyperlinkAndResetsHyperlinkStyle)
        // still uses the format-stripping RemoveHyperlinksCommand directly; only the worksheet
        // right-click path was re-pointed to the format-preserving ClearHyperlinksCommand.
        var commandsSource = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.Core.Commands", "HyperlinkCommands.cs");

        commandsSource.Should().Contain("public sealed class RemoveHyperlinksCommand");
        commandsSource.Should().Contain("style.Underline = false;");
        commandsSource.Should().Contain("style.DoubleUnderline = false;");
        commandsSource.Should().Contain("style.FontColor = CellColor.Black;");
    }

    [Fact]
    public void AvaloniaWorksheetContextMenu_AlreadyRoutesRemoveHyperlinksToFormatPreservingHandler()
    {
        // The Avalonia shell keeps its worksheet context-menu switch inline in MainWindow.cs
        // (there is no separate MainWindow.WorksheetContextMenu.cs file on that shell). It
        // already dispatches both ClearHyperlinks and RemoveHyperlinks context actions to the
        // same ClearSelectedRangeHyperlinks() handler, which in turn uses ClearHyperlinksCommand
        // (see WorkbookSession.ClearSelectedRangeHyperlinks), so no change was needed there.
        var avaloniaMainWindowPath = Path.Combine(
            WorkspaceFileLocator.FindWorkspaceRoot(), "src", "FreeX.App.Avalonia", "MainWindow.WorksheetContextMenu.cs");
        File.Exists(avaloniaMainWindowPath)
            .Should().BeFalse("the Avalonia shell's worksheet context-menu handling lives in MainWindow.cs, not a dedicated file");

        var avaloniaSource = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", "MainWindow.cs");

        var caseIndex = avaloniaSource.IndexOf("case WorksheetContextMenuAction.RemoveHyperlinks:", System.StringComparison.Ordinal);
        caseIndex.Should().BeGreaterThan(-1);
        var breakIndex = avaloniaSource.IndexOf("break;", caseIndex, System.StringComparison.Ordinal);
        avaloniaSource[caseIndex..breakIndex].Should().Contain("ClearSelectedRangeHyperlinks");

        var sessionSource = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Services", "WorkbookSession.cs");
        var methodIndex = sessionSource.IndexOf(
            "public WorkbookCellEditResult ClearSelectedRangeHyperlinks()", System.StringComparison.Ordinal);
        methodIndex.Should().BeGreaterThan(-1);
        var methodLength = System.Math.Min(400, sessionSource.Length - methodIndex);
        sessionSource.Substring(methodIndex, methodLength).Should().Contain("new ClearHyperlinksCommand(");
    }
}
