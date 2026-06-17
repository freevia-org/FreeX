using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using Xunit;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for the HIGH QA finding "Collapsed-heading index drift": with a heading collapsed
/// <em>before</em> the selection, paragraph commands used a visible-ordinal index to address
/// <c>_model.Blocks</c>, which after <c>MergeHiddenBlocks</c> re-splices the hidden blocks back in is the
/// wrong slot — so the command mis-targeted (e.g. formatted the heading instead of the intended body
/// paragraph). The fix maps the visible ordinal to the real model index through the hidden-block offsets.
/// </summary>
public sealed class CollapsedHeadingDriftTests
{
    private static Paragraph Heading(string text) =>
        new(text) { StyleId = "Heading1" };

    // Build [Heading1, body, Heading1(target), targetBody]; collapsing the first heading hides "body",
    // so the target body paragraph's visible ordinal (2) differs from its model index (3).
    private static TextDocument BuildCollapsibleDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(Heading("First Heading"));
        doc.Blocks.Add(new Paragraph("hidden body"));
        doc.Blocks.Add(Heading("Second Heading"));
        doc.Blocks.Add(new Paragraph("target body"));
        return doc;
    }

    // Move the caret into the WPF paragraph whose plain text matches `text`.
    private static void PlaceCaretInParagraph(DocumentView view, string text)
    {
        var paragraph = view.Document.Blocks.OfType<WpfParagraph>()
            .First(p => new TextRange(p.ContentStart, p.ContentEnd).Text == text);
        view.CaretPosition = paragraph.ContentStart;
    }

    [StaFact]
    public void ParagraphCommand_TargetsCorrectModelBlock_WhenHeadingCollapsedBeforeSelection()
    {
        var doc = BuildCollapsibleDocument();
        var view = new DocumentView();
        view.LoadModel(doc);

        // Collapse the first heading (index 0), hiding "hidden body" from the view.
        view.CollapseHeading(0);
        view.IsHeadingCollapsed(0).Should().BeTrue();

        // Put the caret in the still-visible "target body" paragraph and apply a paragraph command.
        // SetLineSpacing sets the value directly (no toggle), so the assertion is unambiguous.
        PlaceCaretInParagraph(view, "target body");
        view.SetLineSpacing(2.0);

        var blocks = view.Model.Blocks.OfType<Paragraph>().ToList();
        var targetBody = blocks.Single(p => p.PlainText == "target body");
        var secondHeading = blocks.Single(p => p.PlainText == "Second Heading");
        var hiddenBody = blocks.Single(p => p.PlainText == "hidden body");

        targetBody.Formatting.LineSpacing.Should().Be(2.0, "the command must target the caret's paragraph");
        secondHeading.Formatting.LineSpacing.Should().NotBe(2.0, "the preceding heading must not be hit by index drift");
        hiddenBody.Formatting.LineSpacing.Should().NotBe(2.0, "the collapsed/hidden body must be untouched");
    }

    [StaFact]
    public void HiddenBlocks_AreRestoredOnCommit_WhenCollapsed()
    {
        var doc = BuildCollapsibleDocument();
        var view = new DocumentView();
        view.LoadModel(doc);
        view.CollapseHeading(0);

        // Editing while collapsed still commits the full document (hidden body re-spliced in order).
        PlaceCaretInParagraph(view, "target body");
        view.SetLineSpacing(2.0);

        view.Model.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("First Heading", "hidden body", "Second Heading", "target body");
    }
}
