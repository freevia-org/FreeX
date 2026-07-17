using System.Linq;
using System.Windows.Controls;
using System.Windows.Shapes;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Tests for the local thesaurus lookup (W25 Feature 1) and the balloon overlay (W25 Feature 2).
///
/// Thesaurus tests verify that <see cref="ThesaurusLookup"/> loads its bundled dataset and returns
/// plausible synonym entries for well-known English headwords.
///
/// Balloon tests verify that <see cref="BalloonOverlay.Rebuild"/> produces the expected number of
/// visual children from the live document model, and that toggle correctly enables/disables the strip.
/// </summary>
public sealed class ThesaurusAndBalloonsTests
{
    // ── ThesaurusLookup ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The singleton loads without exception and advertises a meaningful headword count.
    /// </summary>
    [Fact]
    public void ThesaurusLookup_LoadsDataset_ReturnsNonTrivialHeadwordCount()
    {
        var count = ThesaurusLookup.Instance.HeadwordCount;
        count.Should().BeGreaterThan(300, "the bundled compact thesaurus covers a few hundred common English head words");
    }

    /// <summary>
    /// Looking up a word that is definitely in the dataset returns at least one sense with synonyms.
    /// "happy" is one of the most-common English adjectives and is present in every thesaurus.
    /// </summary>
    [Fact]
    public void ThesaurusLookup_KnownWord_ReturnsSensesWithSynonyms()
    {
        var entry = ThesaurusLookup.Instance.Lookup("happy");

        entry.Should().NotBeNull("'happy' is a headword in the bundled dataset");
        entry!.Senses.Should().NotBeEmpty();
        entry.Senses[0].Synonyms.Should().NotBeEmpty();
    }

    /// <summary>
    /// Lookup is case-insensitive: "Happy", "HAPPY", and "happy" all resolve to the same entry.
    /// </summary>
    [Theory]
    [InlineData("happy")]
    [InlineData("Happy")]
    [InlineData("HAPPY")]
    public void ThesaurusLookup_CaseInsensitive_ReturnsSameEntry(string input)
    {
        var entry = ThesaurusLookup.Instance.Lookup(input);
        entry.Should().NotBeNull($"lookup for '{input}' should be case-insensitive");
    }

    /// <summary>
    /// Looking up a word that is not in the dataset returns null without throwing.
    /// </summary>
    [Fact]
    public void ThesaurusLookup_UnknownWord_ReturnsNull()
    {
        var entry = ThesaurusLookup.Instance.Lookup("xyzzy_notaword_12345");
        entry.Should().BeNull();
    }

    /// <summary>
    /// Looking up a second well-known word ("ability") confirms the dataset is actually populated
    /// with multiple independent headwords.
    /// </summary>
    [Fact]
    public void ThesaurusLookup_AnotherKnownWord_ReturnsSynonyms()
    {
        var entry = ThesaurusLookup.Instance.Lookup("ability");
        entry.Should().NotBeNull("'ability' is one of the headwords in the bundled dataset");
        entry!.Senses.SelectMany(s => s.Synonyms).Should().NotBeEmpty();
    }

