using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// R143 (shared-undo-boundaries, id freew-avalonia-undogroup-abort-not-rollback): Insert Field / Insert
/// Content Control opens an undo group, deletes the active selection through the bus (applying it
/// immediately), and only THEN checks whether the caret's paragraph is actually field/content-control
/// insertable. When that check fails -- e.g. because some OTHER run elsewhere in the same paragraph carries
/// a footnote/endnote/image/equation/content-control marker, which <see cref="DocumentView"/>'s
/// per-paragraph <c>IsFieldInsertable</c>/<c>IsEditable</c> checks reject wholesale -- the method used to
/// call <c>DocumentCommandBus.AbortUndoGroup()</c>, which (per its own doc comment) discards the group
/// WITHOUT reverting any command already applied. The already-applied selection delete therefore survived
/// permanently with no undo-stack entry to remove it. The fix rolls the group back
/// (<c>RollbackUndoGroup()</c>) instead of abandoning it, so a failed insert leaves the document exactly as
/// it was.
/// </summary>
public sealed class DocumentViewInsertFieldUndoRollbackTests
{
    private static DocumentView ViewWithFootnoteGuardedParagraph(string text, int footnoteAtOffset = -1)
    {
        var paragraph = new Paragraph(text);
        // The footnote reference run is appended after the body text, so it never overlaps the selection
        // below -- it is present in the paragraph but has nothing to do with the edit being attempted.
        paragraph.Runs.Add(Run.FootnoteReference(1));

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);
        document.Footnotes[1] = new Footnote(1, "a note");

        var view = new DocumentView();
        view.LoadDocument(document);
        return view;
    }

    [Fact]
    public void InsertField_OverSelection_WhenParagraphHasUnrelatedFootnote_RollsBackTheAlreadyAppliedDelete()
    {
        var view = ViewWithFootnoteGuardedParagraph("abcdef");
        // Select "bc" (offsets 1..3) -- nowhere near the trailing footnote-reference run.
        view.SetSelectionRangePublic(0, 1, 0, 3);
        var canUndoBefore = view.CanUndo;

        view.InsertField(RunFieldKind.Date);

        var paragraph = (Paragraph)view.Document.Blocks[0];
        paragraph.PlainText.Should().Be(
            "abcdef1",
            "the paragraph is not field-insertable (the footnote reference run elsewhere in it blocks the " +
            "whole paragraph), so the selection delete that InsertField already applied before discovering " +
            "that must be rolled back -- not left in place with no way to undo it");
        paragraph.Runs.Should().NotContain(run => run.FieldKind == RunFieldKind.Date,
            "the field itself must not have been inserted either");
        view.CanUndo.Should().Be(canUndoBefore,
            "a fully rolled-back gesture must not leave a stray undo-stack entry behind");
    }

    [Fact]
    public void InsertCheckBoxControl_OverSelection_WhenParagraphHasUnrelatedFootnote_RollsBackTheAlreadyAppliedDelete()
    {
        var view = ViewWithFootnoteGuardedParagraph("abcdef");
        view.SetSelectionRangePublic(0, 1, 0, 3);
        var canUndoBefore = view.CanUndo;

        view.InsertCheckBoxControl();

        var paragraph = (Paragraph)view.Document.Blocks[0];
        paragraph.PlainText.Should().Be(
            "abcdef1",
            "the same abort-not-rollback defect exists in InsertBodyContentControlRun, which every " +
            "Developer-tab content-control command (including the check box) routes through");
        view.CanUndo.Should().Be(canUndoBefore);
    }

    /// <summary>Sibling no-regression: when the paragraph genuinely IS field-insertable (no footnote/image/
    /// equation/content-control anywhere in it), a selection replace-with-field still works and still
    /// undoes cleanly in one step -- the rollback fix must not affect the success path.</summary>
    [Fact]
    public void InsertField_OverSelection_WhenParagraphIsPlain_StillInsertsAndUndoesInOneStep()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("abcdef"));

        var view = new DocumentView();
        view.LoadDocument(document);
        view.SetSelectionRangePublic(0, 1, 0, 3);

        view.InsertField(RunFieldKind.Date);

        var paragraph = (Paragraph)view.Document.Blocks[0];
        paragraph.Runs.Should().Contain(run => run.FieldKind == RunFieldKind.Date,
            "the field must be inserted when the paragraph has nothing blocking it");
        paragraph.PlainText.Should().NotContain("bc", "the selected text must have been replaced");

        view.CanUndo.Should().BeTrue();
        view.Undo();
        paragraph.PlainText.Should().Be("abcdef", "a single undo must restore the whole gesture");
    }
}
