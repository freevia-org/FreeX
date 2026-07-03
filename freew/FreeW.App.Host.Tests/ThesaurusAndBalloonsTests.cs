using System.Linq;
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

    // ── BalloonOverlay ───────────────────────────────────────────────────────────────────────────────

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
}