    [StaFact]
    public void ReplaceCaretWord_ReplacesRenderedWordAndCommitsModel()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("A happy day"));
        var view = new DocumentView();
        view.LoadModel(doc);

        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.CaretPosition = PositionAtTextOffset(paragraph, 4);

        view.ReplaceCaretWord("cheerful");

        var rendered = new System.Windows.Documents.TextRange(
            view.Document.ContentStart,
            view.Document.ContentEnd).Text;
        rendered.Should().Contain("A cheerful day");
        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Be("A cheerful day");
    }

    // ── BalloonOverlay ───────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void DocumentView_ReviewAnchors_UseLaidOutParagraphGeometry()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("First paragraph."));
        document.Blocks.Add(new Paragraph("Second paragraph."));

        var editor = new DocumentView();
        editor.LoadModel(document);
        var window = new System.Windows.Window
        {
            Content = editor,
            Width = 420,
            Height = 260,
            ShowInTaskbar = false,
            WindowStyle = System.Windows.WindowStyle.None,
        };

        try
        {
            window.Show();
            window.UpdateLayout();

            var firstAnchorY = editor.TryGetReviewAnchorY(0, 0);
            var secondAnchorY = editor.TryGetReviewAnchorY(1, 0);

            firstAnchorY.Should().NotBeNull();
            secondAnchorY.Should().NotBeNull();
            secondAnchorY!.Value.Should().BeGreaterThan(firstAnchorY!.Value);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// When balloons mode is disabled (default), the canvas has Width=0 and no children.
    /// </summary>
    [StaFact]
    public void BalloonOverlay_InitialState_DisabledAndEmpty()
    {
        var editor = MakeEditorWithRevisions();
        var overlay = new BalloonOverlay(editor);

        overlay.BalloonsEnabled.Should().BeFalse();
        ((System.Windows.Controls.Canvas)overlay.Visual).Width.Should().Be(0);
        ((System.Windows.Controls.Canvas)overlay.Visual).Children.Count.Should().Be(0);
    }

    /// <summary>
    /// Enabling balloons (Toggle) sets BalloonsEnabled to true and rebuilds from the live model:
    /// for a document with 2 tracked-change revisions and 1 comment, Rebuild should produce
    /// at least 3 visual groups (leader line + rectangle + header per balloon).
    /// </summary>
    [StaFact]
    public void BalloonOverlay_Enable_ProducesBalloonsForEachDocumentItem()
    {
        var editor = MakeEditorWithRevisions();
        var overlay = new BalloonOverlay(editor);

        overlay.Toggle();   // enable

        overlay.BalloonsEnabled.Should().BeTrue();
        var canvas = (System.Windows.Controls.Canvas)overlay.Visual;
        canvas.Width.Should().BeGreaterThan(0);

        // Each balloon consists of at minimum: 1 leader Line + 1 Rectangle + 1 header TextBlock.
        // We have 2 revisions + 1 comment = 3 balloons, so at least 9 children.
        canvas.Children.Count.Should().BeGreaterThanOrEqualTo(3 * 3,
            "each balloon contributes at least a leader line, a rectangle, and a header label");
    }

    [StaFact]
    public void BalloonOverlay_Enable_RendersSharedCardMetadata()
    {
        var editor = MakeEditorWithRevisions();
        var comment = editor.Model.Comments[0];
        comment.DateXml = "2026-07-02T11:00:00Z";
        comment.Resolved = true;
        comment.AddReply(10, "fixed", "Alice", "A");
        var overlay = new BalloonOverlay(editor);

        overlay.Enable();

        var canvas = (Canvas)overlay.Visual;
        var texts = canvas.Children.OfType<TextBlock>().Select(block => block.Text).ToArray();
        texts.Should().Contain("Resolved - 1 reply - 2026-07-02",
            "WPF should render the shared resolved/reply/date metadata line");
        texts.Should().Contain(text => text.Contains("Carol") && text.Contains("Resolved comment"),
            "the card header should use the shared comment kind label");
        texts.Should().Contain("Tracked change",
            "revision cards should expose their shared metadata line too");
    }

    [StaFact]
    public void BalloonOverlay_Enable_UsesSharedViewportAnchoredCollisionLayout()
    {
        var editor = MakeEditorWithRevisions();
        var overlay = new BalloonOverlay(editor);

        overlay.Enable();

        var canvas = (Canvas)overlay.Visual;
        var leaders = canvas.Children.OfType<Line>().ToArray();
        var rectangles = canvas.Children.OfType<Rectangle>().ToArray();
        var balloonTops = rectangles.Select(Canvas.GetTop).ToArray();

        leaders.Should().HaveCount(3);
        rectangles.Should().HaveCount(3);
        balloonTops.Should().BeInAscendingOrder();
        balloonTops[0].Should().BeGreaterThan(80, "balloons should track their viewport anchors instead of always starting at the top gap");
        balloonTops.Zip(balloonTops.Skip(1), (previous, next) => next - previous)
            .Should().OnlyContain(delta => delta >= 64);
        leaders.Select(leader => leader.Y1).Should().BeInAscendingOrder();

        for (var i = 0; i < rectangles.Length; i++)
            leaders[i].Y2.Should().BeApproximately(balloonTops[i] + 28, 0.001);
    }

    /// <summary>
    /// Toggle twice returns to disabled state with an empty canvas.
    /// </summary>
    [StaFact]
    public void BalloonOverlay_ToggleTwice_ReturnsToClearedDisabledState()
    {
        var editor = MakeEditorWithRevisions();
        var overlay = new BalloonOverlay(editor);

        overlay.Toggle();   // enable
        overlay.Toggle();   // disable

        overlay.BalloonsEnabled.Should().BeFalse();
        ((System.Windows.Controls.Canvas)overlay.Visual).Width.Should().Be(0);
        ((System.Windows.Controls.Canvas)overlay.Visual).Children.Count.Should().Be(0);
    }

    /// <summary>
    /// Rebuild is a no-op when balloons mode is disabled — the canvas stays empty.
    /// </summary>
    [StaFact]
    public void BalloonOverlay_RebuildWhileDisabled_DoesNotPopulateCanvas()
    {
        var editor = MakeEditorWithRevisions();
        var overlay = new BalloonOverlay(editor);

        overlay.Rebuild();   // called from TextChanged while disabled

        ((System.Windows.Controls.Canvas)overlay.Visual).Children.Count.Should().Be(0);
    }

    /// <summary>
    /// With an empty document (no revisions, no comments) and balloons enabled, Rebuild produces
    /// zero children — the canvas is open but shows nothing.
    /// </summary>
    [StaFact]
    public void BalloonOverlay_EmptyDocument_NoBalloons()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var overlay = new BalloonOverlay(view);

        overlay.Enable();

        ((System.Windows.Controls.Canvas)overlay.Visual).Children.Count.Should().Be(0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static DocumentView MakeEditorWithRevisions()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var p0 = new Paragraph();
        p0.Runs.Add(new Run("Hello ") { CommentId = 0 });
        p0.Runs.Add(Run.CommentReference(0));
        p0.Runs.Add(new Run("world") { Revision = RevisionKind.Inserted, RevisionAuthor = "Alice" });
        p0.Runs.Add(new Run(" old") { Revision = RevisionKind.Deleted, RevisionAuthor = "Bob" });
        doc.Blocks.Add(p0);

        // Add a comment to the model (id=0, text, author).
        doc.Comments[0] = new Comment(0, "Looks good!", "Carol", "C");

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static System.Windows.Documents.TextPointer PositionAtTextOffset(
        System.Windows.Documents.Paragraph paragraph,
        int offset)
    {
        var remaining = Math.Max(0, offset);
        var pointer = paragraph.ContentStart;
        while (pointer is not null && pointer.CompareTo(paragraph.ContentEnd) < 0)
        {
            if (pointer.GetPointerContext(System.Windows.Documents.LogicalDirection.Forward) == System.Windows.Documents.TextPointerContext.Text)
            {
                var text = pointer.GetTextInRun(System.Windows.Documents.LogicalDirection.Forward);
                if (remaining <= text.Length)
                    return pointer.GetPositionAtOffset(remaining, System.Windows.Documents.LogicalDirection.Forward) ?? pointer;

                remaining -= text.Length;
                pointer = pointer.GetPositionAtOffset(text.Length, System.Windows.Documents.LogicalDirection.Forward);
            }
            else
            {
                pointer = pointer.GetNextContextPosition(System.Windows.Documents.LogicalDirection.Forward);
            }
        }

        return paragraph.ContentEnd;
    }
}
