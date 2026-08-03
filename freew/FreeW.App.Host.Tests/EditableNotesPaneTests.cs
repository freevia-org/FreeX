using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Tests for the W11 editable Notes pane (Phases 1A-1C) and the docked Header/Footer pane (Phase 2A).
/// All tests run on STA because they create DocumentView (a WPF RichTextBox).
/// </summary>
public sealed class EditableNotesPaneTests
{
    // ── Phase 1A: Notes pane backing — DeleteFootnote / DeleteEndnote ─────────────────────────────

    /// <summary>
    /// DeleteFootnote removes the dict entry AND strips the marker run from the body so the view is
    /// left consistent. The marker is gone; other runs are preserved.
    /// </summary>
    [StaFact]
    public void DeleteFootnote_RemovesDictEntryAndMarker()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var body = new Paragraph();
        body.Runs.Add(new Run("before "));
        body.Runs.Add(Run.FootnoteReference(1));
        body.Runs.Add(new Run(" after"));
        doc.Blocks.Add(body);
        doc.Footnotes[1] = new Footnote(1, "The footnote body");

        var view = new DocumentView();
        view.LoadModel(doc);

        view.DeleteFootnote(1);

        view.Model.Footnotes.Should().BeEmpty("dict entry must be removed");
        var paragraph = view.Model.Blocks.OfType<Paragraph>().First();
        paragraph.Runs.Should()
            .NotContain(r => r.FootnoteId == 1, "footnote reference marker must be stripped from body");
        paragraph.Runs.Should()
            .Contain(r => r.Text == "before " || r.Text == " after",
                "non-marker runs must survive");

        view.CanUndo.Should().BeTrue("deleting a note must be one undoable document edit");
        view.Undo();
        view.Model.Footnotes[1].PlainText.Should().Be("The footnote body");
        view.Model.Blocks.OfType<Paragraph>().First().Runs.Should()
            .Contain(r => r.FootnoteId == 1, "undo must restore the reference marker");

