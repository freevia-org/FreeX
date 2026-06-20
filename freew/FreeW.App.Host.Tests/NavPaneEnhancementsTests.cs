using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for the navigation-pane enhancements: heading reorder (move a heading and its whole subtree
/// through the editor's reversible <see cref="DocumentView.MoveHeading"/>) and search-the-document (the
/// pane jumps to a body match via <see cref="DocumentView.BringBlockIntoView"/>, located with the shared
/// <see cref="TextSearch"/> helper — the same matching the pane runs over the live model).
/// </summary>
public sealed class NavPaneEnhancementsTests
{
    private static Paragraph H(int level, string text) =>
        new(text) { StyleId = level == 0 ? "Title" : "Heading" + level };

    // [H1 "Alpha", body, H2 "Alpha.1", body, H1 "Bravo", "needle body"]
    private static TextDocument Sample()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(H(1, "Alpha"));
        doc.Blocks.Add(new Paragraph("alpha intro"));
        doc.Blocks.Add(H(2, "Alpha.1"));
        doc.Blocks.Add(new Paragraph("alpha one body"));
        doc.Blocks.Add(H(1, "Bravo"));
        doc.Blocks.Add(new Paragraph("the needle lives here"));
        return doc;
    }

    [StaFact]
    public void MoveHeading_Down_MovesSubtreeAndIsUndoable()
    {
        var view = new DocumentView();
        view.LoadModel(Sample());

        // Move the "Alpha" heading (index 0) and its A.1 subtree down past sibling "Bravo".
        var newIndex = view.MoveHeading(0, moveUp: false);

        view.Model.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Bravo", "the needle lives here", "Alpha", "alpha intro", "Alpha.1", "alpha one body");
        view.Model.Blocks[newIndex].Should().BeOfType<Paragraph>()
            .Which.PlainText.Should().Be("Alpha", "the returned index tracks the moved heading");

        // The reorder went through the undo/redo bus, so a single undo restores the original order.
        view.Commands.Undo().Should().BeTrue();
        view.Model.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Alpha", "alpha intro", "Alpha.1", "alpha one body", "Bravo", "the needle lives here");
    }

    [StaFact]
    public void MoveHeading_Up_AtFirstSibling_IsNoOp()
    {
        var view = new DocumentView();
        view.LoadModel(Sample());

        var index = view.MoveHeading(0, moveUp: true);

        index.Should().Be(0);
        view.Commands.CanUndo.Should().BeFalse("a no-op move must not push an undo entry");
    }

    [StaFact]
    public void NavSearch_FindsBodyMatch_AndBringsItIntoView()
    {
        var view = new DocumentView();
        view.LoadModel(Sample());
        view.CommitToModel();

        // The pane scans every body block with the shared TextSearch helper; assert the term is found in
        // exactly the block the pane would jump to, then drive that jump (BringBlockIntoView) and confirm
        // the caret lands inside the matching paragraph.
        var hits = Enumerable.Range(0, view.Model.Blocks.Count)
            .Where(i => view.Model.Blocks[i] is Paragraph p
                && TextSearch.FindAll(p.PlainText, "needle", matchCase: false, wholeWord: false).Any())
            .ToList();

        hits.Should().ContainSingle().Which.Should().Be(5);

        view.BringBlockIntoView(hits[0]);

        var caretText = view.CaretPosition.Paragraph is { } wpfParagraph
            ? new System.Windows.Documents.TextRange(wpfParagraph.ContentStart, wpfParagraph.ContentEnd).Text
            : string.Empty;
        caretText.Should().Contain("needle");
    }
}
