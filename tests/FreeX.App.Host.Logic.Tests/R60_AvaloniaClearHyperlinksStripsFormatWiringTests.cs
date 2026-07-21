using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R60-meta-2: r59 re-pointed BOTH the right-click "Remove Hyperlink" action AND the ribbon/right-click
/// "Clear Hyperlinks" action to the format-PRESERVING ClearHyperlinksCommand on the Avalonia shell (both
/// funneled through the single ClearSelectedRangeHyperlinks() helper), orphaning the format-STRIPPING
/// RemoveHyperlinksCommand entirely. Excel's actual behavior: Home&gt;Clear&gt;Remove Hyperlinks (and the
/// equivalent right-click Clear submenu entry) STRIPS the hyperlink's blue/underline formatting; only
/// right-click's top-level "Remove Hyperlink" item PRESERVES it. The fix adds
/// WorkbookSession.RemoveSelectedRangeHyperlinks()/MainWindow.RemoveSelectedRangeHyperlinks() (format
/// stripping via RemoveHyperlinksCommand) and re-points every "Clear Hyperlinks" entry point (ribbon
/// dictionary, native/flyout menu items, and the WorksheetContextMenuAction.ClearHyperlinks case) to it,
/// while leaving WorksheetContextMenuAction.RemoveHyperlinks on the original format-preserving path.
/// </summary>
public sealed class R60_AvaloniaClearHyperlinksStripsFormatWiringTests
{
    [Fact]
    public void AvaloniaRibbonClearHyperlinksEntry_RoutesToFormatStrippingHandler()
    {
        var source = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", "MainWindow.cs");

        // Pre-fix this dictionary entry pointed at ClearSelectedRangeHyperlinks (format-preserving),
        // so the ribbon's Home>Clear>Remove Hyperlinks action never actually stripped formatting.
        source.Should().Contain(
            "[\"Clear Hyperlinks\"] = RemoveSelectedRangeHyperlinks,",
            "the ribbon's Clear Hyperlinks entry must route to the format-stripping handler, matching Excel");

        var methodIndex = source.IndexOf("private void RemoveSelectedRangeHyperlinks()", System.StringComparison.Ordinal);
        methodIndex.Should().BeGreaterThan(-1, "a dedicated format-stripping handler must exist");
        var methodLength = System.Math.Min(500, source.Length - methodIndex);
        source.Substring(methodIndex, methodLength).Should().Contain("_session.RemoveSelectedRangeHyperlinks()");
    }

    [Fact]
    public void AvaloniaContextMenuClearHyperlinksAction_RoutesToFormatStrippingHandler()
    {
        var source = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", "MainWindow.cs");

        var caseIndex = source.IndexOf("case WorksheetContextMenuAction.ClearHyperlinks:", System.StringComparison.Ordinal);
        caseIndex.Should().BeGreaterThan(-1);
        var breakIndex = source.IndexOf("break;", caseIndex, System.StringComparison.Ordinal);
        source[caseIndex..breakIndex].Should().Contain(
            "RemoveSelectedRangeHyperlinks",
            "the right-click Clear submenu's Clear Hyperlinks entry mirrors ribbon Home>Clear semantics and must strip formatting");
    }

    [Fact]
    public void AvaloniaContextMenuRemoveHyperlinksAction_StillRoutesToFormatPreservingHandler()
    {
        // Sibling no-regression case: the true right-click "Remove Hyperlink" item (distinct from the
        // Clear submenu's "Clear Hyperlinks" entry) must keep preserving formatting, exactly as before.
        var source = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", "MainWindow.cs");

        var caseIndex = source.IndexOf("case WorksheetContextMenuAction.RemoveHyperlinks:", System.StringComparison.Ordinal);
        caseIndex.Should().BeGreaterThan(-1);
        var breakIndex = source.IndexOf("break;", caseIndex, System.StringComparison.Ordinal);
        var caseBody = source[caseIndex..breakIndex];
        caseBody.Should().Contain("ClearSelectedRangeHyperlinks");
        caseBody.Should().NotContain("RemoveSelectedRangeHyperlinks");
    }

    [Fact]
    public void WorkbookSession_RemoveSelectedRangeHyperlinks_UsesFormatStrippingCommand()
    {
        var source = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Services", "WorkbookSession.cs");

        var methodIndex = source.IndexOf(
            "public WorkbookCellEditResult RemoveSelectedRangeHyperlinks()", System.StringComparison.Ordinal);
        methodIndex.Should().BeGreaterThan(-1, "a dedicated format-stripping session method must exist");
        var methodLength = System.Math.Min(400, source.Length - methodIndex);
        source.Substring(methodIndex, methodLength).Should().Contain("new RemoveHyperlinksCommand(");
    }

    [Fact]
    public void WorkbookSession_ClearSelectedRangeHyperlinks_StillUsesFormatPreservingCommand()
    {
        // Sibling no-regression case: the original preserving method must be untouched.
        var source = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Services", "WorkbookSession.cs");

        var methodIndex = source.IndexOf(
            "public WorkbookCellEditResult ClearSelectedRangeHyperlinks()", System.StringComparison.Ordinal);
        methodIndex.Should().BeGreaterThan(-1);
        var methodLength = System.Math.Min(400, source.Length - methodIndex);
        source.Substring(methodIndex, methodLength).Should().Contain("new ClearHyperlinksCommand(");
    }
}
