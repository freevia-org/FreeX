using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round 150 fix wave, three findings in <c>freew/FreeW.App.Avalonia/Editing/DocumentView.cs</c>:
///
/// (find-replace F2) <see cref="DocumentView.ReplaceAll(string, string, FindReplaceSearchOptions)"/> (and
/// its 2-arg sibling) used to ignore any active selection and always rewrite the whole document, unlike
/// Word and unlike the WPF shell's <c>WpfFindReplaceCommandHost.ReplaceAll</c> (which restricts to the
/// selection when one is active). Fixed by restricting the search/replace loop to the prior selection's
/// span when a non-cell body-text selection was active at call time.
///
/// (undo-coalescing F1 / F2) <c>InsertParagraphBreak</c>'s body-text fallback and <c>InsertCitation</c>
/// both open an undo group, call <c>DeleteSelection()</c> into it (which applies immediately), and used to
/// call <c>_bus.AbortUndoGroup()</c> -- which discards the group WITHOUT reverting already-applied
/// commands -- when the resulting paragraph turns out not to be editable (e.g. it still carries an
/// image/field/footnote run elsewhere). That silently and permanently deleted the user's selected text
/// with no undo entry. Fixed by using <c>_bus.RollbackUndoGroup()</c> instead, matching the sibling
/// <c>InsertFieldRunAtActiveCaret</c> guard a few hundred lines below each.
/// </summary>
public sealed class R150_ReplaceAllSelectionScopeAndUndoRollbackTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task OnUiThread(System.Action action) => Session.Dispatch(action, CancellationToken.None);

    // ==== find-replace F2: ReplaceAll must restrict to an active selection =========================

    private static DocumentView BuildThreeParagraphView(string p0, string p1, string p2)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(p0));
        document.Blocks.Add(new Paragraph(p1));
        document.Blocks.Add(new Paragraph(p2));

        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static string[] Paragraphs(DocumentView view) =>
        view.Document.Blocks.Cast<Paragraph>().Select(p => p.PlainText).ToArray();

    [Fact]
    public async Task ReplaceAll_WithActiveSelection_OnlyReplacesInsideTheSelectedParagraph()
    {
        var count = -1;
        string[] result = [];

        await OnUiThread(() =>
        {
            var view = BuildThreeParagraphView("total apples", "The grand total is here", "total oranges");

            // Select the whole of paragraph 1 (the one containing "total" once) before opening Replace All --
            // mirrors selecting a paragraph, then Ctrl+H, Replace All.
            view.SetBodySelectionForTest(1, 0, 1, "The grand total is here".Length);

            count = view.ReplaceAll(
                "total",
                "sum",
                new FindReplaceSearchOptions(MatchCase: false, WholeWord: false, UseWildcards: false));

            result = Paragraphs(view);
        });

        count.Should().Be(1, "only the one occurrence inside the selected paragraph must be replaced");
        result.Should().Equal(
            "total apples",
            "The grand sum is here",
            "total oranges");
    }

    [Fact]
    public async Task ReplaceAll_WithActiveSelection_2ArgOverload_AlsoRestrictsToSelection()
    {
        var count = -1;
        string[] result = [];

        await OnUiThread(() =>
        {
            var view = BuildThreeParagraphView("total apples", "The grand total is here", "total oranges");
            view.SetBodySelectionForTest(1, 0, 1, "The grand total is here".Length);

            count = view.ReplaceAll("total", "sum");

            result = Paragraphs(view);
        });

        count.Should().Be(1);
        result.Should().Equal(
            "total apples",
            "The grand sum is here",
            "total oranges");
    }

    [Fact]
    public async Task ReplaceAll_WithActiveSelection_ReplacesEveryOccurrenceInsideTheSelectionAndStops()
    {
        // Exercises the running-selection-end adjustment: three matches inside one selected paragraph must
        // all be replaced (proving the shrinking selection boundary is tracked correctly as "total" (5
        // chars) becomes "sum" (3 chars) three times), while the neighbour paragraphs stay untouched.
        var count = -1;
        string[] result = [];

        await OnUiThread(() =>
        {
            var view = BuildThreeParagraphView("total", "total total total", "total");
            view.SetBodySelectionForTest(1, 0, 1, "total total total".Length);

            count = view.ReplaceAll(
                "total",
                "sum",
                new FindReplaceSearchOptions(MatchCase: false, WholeWord: false, UseWildcards: false));

            result = Paragraphs(view);
        });

        count.Should().Be(3);
        result.Should().Equal("total", "sum sum sum", "total");
    }

    /// <summary>Sibling no-regression: with no active selection (collapsed caret), Replace All still
    /// rewrites every occurrence in the whole document, exactly as before this fix.</summary>
    [Fact]
    public async Task ReplaceAll_WithNoActiveSelection_StillReplacesTheWholeDocument()
    {
        var count = -1;
        string[] result = [];

        await OnUiThread(() =>
        {
            var view = BuildThreeParagraphView("total apples", "The grand total is here", "total oranges");
            // No selection set up -- caret sits at the document start, collapsed.

            count = view.ReplaceAll(
                "total",
                "sum",
                new FindReplaceSearchOptions(MatchCase: false, WholeWord: false, UseWildcards: false));

            result = Paragraphs(view);
        });

        count.Should().Be(3, "with no selection active, Replace All must still cover the whole document");
        result.Should().Equal(
            "sum apples",
            "The grand sum is here",
            "sum oranges");
    }

    /// <summary>Adjacent case: a table-CELL text selection is deliberately NOT honored as a scope restriction
    /// (this editor has no scoped Replace All for table cells), so it must keep the historical
    /// whole-document behavior rather than silently replacing nothing.</summary>
    [Fact]
    public async Task ReplaceAll_WithActiveCellTextSelection_StillReplacesTheWholeDocument()
    {
        var count = -1;
        string? cellText = null;
        string? bodyText = null;

        await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph("total in body"));
            var table = new Table();
            var row = new TableRow();
            row.Cells.Add(new TableCell("total in cell"));
            table.Rows.Add(row);
            document.Blocks.Add(table);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(800, 2000));

            var tableBlock = document.Blocks.IndexOf(table);
            // A real (non-collapsed) in-cell text selection covering the whole cell paragraph.
            view.SetCellTextSelectionForTest(tableBlock, 0, 0, 0, 0, 0, 0, 0, "total in cell".Length);

            count = view.ReplaceAll(
                "total",
                "sum",
                new FindReplaceSearchOptions(MatchCase: false, WholeWord: false, UseWildcards: false));

            bodyText = ((Paragraph)view.Document.Blocks[0]).PlainText;
            cellText = ((Table)view.Document.Blocks[1]).Rows[0].Cells[0].Paragraphs[0].PlainText;
        });

        count.Should().Be(2, "a cell-text selection is not honored as a Replace-All scope, so both occurrences replace as before");
        bodyText.Should().Be("sum in body");
        cellText.Should().Be("sum in cell");
    }

    // ==== undo-coalescing F1: InsertParagraphBreak must not lose a deletion it cannot complete ======

    // A footnote-reference run (not an image) is used to make the paragraph non-editable "elsewhere":
    // unlike an image, its single reference character round-trips through ParaCells/SetRuns (each Cell
    // carries the run's FootnoteId), so it reliably SURVIVES DeleteSelection()'s cell-rewrite and the
    // post-delete IsEditable(paragraph) check still (correctly) sees it and fails, exactly like the
    // finding's own example ("an inline image, footnote/endnote reference, or field elsewhere").
    private static DocumentView BuildParagraphWithFootnoteElsewhere(string beforeText, string afterText)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(beforeText));
        paragraph.Runs.Add(Run.FootnoteReference(1));
        paragraph.Runs.Add(new Run(afterText));
        // A bookmark boundary forces the shared editing session's portable-span fast path
        // (DocumentEditingSession.TryResolveBodySpan / IsPortableBodyTextParagraph) to reject this
        // paragraph and fall through to DocumentView's renderer-owned fallback -- the exact branch
        // the finding describes -- matching DocumentViewStructuralInsertUndoGroupingTests's own setup.
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary(
            "structural-marker", BookmarkBoundaryKind.Start, RunIndex: paragraph.Runs.Count, Name: "Marker"));

        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        document.Footnotes[1] = new Footnote(1, "A note.");

        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }

    [Fact]
    public async Task InsertParagraphBreak_OverSelectionInParagraphWithFootnoteElsewhere_DoesNotLoseTheSelectedText()
    {
        string before = "";
        string after = "";
        var canUndo = true;

        await OnUiThread(() =>
        {
            var view = BuildParagraphWithFootnoteElsewhere("See ", " for details.");
            before = ((Paragraph)view.Document.Blocks[0]).PlainText;

            // Select "See " (offsets 0..4), entirely inside the plain-text run before the footnote marker
            // -- the paragraph as a WHOLE is still not editable (it carries a footnote reference elsewhere),
            // so Enter must refuse the split, but it must not have destroyed the selected text on the way
            // there.
            view.SetBodySelectionForTest(0, 0, 0, 4);

            view.InsertParagraphBreakPublic();

            after = ((Paragraph)view.Document.Blocks[0]).PlainText;
            canUndo = view.CanUndo;
        });

        after.Should().Be(before, "the guard refuses the split because of the footnote reference elsewhere in " +
            "the paragraph, so the already-applied DeleteSelection() must be rolled back rather than left in place");
        canUndo.Should().BeFalse("a refused/aborted gesture must not leave a phantom undo entry either");
    }

    /// <summary>Sibling no-regression: the same structural (bookmark-forced-fallback) paragraph, with NO
    /// image and an ordinary selection, must still split normally and undo in one step.</summary>
    [Fact]
    public async Task InsertParagraphBreak_OverSelectionInStructuralParagraphWithoutImage_StillSplitsAndUndoesCleanly()
    {
        string[] edited = [];
        string[] undone = [];
        var canUndoAfterUndo = true;

        await OnUiThread(() =>
        {
            var paragraph = new Paragraph("abcdef");
            paragraph.BookmarkBoundaries.Add(new BookmarkBoundary(
                "structural-marker", BookmarkBoundaryKind.Start, RunIndex: paragraph.Runs.Count, Name: "Marker"));
            var document = new TextDocument();
            document.Blocks.Add(paragraph);
            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(800, 2000));

            view.SetBodySelectionForTest(0, 2, 0, 5);

            view.InsertParagraphBreakPublic();

            edited = Paragraphs(view);
            view.Undo();
            undone = Paragraphs(view);
            canUndoAfterUndo = view.CanUndo;
        });

        edited.Should().Equal("ab", "f");
        undone.Should().Equal("abcdef");
        canUndoAfterUndo.Should().BeFalse("the delete+split must be one undo entry, not two");
    }

    // ==== undo-coalescing F2: InsertCitation must not lose a deletion it cannot complete ============

    private static readonly Source CitationSource =
        new() { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" };

    // See the F1 comment above on why a footnote reference (not an image) is used to make the paragraph
    // non-editable "elsewhere": it reliably survives the ParaCells/SetRuns round-trip DeleteSelection()
    // performs, so the post-delete IsEditable(paragraph) check still (correctly) sees it and fails.
    private static DocumentView BuildCitationViewWithFootnoteElsewhere(string beforeText, string afterText)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(beforeText));
        paragraph.Runs.Add(Run.FootnoteReference(1));
        paragraph.Runs.Add(new Run(afterText));
        // Force DocumentView's renderer-owned fallback (see BuildParagraphWithFootnoteElsewhere above).
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary(
            "structural-marker", BookmarkBoundaryKind.Start, RunIndex: paragraph.Runs.Count, Name: "Marker"));

        var document = new TextDocument { BibliographyStyle = CitationStyle.Vancouver };
        document.Blocks.Add(paragraph);
        document.Sources.Add(CitationSource);
        document.Footnotes[1] = new Footnote(1, "A note.");

        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }

    [Fact]
    public async Task InsertCitation_OverSelectionInParagraphWithFootnoteElsewhere_DoesNotLoseTheSelectedText()
    {
        string before = "";
        string after = "";
        var canUndo = true;

        await OnUiThread(() =>
        {
            var view = BuildCitationViewWithFootnoteElsewhere("See ", " for details.");
            before = ((Paragraph)view.Document.Blocks[0]).PlainText;

            view.SetBodySelectionForTest(0, 0, 0, 4);

            view.InsertCitation(CitationSource);

            after = ((Paragraph)view.Document.Blocks[0]).PlainText;
            canUndo = view.CanUndo;
        });

        after.Should().Be(before, "the guard refuses the citation insert because of the footnote reference " +
            "elsewhere in the paragraph, so the already-applied DeleteSelection() must be rolled back rather " +
            "than left in place");
        canUndo.Should().BeFalse("a refused/aborted gesture must not leave a phantom undo entry either");
    }

    /// <summary>Sibling no-regression: inserting a citation over a selection in an ordinary (no footnote)
    /// paragraph still replaces the selection and undoes in one step. (Deliberately asserts on CONTENT, not
    /// length -- a Vancouver numeric citation like "[1]" happens to be exactly as long as the 3-character
    /// selection it replaces, so a length-only assertion would not catch a regression here.)</summary>
    [Fact]
    public async Task InsertCitation_OverSelectionWithoutImage_StillInsertsAndUndoesCleanly()
    {
        string[] edited = [];
        string[] undone = [];
        var canUndoAfterUndo = true;

        await OnUiThread(() =>
        {
            var document = new TextDocument { BibliographyStyle = CitationStyle.Vancouver };
            document.Blocks.Add(new Paragraph("abcdef"));
            document.Sources.Add(CitationSource);
            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(800, 2000));

            view.SetBodySelectionForTest(0, 2, 0, 5);

            view.InsertCitation(CitationSource);

            edited = Paragraphs(view);
            view.Undo();
            undone = Paragraphs(view);
            canUndoAfterUndo = view.CanUndo;
        });

        edited.Single().Should().NotBe("abcdef", "the citation must have replaced the selected \"cde\"");
        edited.Single().Should().StartWith("ab").And.EndWith("f").And.NotContain("cde");
        undone.Should().Equal("abcdef");
        canUndoAfterUndo.Should().BeFalse("the delete+insert must be one undo entry, not two");
    }
}
