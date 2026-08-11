using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Excel's right-click "Remove Hyperlink" removes only the link and keeps the cell's visible
/// hyperlink formatting (blue/underline); only Home&gt;Clear&gt;Clear Hyperlinks (the ribbon
/// command whose id is literally "Clear Hyperlinks", see Ribbon/FreeXRibbonHandlerMap.g.cs)
/// strips that formatting (resetting Underline/DoubleUnderline/FontColor via
/// RemoveHyperlinksCommand). r63 fixed the WPF host: previously BOTH the ribbon's Clear
/// Hyperlinks command and the right-click "Remove Hyperlink" item routed through the single
/// ClearHyperlinksMenuItem_Click handler using the format-preserving ClearHyperlinksCommand, so
/// the ribbon path never actually stripped formatting. The ribbon handler map is generated (by
/// tools/ribgen.py from a pre-cutover XAML snapshot that no longer exists in the repo) and could
/// not be safely regenerated, so the fix keeps the ClearHyperlinksMenuItem_Click method name
/// (which the generated map still points the ribbon's "Clear Hyperlinks" id at) but changes its
/// body to use RemoveHyperlinksCommand, and moves the format-preserving behavior to a new
/// RemoveHyperlinkMenuItem_Click method used only by the right-click "Remove Hyperlink" item.
/// These source-contract checks pin that corrected WPF host worksheet/ribbon routing so a future
/// edit can't silently swap the two command types back.
/// </summary>
public sealed class WorksheetContextMenuRemoveHyperlinkRoutingSourceTests
{
    [Fact]
    public void RemoveHyperlinksContextMenuAction_RoutesToFormatPreservingRemoveHyperlinkHandler()
    {
        var router = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Presentation", "Shell", "WorkbookApplicationCommandRouter.cs");
        var renderer = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Host", "MainWindow.ApplicationCommandRouting.cs");

        router.Should().Contain("Route(source, \"RemoveHyperlinks\", WorkbookApplicationCommandIntent.RemoveHyperlinks");
        renderer.Should().Contain(
            "RemoveHyperlinks = Handled(() => RemoveHyperlinkMenuItem_Click(this, new RoutedEventArgs()))");
        renderer.Should().NotContain(
            "RemoveHyperlinks = Handled(() => ClearHyperlinksMenuItem_Click(this, new RoutedEventArgs()))");
    }

    [Fact]
    public void ClearHyperlinksContextMenuAction_StillRoutesToRibbonSharedFormatStrippingHandler()
    {
        // The worksheet right-click Clear submenu's own "Clear Hyperlinks" entry
        // (WorksheetContextMenuAction.ClearHyperlinks) mirrors ribbon Home>Clear semantics and
        // must keep sharing the ribbon's format-stripping handler -- unlike the sibling
        // top-level "Remove Hyperlink" item checked above, this one was not re-pointed.
        var router = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Presentation", "Shell", "WorkbookApplicationCommandRouter.cs");
        var renderer = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Host", "MainWindow.ApplicationCommandRouting.cs");

        router.Should().Contain("Route(source, \"ClearHyperlinks\", WorkbookApplicationCommandIntent.ClearHyperlinks");
        renderer.Should().Contain(
            "ClearHyperlinks = Handled(() => ClearHyperlinksMenuItem_Click(this, new RoutedEventArgs()))");
    }

    [Fact]
    public void RibbonClearHyperlinksHandler_UsesFormatStrippingCommand()
    {
        // The ribbon command id "Clear Hyperlinks" (Ribbon/FreeXRibbonHandlerMap.g.cs) resolves
        // by reflection to this exact method name, so it cannot be renamed without regenerating
        // that generated map -- but its body must perform Excel's format-stripping behavior.
        var handlerMapSource = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Host", "Ribbon", "FreeXRibbonHandlerMap.g.cs");
        handlerMapSource.Should().Contain(
            "[\"Clear Hyperlinks\"] = \"ClearHyperlinksMenuItem_Click\",",
            "the ribbon's Clear Hyperlinks command id must still resolve to ClearHyperlinksMenuItem_Click by reflection");

        var source = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Host", "MainWindow.HomeEditing.cs");

        var methodIndex = source.IndexOf(
            "private void ClearHyperlinksMenuItem_Click(object sender, RoutedEventArgs e)", System.StringComparison.Ordinal);
        methodIndex.Should().BeGreaterThan(-1);
        var closeIndex = source.IndexOf("\n    }", methodIndex, System.StringComparison.Ordinal);
        closeIndex.Should().BeGreaterThan(methodIndex);

        source[methodIndex..closeIndex].Should().Contain(
            "new RemoveHyperlinksCommand(",
            "the ribbon's Clear Hyperlinks command must strip hyperlink formatting, matching Excel");
    }

