using System.IO;
using System.Text.RegularExpressions;

namespace FreeW.App.Host.Tests;

public sealed class FreeWContextMenuInventorySourceTests
{
    [Fact]
    public void WpfHost_HasExactlySevenExplicitMenuConstructionsIncludingEffectsGallery()
    {
        var root = HostRoot();
        var source = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText);

        // ThemeGallery's shared-planner-backed Effects picker is the seventh explicit WPF menu
        // construction added alongside the existing paragraph-spacing picker.
        source.Sum(text => Regex.Matches(text, @"new\s+ContextMenu\s*\(").Count).Should().Be(7);
    }

    [Fact]
    public void EveryExplicitWpfFamily_ConsumesTheSharedPlannerAuthority()
    {
        Read("Editing", "DocumentView.cs").Should().Contain("FreeWContextMenuPlanner.BuildContentControl");
        ReadPresentation("Panes", "NavigationPaneSession.cs")
            .Should().Contain("FreeWContextMenuPlanner.BuildOutline");
        Read("FindReplaceDialog.cs").Should().Contain("FreeWContextMenuPlanner.FindSpecialCharacters")
            .And.NotContain("SpecialChars =");
        Read("Ribbon", "ThemeGallery.cs").Should()
            .Contain("FreeWContextMenuPlanner.BuildParagraphSpacing")
            .And.Contain("FreeWContextMenuPlanner.BuildEffects");
        Read("Ribbon", "TableStylesGallery.cs").Should().Contain("FreeWContextMenuPlanner.BuildTableStyles");
    }

    private static string HostRoot() => Path.Combine(
        WorkspaceRoot(),
        "freew",
        "FreeW.App.Host");

    private static string WorkspaceRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");

    private static string Read(params string[] parts) =>
        File.ReadAllText(parts.Aggregate(HostRoot(), Path.Combine));

    private static string ReadPresentation(params string[] parts) =>
        File.ReadAllText(parts.Aggregate(
            Path.Combine(WorkspaceRoot(), "freew", "FreeW.App.Presentation"),
            Path.Combine));
}