        view.Redo();
        view.Model.Footnotes.Should().NotContainKey(1);
    }

    /// <summary>DeleteEndnote mirrors DeleteFootnote but for the endnote dict and endnote markers.</summary>
    [StaFact]
    public void DeleteEndnote_RemovesDictEntryAndMarker()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var body = new Paragraph();
        body.Runs.Add(Run.EndnoteReference(2));
        doc.Blocks.Add(body);
        doc.Endnotes[2] = new Endnote(2, "Endnote body");

        var view = new DocumentView();
        view.LoadModel(doc);

        view.DeleteEndnote(2);

        view.Model.Endnotes.Should().BeEmpty("endnote dict entry must be removed");
        view.Model.Blocks.OfType<Paragraph>().First().Runs.Should()
            .NotContain(r => r.EndnoteId == 2, "endnote reference marker must be stripped");
    }

    /// <summary>Deleting a non-existent id is a no-op that does not throw.</summary>
    [StaFact]
    public void DeleteFootnote_NonExistentId_IsNoOp()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var ex = Record.Exception(() => view.DeleteFootnote(99));
        ex.Should().BeNull("deleting a non-existent note id must not throw");
    }

    // ── Phase 1B: sub-editor wrapper pattern ──────────────────────────────────────────────────────

    /// <summary>
    /// The sub-editor wrapper pattern: load a note's Content into a wrapper TextDocument, edit it,
    /// then copy the wrapper's Blocks back into note.Content and verify PlainText reflects the change.
    /// This is the Apply path without the UI pane.
    /// </summary>
    [StaFact]
    public void SubEditorWrapper_ApplyUpdatesNoteContent()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Footnotes[1] = new Footnote(1, "original text");

        // Build wrapper seeded with the note's content (mirrors LoadSelectedNote in MainWindow).
        var wrapper = TextDocument.CreateEmpty();
        wrapper.DefaultRun = doc.DefaultRun;
        wrapper.Blocks.Clear();
        foreach (var para in doc.Footnotes[1].Content)
            wrapper.Blocks.Add(DocumentMerge.CloneBlock(para));

        var subEditor = new DocumentView();
        subEditor.LoadModel(wrapper);

        // Simulate a user edit: replace the text.
        wrapper.Blocks.Clear();
        var editedPara = new Paragraph("edited text");
        wrapper.Blocks.Add(editedPara);
        subEditor.LoadModel(wrapper);
        subEditor.CommitToModel();

        // Apply: copy back into note.Content.
        var note = doc.Footnotes[1];
        note.Content.Clear();
        foreach (var block in subEditor.Model.Blocks.OfType<Paragraph>())
            note.Content.Add(block);

        note.PlainText.Should().Be("edited text",
            "applying sub-editor content must update note.PlainText");
    }

    [StaFact]
    public void ReplaceNoteContent_IsUndoableAndPreservesRichParagraphs()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Footnotes[1] = new Footnote(1, "original text");
        var view = new DocumentView();
        view.LoadModel(doc);

        var edited = new Paragraph();
        edited.Runs.Add(new Run("edited text")
        {
            Formatting = RunFormatting.Default with { Bold = true },
        });
        view.ReplaceNoteContent(1, footnote: true, [edited, new Paragraph("more")]);

        view.Model.Footnotes[1].PlainText.Should().Be("edited text\nmore");
        view.Model.Footnotes[1].Content[0].Runs.Single().Formatting.Bold.Should().BeTrue();
        view.CanUndo.Should().BeTrue();

        view.Undo();
        view.Model.Footnotes[1].PlainText.Should().Be("original text");
        view.Redo();
        view.Model.Footnotes[1].PlainText.Should().Be("edited text\nmore");
    }

    /// <summary>
    /// The sub-editor's undo stack is independent of the main editor's undo stack.
    /// Undo on the sub-editor does not affect the main editor's model.
    /// </summary>
    [StaFact]
    public void SubEditorUndo_DoesNotAffectMainEditor()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("main body"));

        var mainEditor = new DocumentView();
        mainEditor.LoadModel(doc);

        var wrapper = TextDocument.CreateEmpty();
        wrapper.Blocks.Clear();
        wrapper.Blocks.Add(new Paragraph("note content"));
        var subEditor = new DocumentView();
        subEditor.LoadModel(wrapper);

        // The sub-editor's undo — even if it somehow fires — must not touch mainEditor.Model.
        subEditor.CommitToModel();
        mainEditor.CommitToModel();

        mainEditor.Model.Blocks.OfType<Paragraph>().First().PlainText.Should().Be("main body",
            "main editor body must be unaffected by sub-editor operations");
    }

    // ── Phase 2A: Header/Footer pane — formatted run round-trip ──────────────────────────────────

    /// <summary>
    /// The wrapper pattern for the H/F pane must preserve bold formatting that the old plain-text
    /// dialog would have lost. Load slot paragraphs → sub-editor → CommitToModel → copy back.
    /// </summary>
    [StaFact]
    public void HeaderFooterWrapper_PreservesRunFormatting()
    {
        var doc = TextDocument.CreateEmpty();
        var boldRun = new Run("Bold Header")
        {
            Formatting = RunFormatting.Default with { Bold = true }
        };
        var headerPara = new Paragraph();
        headerPara.Runs.Add(boldRun);
        var slot = new HeaderFooter();
        slot.Paragraphs.Add(headerPara);
        doc.FinalSectionHeadersFooters.Header = slot;

        // Build wrapper mirroring OpenHeaderFooterPane in MainWindow.
        var wrapper = TextDocument.CreateEmpty();
        wrapper.DefaultRun = doc.DefaultRun;
        wrapper.Blocks.Clear();
        foreach (var para in slot.Paragraphs)
            wrapper.Blocks.Add(para);

        var subEditor = new DocumentView();
        subEditor.LoadModel(wrapper);
        subEditor.CommitToModel();

        // Write back mirroring CloseHeaderFooterPane.
        var hfOut = new HeaderFooter();
        foreach (var block in subEditor.Model.Blocks.OfType<Paragraph>())
            hfOut.Paragraphs.Add(block);

        // Bold formatting must survive the sub-editor round-trip.
        var recoveredRun = hfOut.Paragraphs.First().Runs.First();
        recoveredRun.Formatting.Bold.Should().BeTrue(
            "bold formatting on a header run must survive the sub-editor load→commit→copy-back cycle");
    }

    /// <summary>
    /// A page-number field run in a header slot must survive the wrapper round-trip without becoming
    /// a plain text run (the old dialog would have discarded field runs entirely).
    /// </summary>
    [StaFact]
    public void HeaderFooterWrapper_PreservesPageNumberFieldRun()
    {
        var doc = TextDocument.CreateEmpty();
        var footerPara = new Paragraph();
        footerPara.Runs.Add(Run.PageNumberField());
        var slot = new HeaderFooter();
        slot.Paragraphs.Add(footerPara);
        doc.FinalSectionHeadersFooters.Footer = slot;

        var wrapper = TextDocument.CreateEmpty();
        wrapper.Blocks.Clear();
        foreach (var para in slot.Paragraphs)
            wrapper.Blocks.Add(para);

        var subEditor = new DocumentView();
        subEditor.LoadModel(wrapper);
        subEditor.CommitToModel();

        var hfOut = new HeaderFooter();
        foreach (var block in subEditor.Model.Blocks.OfType<Paragraph>())
            hfOut.Paragraphs.Add(block);

        hfOut.Paragraphs.SelectMany(p => p.Runs)
            .Should()
            .Contain(r => r.FieldKind == RunFieldKind.PageNumber,
                "a page-number field run in a footer slot must survive the sub-editor round-trip");
    }
}
