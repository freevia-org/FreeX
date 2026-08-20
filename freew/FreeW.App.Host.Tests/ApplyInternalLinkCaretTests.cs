using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for freew-hyperlinks-nav F1: <see cref="DocumentView.ApplyInternalLink"/>
/// (Insert &gt; Links &gt; Link to Bookmark) must insert the internal-link run at the caret when
/// nothing is selected, the same as <see cref="DocumentView.InsertHyperlink"/> (Insert &gt; Links &gt;
/// Link) already does — not unconditionally append it to the end of the paragraph's Inlines.
///
/// <para>Runs on STA because tests create a real WPF <see cref="DocumentView"/>.</para>
/// </summary>
public sealed class ApplyInternalLinkCaretTests
{
    [StaFact]
    public void ApplyInternalLink_NoSelection_InsertsAtCaret_NotAtParagraphEnd()
    {
        var view = CreateView("See also: here.");
        // Caret between "See also: " (10 chars) and "here." — the middle of the paragraph, nothing
        // selected.
        view.MoveCaretToBlockForTest(0, 10);

        view.ApplyInternalLink("MyBookmark");

        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.PlainText.Should().Be(
            "See also: MyBookmarkhere.",
            "the bookmark link must land at the caret, matching Word and matching InsertHyperlink's " +
            "own caret-aware behaviour — not get appended after the paragraph's existing text");
    }

    /// <summary>
    /// Sibling no-regression: <see cref="DocumentView.InsertHyperlink"/> (the general Insert &gt;
    /// Links &gt; Link dialog) already used the caret-aware path before this fix and must be
    /// unaffected by it.
    /// </summary>
    [StaFact]
    public void InsertHyperlink_NoSelection_NoRegression_StillInsertsAtCaret()
    {
        var view = CreateView("See also: here.");
        view.MoveCaretToBlockForTest(0, 10);

        view.InsertHyperlink("LINK", "https://example.com");

        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.PlainText.Should().Be(
            "See also: LINKhere.",
            "InsertHyperlink's existing caret-aware insertion must keep working unchanged");
    }

    private static DocumentView CreateView(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));

        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }
}