    [Fact]
    public void RemoveHyperlinkMenuItem_UsesFormatPreservingCommand()
    {
        var source = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Host", "MainWindow.HomeEditing.cs");

        var methodIndex = source.IndexOf(
            "private void RemoveHyperlinkMenuItem_Click(object sender, RoutedEventArgs e)", System.StringComparison.Ordinal);
        methodIndex.Should().BeGreaterThan(-1, "a dedicated format-preserving handler must exist for the right-click Remove Hyperlink item");
        var closeIndex = source.IndexOf("\n    }", methodIndex, System.StringComparison.Ordinal);
        closeIndex.Should().BeGreaterThan(methodIndex);

        source[methodIndex..closeIndex].Should().Contain(
            "new ClearHyperlinksCommand(",
            "the right-click Remove Hyperlink item must preserve hyperlink formatting, matching Excel");
    }

    [Fact]
    public void RemoveHyperlinksCommand_StripFormattingBehaviorBackingRibbonClearHyperlinks()
    {
        // The ribbon's Home>Clear>Clear Hyperlinks path (via ClearHyperlinksMenuItem_Click above)
        // and its pinned unit test
        // HyperlinkCommandTests.RemoveHyperlinksCommand_RemovesHyperlinkAndResetsHyperlinkStyle
        // both rely on RemoveHyperlinksCommand actually stripping the visible hyperlink styling.
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
        // (there is no separate MainWindow.WorksheetContextMenu.cs file on that shell). Since R60
        // it dispatches ClearHyperlinks (ribbon-mirroring) to the format-stripping
        // RemoveSelectedRangeHyperlinks() handler and RemoveHyperlinks (right-click's own item) to
        // the format-preserving ClearSelectedRangeHyperlinks() handler -- these are DISTINCT
        // handlers, which is exactly the WPF host now mirrors above.
        var avaloniaMainWindowPath = Path.Combine(
            WorkspaceFileLocator.FindWorkspaceRoot(), "src", "FreeX.App.Avalonia", "MainWindow.WorksheetContextMenu.cs");
        File.Exists(avaloniaMainWindowPath)
            .Should().BeFalse("the Avalonia shell's worksheet context-menu handling lives in MainWindow.cs, not a dedicated file");

        var avaloniaSource = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Avalonia", "MainWindow.ApplicationCommandRouting.cs");
        avaloniaSource.Should().Contain(
            "RemoveHyperlinks = Handled(() => ClearSelectedRangeHyperlinks())");

        var sessionSource = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Services", "WorkbookSession.cs");
        var methodBody = ExtractMethodBody(sessionSource, 
            "public WorkbookCellEditResult ClearSelectedRangeHyperlinks()");
        methodBody.Should().NotBeEmpty();
        methodBody.Should().Contain("new ClearHyperlinksCommand(");
    }

    /// <summary>
    /// R128: extract a method body by BRACE MATCHING, not by a fixed character window.
    /// These tests previously took Math.Min(400..500, ...) characters from the method signature,
    /// which silently stopped covering the assertion as soon as the method grew. r128's multi-area
    /// widening added an explanatory comment plus a SelectionStyleCommandPlanner call, pushing the
    /// pinned "new ClearHyperlinksCommand(" past the 500-character cliff -- so the test failed even
    /// though the invariant it protects still held. A window that can drift off the thing it is
    /// checking is a check that stops firing for reasons unrelated to the defect.
    /// </summary>
    private static string ExtractMethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, System.StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var open = source.IndexOf('{', start);
        if (open < 0) return string.Empty;
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source.Substring(start, i - start + 1);
            }
        }
        return source.Substring(start);
    }
}
