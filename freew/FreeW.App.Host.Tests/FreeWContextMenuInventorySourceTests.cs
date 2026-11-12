using System.IO;
using System.Text.RegularExpressions;

namespace FreeW.App.Host.Tests;

public sealed class FreeWContextMenuInventorySourceTests
{
    [Fact]
    public void WpfHost_HasExactlyTenExplicitMenuConstructionsIncludingGrammarMenu()
    {
        var root = HostRoot();
        var source = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText);

        // ThemeGallery's shared-planner-backed Effects picker is the seventh explicit WPF menu
        // construction added alongside the existing paragraph-spacing picker; the editor's grammar
        // menu (DocumentView.Proofing.cs, planned by ProofingContextMenuPlanner) is the eighth. The
        // Styles gallery swatch menu (StyleGalleryPlanner) and the Modify Style dialog's Format menu
        // (StyleDefinitionDialogSession.FormatMenu) are the ninth and tenth. The legacy drop-down form
        // field's in-place entry list (DocumentView.LegacyFormFields.cs, picks applied through
        // LegacyFormFieldInteractionPlanner.SelectDropDownEntry) is the eleventh.
        source.Sum(text => Regex.Matches(text, @"new\s+ContextMenu\s*\(").Count).Should().Be(11);
    }

    [Fact]
    public void EveryExplicitWpfFamily_ConsumesTheSharedPlannerAuthority()
    {
        Read("Editing", "DocumentView.cs").Should().Contain("FreeWContextMenuPlanner.BuildContentControl");
        ReadPresentation("Panes", "NavigationPaneSession.cs")
            .Should().Contain("FreeWContextMenuPlanner.BuildOutline");
        // The Special list is per box and wildcard-aware, so the dialog asks its session, which
        // builds it from the same catalog FreeWContextMenuPlanner.FindSpecialCharacters exposes.
        Read("FindReplaceDialog.cs").Should().Contain("_session.BuildSpecialItems(")
            .And.NotContain("SpecialChars =");
        ReadPresentation("ContextMenus", "FreeWContextMenuPlanner.cs")
            .Should().Contain("FindReplaceDialogPlanner");
        Read("Ribbon", "ThemeGallery.cs").Should()
            .Contain("FreeWContextMenuPlanner.BuildParagraphSpacing")
            .And.Contain("FreeWContextMenuPlanner.BuildEffects");
        Read("Ribbon", "TableStylesGallery.cs").Should().Contain("FreeWContextMenuPlanner.BuildTableStyles");
        Read("Editing", "DocumentView.Proofing.cs").Should().Contain("ProofingContextMenuPlanner.BuildGrammar");
        Read("Ribbon", "StylesGallery.cs").Should().Contain("StyleGalleryPlanner.BuildContextMenu(");
        Read("StyleDefinitionDialog.cs").Should().Contain("foreach (var entry in session.FormatMenu)");
        Read("Editing", "DocumentView.LegacyFormFields.cs").Should().Contain("LegacyFormFieldInteractionPlanner.SelectDropDownEntry");
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
