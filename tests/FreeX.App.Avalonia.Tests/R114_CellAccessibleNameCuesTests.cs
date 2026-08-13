using FreeX.App.Presentation.Accessibility;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-114 regression test for the confirmed finding "Avalonia grid's screen-reader cell name
/// silently drops comment/formula/merge/hyperlink cues that WPF announces": WPF's
/// <c>GridViewCellAutomationPeer.GetNameCore</c> (FreeX.App.UI/GridView.cs:688-708, via
/// <c>GridView.BuildCellAnnouncementName</c>) appends "has &lt;note&gt;", "is a formula",
/// "is merged", and "has a hyperlink" cues to a cell's UIA accessible name whenever it carries that
/// metadata, but the Avalonia shell's <c>MainWindow.FormatCellAccessibleName</c> only ever emitted
/// the bare "&lt;address&gt;" or "&lt;address&gt;: &lt;value&gt;" -- silently dropping every cue a
/// Linux/macOS screen-reader user would otherwise get. Fixed by threading comment/formula/merge/
/// hyperlink metadata through <c>FormatCellAccessibleName</c> (mirroring
/// <c>GridView.BuildCellAnnouncementName</c>'s cue text and ordering exactly) and wiring it from the
/// interactive cell border's construction site (comment via the existing <c>commentDisplay</c>
/// plumbing, formula via <c>DisplayCell.Formula</c>, merge via the existing <c>mergeRegion</c>
/// plumbing, hyperlink via a direct <c>Sheet.Hyperlinks</c> lookup -- the same source
/// <c>FormatHyperlinkTooltip</c> already uses for the hover tooltip).
/// </summary>
public sealed class R114_CellAccessibleNameCuesTests
{
    [Fact]
    public void FormatCellAccessibleName_PlainCell_NoRegression()
    {
        // No-regression sibling: a plain cell with no metadata still gets the original bare
        // "<address>: <value>" name -- adding cue support must not add stray text when there is
        // nothing to announce.
        var name = CellAnnouncementPlanner.BuildName("A1", "42", default);

        name.Should().Be("A1: 42");
    }

    [Fact]
    public void FormatCellAccessibleName_EmptyCell_NoRegression()
    {
        var name = CellAnnouncementPlanner.BuildName("B2", "", default);

        name.Should().Be("B2");
    }

    [Fact]
    public void FormatCellAccessibleName_FormulaCell_AppendsIsAFormulaCue()
    {
        // This is the case that FAILS before the fix: FormatCellAccessibleName previously had no
        // isFormula parameter at all and could never emit this cue.
        var name = CellAnnouncementPlanner.BuildName("C3", "10", new CellAnnouncementMetadata(IsFormula: true));

        name.Should().Be("C3: 10, is a formula");
    }

    [Fact]
    public void FormatCellAccessibleName_MergedCell_AppendsIsMergedCue()
    {
        var name = CellAnnouncementPlanner.BuildName("D4", "Title", new CellAnnouncementMetadata(IsMerged: true));

        name.Should().Be("D4: Title, is merged");
    }

    [Fact]
    public void FormatCellAccessibleName_HyperlinkCell_AppendsHasAHyperlinkCue()
    {
        var name = CellAnnouncementPlanner.BuildName("E5", "Visit", new CellAnnouncementMetadata(HasHyperlink: true));

        name.Should().Be("E5: Visit, has a hyperlink");
    }

    [Fact]
    public void FormatCellAccessibleName_CommentedCell_AppendsHasNoteCue_LowerCasingTheTitle()
    {
        var name = CellAnnouncementPlanner.BuildName(
            "F6",
            "7",
            new CellAnnouncementMetadata(HasComment: true, CommentTitle: "Note"));

        name.Should().Be("F6: 7, has note");
    }

    [Fact]
    public void FormatCellAccessibleName_AllCuesTogether_AreOrderedAndCommaJoined_MatchingWpfBuilder()
    {
        // Mirrors GridView.BuildCellAnnouncementName's cue order: comment, formula, merged,
        // hyperlink (data validation/locked are deliberately excluded on both shells).
        var name = CellAnnouncementPlanner.BuildName(
            "G7",
            "100",
            new CellAnnouncementMetadata(
                HasComment: true,
                CommentTitle: "Threaded Comment",
                IsFormula: true,
                IsMerged: true,
                HasHyperlink: true));

        name.Should().Be("G7: 100, has threaded comment, is a formula, is merged, has a hyperlink");
    }
}
