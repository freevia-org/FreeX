using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Proofing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// R136 (Avalonia DocumentView group): covers two classes of undo-atomicity/state-leak bugs found in a
/// file-wide sweep of <c>DocumentView.cs</c>.
///
/// (1) Several body-caret gestures (InsertText's structural-paragraph fallback, content-control inserts,
/// citation inserts, and the Enter-key paragraph-break fallback) route a selection replacement as TWO
/// separate, ungrouped bus commands: <c>DeleteSelection()</c> (or <c>DeleteCellSelection</c>) followed by a
/// second <c>_bus.Execute</c> for the insert/split. Without an explicit undo group, one Ctrl+Z only undoes
/// the second command and leaves the originally-selected text permanently deleted. Each gesture here is
/// forced onto the "structurally special" renderer-owned fallback path by giving the target paragraph a
/// preserved bookmark boundary (<see cref="IsPortableBodyTextParagraph"/> equivalent — fails the shared
/// editing session's portable-span fast path), which is exactly the situation the finding describes:
/// "any paragraph carrying ... a bookmark boundary".
///
/// (2) Review &gt; Ignore All spelling state (<c>_ignoredProofingWords</c>) lived on the editor instance
/// instead of being scoped to the open document, so words ignored in one document stayed silently ignored
/// after loading a different document into the same view.
/// </summary>
public sealed class DocumentViewStructuralInsertUndoGroupingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // ---- Finding A: InsertText's structural-paragraph fallback ----------------------------------------

    [Fact]
    public async Task InsertTextOverSelectionInStructuralParagraph_UndoesInOneStep()
    {
        IReadOnlyList<string> edited = [];
        IReadOnlyList<string> undone = [];
        var canUndoAfterUndo = true;

        await Session.Dispatch(() =>
        {
            var view = BuildStructuralView("abcdef");
            view.SetSelectionRangePublic(0, 2, 0, 5);

            view.InsertText("Z");

            edited = Paragraphs(view);
            view.Undo();
            undone = Paragraphs(view);
            canUndoAfterUndo = view.CanUndo;
        }, CancellationToken.None);

        edited.Should().Equal("abZf");
        undone.Should().Equal(["abcdef"], "a single undo must restore the whole gesture, including the deleted selection");
        canUndoAfterUndo.Should().BeFalse("the fallback delete+insert must be one undo entry, not two");
    }

    /// <summary>Sibling no-regression: the collapsed-caret (no selection) fallback path must still insert
    /// plainly and remain undoable in one step after wrapping the selection branch in an undo group.</summary>
    [Fact]
    public async Task InsertTextAtCollapsedCaretInStructuralParagraph_StillInsertsAndUndoesCleanly()
    {
        IReadOnlyList<string> edited = [];
        IReadOnlyList<string> undone = [];

        await Session.Dispatch(() =>
        {
            var view = BuildStructuralView("abcdef");
            view.MoveCaretToBlock(0, 3);

            view.InsertText("Z");

            edited = Paragraphs(view);
            view.Undo();
            undone = Paragraphs(view);
        }, CancellationToken.None);

        edited.Should().Equal("abcZdef");
        undone.Should().Equal("abcdef");
    }

    // ---- Finding B: InsertBodyContentControlRun (all six Developer-tab content-control commands) ------

    [Fact]
    public async Task InsertContentControlOverSelectionInStructuralParagraph_UndoesInOneStep()
    {
        IReadOnlyList<string> edited = [];
        IReadOnlyList<string> undone = [];
        var canUndoAfterUndo = true;

        await Session.Dispatch(() =>
        {
            var view = BuildStructuralView("abcdef");
            view.SetSelectionRangePublic(0, 2, 0, 5);

            view.InsertCheckBoxControl();

            edited = Paragraphs(view);
            view.Undo();
            undone = Paragraphs(view);
            canUndoAfterUndo = view.CanUndo;
        }, CancellationToken.None);

        edited.Should().Equal($"ab{ContentControl.UncheckedGlyph}f");
        undone.Should().Equal(["abcdef"], "a single undo must restore the whole gesture, including the deleted selection");
        canUndoAfterUndo.Should().BeFalse("the delete+insert must be one undo entry, not two");
    }

    /// <summary>Sibling no-regression: inserting a content control with no active selection is unaffected
    /// by the new undo grouping.</summary>
    [Fact]
    public async Task InsertContentControlAtCollapsedCaretInStructuralParagraph_StillInsertsAndUndoesCleanly()
    {
        IReadOnlyList<string> edited = [];
        IReadOnlyList<string> undone = [];

        await Session.Dispatch(() =>
        {
            var view = BuildStructuralView("abcdef");
            view.MoveCaretToBlock(0, 3);

            view.InsertCheckBoxControl();

            edited = Paragraphs(view);
            view.Undo();
            undone = Paragraphs(view);
        }, CancellationToken.None);

        edited.Should().Equal($"abc{ContentControl.UncheckedGlyph}def");
        undone.Should().Equal("abcdef");
    }

    // ---- Finding C: InsertCitation --------------------------------------------------------------------

    [Fact]
    public async Task InsertCitationOverSelectionInStructuralParagraph_UndoesInOneStep()
    {
        IReadOnlyList<string> edited = [];
        IReadOnlyList<string> undone = [];
        var canUndoAfterUndo = true;

        await Session.Dispatch(() =>
        {
            var view = BuildStructuralView("abcdef", withSource: true);

            view.SetSelectionRangePublic(0, 2, 0, 5);

            view.InsertCitation(Source1);

            edited = Paragraphs(view);
            view.Undo();
            undone = Paragraphs(view);
            canUndoAfterUndo = view.CanUndo;
        }, CancellationToken.None);

        edited.Should().NotEqual(["abcdef"], "the citation must have replaced the selected text");
        undone.Should().Equal(["abcdef"], "a single undo must restore the whole gesture, including the deleted selection");
        canUndoAfterUndo.Should().BeFalse("the delete+insert must be one undo entry, not two");
    }

    /// <summary>Sibling no-regression: inserting a citation with no active selection is unaffected by the
    /// new undo grouping.</summary>
    [Fact]
    public async Task InsertCitationAtCollapsedCaretInStructuralParagraph_StillInsertsAndUndoesCleanly()
    {
        IReadOnlyList<string> edited = [];
        IReadOnlyList<string> undone = [];

        await Session.Dispatch(() =>
        {
            var view = BuildStructuralView("abcdef", withSource: true);
            view.MoveCaretToBlock(0, 3);

            view.InsertCitation(Source1);

            edited = Paragraphs(view);
            view.Undo();
            undone = Paragraphs(view);
        }, CancellationToken.None);

        edited.Single().Should().StartWith("abc").And.EndWith("def");
        edited.Single().Length.Should().BeGreaterThan("abcdef".Length);
        undone.Should().Equal("abcdef");
    }

    // ---- Sweep addendum: InsertParagraphBreak's body fallback shares the same shape -------------------

    [Fact]
    public async Task InsertParagraphBreakOverSelectionInStructuralParagraph_UndoesInOneStep()
    {
        IReadOnlyList<string> edited = [];
        IReadOnlyList<string> undone = [];
        var canUndoAfterUndo = true;

        await Session.Dispatch(() =>
        {
            var view = BuildStructuralView("abcdef");
            view.SetSelectionRangePublic(0, 2, 0, 5);

            view.InsertParagraphBreakPublic();

            edited = Paragraphs(view);
            view.Undo();
            undone = Paragraphs(view);
            canUndoAfterUndo = view.CanUndo;
        }, CancellationToken.None);

        edited.Should().Equal("ab", "f");
        undone.Should().Equal(["abcdef"], "a single undo must restore the whole gesture, including the deleted selection");
        canUndoAfterUndo.Should().BeFalse("the delete+split must be one undo entry, not two");
    }

    // ---- Finding D: Ignore All proofing state must not leak across loaded documents -------------------

    [Fact]
    public async Task IgnoreAllProofingWord_DoesNotLeakAcrossLoadedDocuments()
    {
        var ignoredInDocA = false;
        IReadOnlyList<ProofingDiagnostic> diagnosticsAfterIgnoreInDocA = [];
        IReadOnlyList<ProofingDiagnostic> diagnosticsInDocB = [];

        await Session.Dispatch(() =>
        {
            var view = new DocumentView(new CustomDictionaryStore(null));

            var docA = TextDocument.CreateEmpty();
            docA.Blocks.Clear();
            docA.Blocks.Add(new Paragraph("teh example"));
            view.LoadDocument(docA);
            view.MoveCaretToBlock(0, 1);

            ignoredInDocA = view.IgnoreCurrentProofingWord();
            diagnosticsAfterIgnoreInDocA = view.ProofingDiagnosticsForTest;

            var docB = TextDocument.CreateEmpty();
            docB.Blocks.Clear();
            docB.Blocks.Add(new Paragraph("teh again"));
            view.LoadDocument(docB);

            diagnosticsInDocB = view.ProofingDiagnosticsForTest;
        }, CancellationToken.None);

        ignoredInDocA.Should().BeTrue();
        diagnosticsAfterIgnoreInDocA.Should().BeEmpty("ignoring 'teh' in document A must hide it in document A");
        diagnosticsInDocB.Should().ContainSingle(d => d.Word == "teh",
            "loading a different document must reset the ignore-all set — 'teh' was never ignored in document B");
    }

    /// <summary>Sibling no-regression: Ignore All still hides the word within the SAME document (the
    /// feature itself, as opposed to the leak, keeps working).</summary>
    [Fact]
    public async Task IgnoreAllProofingWord_StillHidesTheWordWithinTheSameDocument()
    {
        var ignored = false;
        IReadOnlyList<ProofingDiagnostic> before = [];
        IReadOnlyList<ProofingDiagnostic> after = [];

        await Session.Dispatch(() =>
        {
            var view = new DocumentView(new CustomDictionaryStore(null));
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("teh example"));
            view.LoadDocument(doc);
            view.MoveCaretToBlock(0, 1);

            before = view.ProofingDiagnosticsForTest;
            ignored = view.IgnoreCurrentProofingWord();
            after = view.ProofingDiagnosticsForTest;
        }, CancellationToken.None);

        before.Should().ContainSingle(d => d.Word == "teh");
        ignored.Should().BeTrue();
        after.Should().BeEmpty();
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private static readonly Source Source1 =
        new() { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" };

    /// <summary>
    /// Builds a single-paragraph document whose paragraph carries a preserved bookmark boundary, which
    /// forces the shared editing session's portable-span fast path
    /// (<c>DocumentEditingSession.TryResolveBodySpan</c> / <c>IsPortableBodyTextParagraph</c>) to reject it
    /// and fall through to DocumentView's renderer-owned fallback — the exact path each finding describes.
    /// </summary>
    private static DocumentView BuildStructuralView(string text, bool withSource = false)
    {
        var paragraph = new Paragraph(text);
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary(
            "structural-marker",
            BookmarkBoundaryKind.Start,
            RunIndex: paragraph.Runs.Count,
            Name: "StructuralMarker"));

        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        if (withSource)
        {
            document.BibliographyStyle = CitationStyle.Vancouver;
            document.Sources.Add(Source1);
        }

        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static IReadOnlyList<string> Paragraphs(DocumentView view) =>
        view.Document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText).ToList();
}
