using System.IO;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentParagraphPaginationPlannerTests
{
    [Fact]
    public void OmittedWidowControl_BehavesLikeExplicitOff()
    {
        // Word's widow control guards a stranded line, not the whole paragraph, so the omitted
        // default must not reach keep-together -- otherwise a long ordinary paragraph jumps to the
        // next page instead of splitting. It therefore matches the explicit-off case below.
        DocumentParagraphPaginationPlanner
            .ShouldKeepParagraphTogether(ParagraphFormatting.Default)
            .Should().BeFalse();
    }

    [Fact]
    public void EmptyParagraphWithHiddenMark_CollapsesLikeWord()
    {
        // drawing.docx: an empty spacer paragraph between two tables whose mark is w:vanish. Word gives it
        // no height; laying it out as a normal empty line pushed the document a paragraph behind Word.
        var document = new TextDocument();
        var hiddenMark = RunFormatting.Default with { Hidden = true };
        var spacer = new Paragraph { MarkFormatting = hiddenMark };
        DocumentParagraphPaginationPlanner.IsCollapsedHiddenMarkParagraph(document, spacer).Should().BeTrue();

        var hiddenTextOnly = new Paragraph { MarkFormatting = hiddenMark };
        hiddenTextOnly.Runs.Add(new Run("secret", hiddenMark));
        DocumentParagraphPaginationPlanner.IsCollapsedHiddenMarkParagraph(document, hiddenTextOnly).Should().BeTrue();

        var visibleText = new Paragraph { MarkFormatting = hiddenMark };
        visibleText.Runs.Add(new Run("shown"));
        DocumentParagraphPaginationPlanner.IsCollapsedHiddenMarkParagraph(document, visibleText).Should().BeFalse();

        var pageBreak = new Paragraph { MarkFormatting = hiddenMark };
        pageBreak.Runs.Add(new Run(string.Empty) { IsPageBreak = true });
        DocumentParagraphPaginationPlanner.IsCollapsedHiddenMarkParagraph(document, pageBreak).Should().BeFalse();

        DocumentParagraphPaginationPlanner.IsCollapsedHiddenMarkParagraph(document, new Paragraph()).Should().BeFalse();
    }

    [Fact]
    public void ExplicitWidowControlOn_DoesNotKeepOrdinaryParagraphTogether()
    {
        // Widow control guards single lines (MinWidow/MinOrphan lines), not the whole paragraph.
        var formatting = ParagraphFormatting.Default with
        {
            WidowControl = true,
            WidowControlIsSet = true,
        };

        DocumentParagraphPaginationPlanner
            .ShouldKeepParagraphTogether(formatting)
            .Should().BeFalse();
    }

    [Fact]
    public void WidowOrphanMinimumLines_DefaultOnWhenOmittedOrExplicitOn_OffOnlyWhenExplicitOff()
    {
        DocumentParagraphPaginationPlanner
            .ResolveMinimumWidowOrphanLines(ParagraphFormatting.Default)
            .Should().Be(2);
        DocumentParagraphPaginationPlanner
            .ResolveMinimumWidowOrphanLines(ParagraphFormatting.Default with { WidowControl = true, WidowControlIsSet = true })
            .Should().Be(2);
        DocumentParagraphPaginationPlanner
            .ResolveMinimumWidowOrphanLines(ParagraphFormatting.Default with { WidowControl = false, WidowControlIsSet = true })
            .Should().Be(0);
    }

    [Theory]
    // total, fit, widow, keepLines, atTop, expected
    [InlineData(5, 5, true, false, false, 5)]   // fits entirely
    [InlineData(5, 4, true, false, false, 3)]   // one widow line: pull a second line forward
    [InlineData(5, 3, true, false, false, 3)]   // two lines carry over: fine
    [InlineData(5, 1, true, false, false, 0)]   // single orphan line: move the paragraph
    [InlineData(5, 0, true, false, false, 0)]
    [InlineData(3, 2, true, false, false, 0)]   // 3 lines: pulling the widow would orphan one line
    [InlineData(2, 1, true, false, false, 0)]
    [InlineData(4, 2, true, false, false, 2)]
    [InlineData(5, 4, false, false, false, 4)]  // widow control off: whatever fits
    [InlineData(5, 1, false, false, false, 1)]
    [InlineData(5, 4, true, true, false, 0)]    // keep-lines: move whole
    [InlineData(50, 40, true, true, true, 40)]  // at page top a too-tall paragraph still splits
    public void PlanLinesOnCurrentPage_FollowsWordWidowOrphanRule(
        int total, int fit, bool widow, bool keepLines, bool atTop, int expected)
    {
        DocumentParagraphPaginationPlanner
            .PlanLinesOnCurrentPage(total, fit, widow, keepLines, atTop)
            .Should().Be(expected);
    }

    [Fact]
    public void ExplicitWidowControlOff_DoesNotKeepOrdinaryParagraphTogether()
    {
        var formatting = ParagraphFormatting.Default with
        {
            WidowControl = false,
            WidowControlIsSet = true,
        };

        DocumentParagraphPaginationPlanner
            .ShouldKeepParagraphTogether(formatting)
            .Should().BeFalse();
    }

    [Fact]
    public void ExplicitKeepLinesTogether_OverridesWidowControlOff()
    {
        var formatting = ParagraphFormatting.Default with
        {
            KeepLinesTogether = true,
            WidowControl = false,
            WidowControlIsSet = true,
        };

        DocumentParagraphPaginationPlanner
            .ShouldKeepParagraphTogether(formatting)
            .Should().BeTrue();
    }

    [Fact]
    public void TableCellAndNonTextObjectPaths_DoNotInheritOrdinaryDefault()
    {
        var formatting = ParagraphFormatting.Default;

        DocumentParagraphPaginationPlanner
            .ShouldKeepParagraphTogether(formatting, isTableCell: true)
            .Should().BeFalse();
        DocumentParagraphPaginationPlanner
            .ShouldKeepParagraphTogether(formatting, hasNonTextLayoutObject: true)
            .Should().BeFalse();
    }

    [Fact]
    public void PlatformDocumentViews_DelegateOrdinaryParagraphPaginationToSharedPlanner()
    {
        var hostSource = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avaloniaSource = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        hostSource.Should().Contain("DocumentParagraphPaginationPlanner.ShouldKeepParagraphTogether(");
        avaloniaSource.Should().Contain("DocumentParagraphPaginationPlanner.ShouldKeepParagraphTogether(");
        hostSource.Should().Contain("DocumentParagraphPaginationPlanner.ResolveMinimumWidowOrphanLines(");
        avaloniaSource.Should().Contain("DocumentParagraphPaginationPlanner.PlanLinesOnCurrentPage(");
    }

    private static string ReadSource(params string[] relativeParts)
    {
        var parts = new string[relativeParts.Length + 1];
        parts[0] = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
        return File.ReadAllText(Path.Combine(parts));
    }
}
