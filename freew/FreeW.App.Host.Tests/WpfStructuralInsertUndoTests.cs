using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round-136 remediation coverage for the WPF host's ungrouped delete+insert undo bug: InsertText's
/// structural fallback and the content-control inserts mutate the live FlowDocument directly and then
/// call <see cref="DocumentView.CommitToModel"/> + Render (via the command bus), which rebuilds a
/// brand-new FlowDocument and reassigns <c>Document</c> — proven (empirically, in a throwaway WPF probe
/// outside this repo) to discard WPF's native RichTextBox undo stack outright, regardless of whether the
/// edit was one WPF operation or a clear-then-insert pair. Every test here exercises undo at the level
/// that actually matters: perform the gesture, call <see cref="DocumentView.Undo"/> — the shell's own
/// entry point, not the command bus directly — exactly ONCE, and assert the document text is back to
/// its pre-gesture state.
/// </summary>
public sealed class WpfStructuralInsertUndoTests
{
    private static string PlainText(DocumentView view) =>
        new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text.TrimEnd('\r', '\n');

    /// <summary>
    /// Drives InsertText's structural fallback specifically: a caret inside a table cell paragraph can't
    /// be resolved to a model index by the portable fast path (DocumentView.NumberLeafBlocks only
    /// registers TOP-LEVEL paragraphs, never descending into table cells), so TryApplyBodyTextInput
    /// returns false and InsertText falls through to the raw selection-clear/InsertTextInRun path.
    /// </summary>
    [StaFact]
    public void InsertText_StructuralFallbackInTableCell_OneUndo_RestoresPreGestureText()
    {
        var view = new DocumentView();
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs.Clear();
        table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("Cell"));
        document.Blocks.Add(table);
        view.LoadModel(document);

        var cellParagraph = view.Document.Blocks
            .OfType<System.Windows.Documents.Table>().Single()
            .RowGroups.Single().Rows.Single().Cells.Single().Blocks
            .OfType<System.Windows.Documents.Paragraph>().Single();
        view.CaretPosition = cellParagraph.ContentEnd;

        view.InsertText("X");

        Assert.Contains("CellX", PlainText(view));

        Assert.True(view.CanUndo);
        view.Undo();

        Assert.Equal("Cell", PlainText(view));
        Assert.False(view.CanUndo, "the fallback insert must be one undo entry, not two");
    }

    /// <summary>
    /// The exact shape the auditor flagged: a non-empty selection cleared, then a content-control run
    /// inserted at the resulting caret — two separate live-document mutations that used to be two
    /// separate (and, post-Render, entirely undo-unrecoverable) WPF edits.
    /// </summary>
    [StaFact]
    public void InsertPlainTextControl_OverSelection_OneUndo_RestoresPreGestureText()
    {
        var view = new DocumentView();
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Hello world"));
        view.LoadModel(document);

        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        var selectionStart = paragraph.ContentStart.GetPositionAtOffset(6)!; // just before "world"
        view.Selection.Select(selectionStart, paragraph.ContentEnd);

        view.InsertPlainTextControl();
        view.CommitToModel();

        // The selected text ("world") becomes the new control's content, so the plain text is unchanged —
        // what must change is that it is now wrapped in a content control.
        Assert.Equal("Hello world", PlainText(view));
        Assert.Contains(view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs), r => r.Control is not null);

        Assert.True(view.CanUndo);
        view.Undo();

        Assert.Equal("Hello world", PlainText(view));
        Assert.False(view.CanUndo, "the clear-selection + insert-control must be one undo entry, not two");
        view.CommitToModel();
        Assert.DoesNotContain(view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs), r => r.Control is not null);
    }

    [StaFact]
    public void InsertRichTextControl_OverSelection_OneUndo_RestoresPreGestureText()
    {
        var view = new DocumentView();
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Hello world"));
        view.LoadModel(document);

        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        var selectionStart = paragraph.ContentStart.GetPositionAtOffset(6)!;
        view.Selection.Select(selectionStart, paragraph.ContentEnd);

        view.InsertRichTextControl();
        view.CommitToModel();

        Assert.Equal("Hello world", PlainText(view));
        Assert.Contains(view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs), r => r.Control is not null);

        Assert.True(view.CanUndo);
        view.Undo();

        Assert.Equal("Hello world", PlainText(view));
        Assert.False(view.CanUndo, "the clear-selection + insert-control must be one undo entry, not two");
        view.CommitToModel();
        Assert.DoesNotContain(view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs), r => r.Control is not null);
    }

    /// <summary>
    /// No selection to clear — just ONE WPF structural mutation (InsertInlineAtCaret) followed by
    /// Render(). This still needed to be undoable through the command bus: before the fix, base
    /// RichTextBox undo was wiped by Render()'s Document reassignment (proven empirically) and nothing
    /// was pushed to the DocumentCommandBus, so Undo() was a complete no-op — worse than needing two
    /// clicks.
    /// </summary>
    [StaFact]
    public void InsertCheckBoxControl_OneUndo_RestoresPreGestureTextAndRemovesControl()
    {
        var view = new DocumentView();
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Hello"));
        view.LoadModel(document);

        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.CaretPosition = paragraph.ContentEnd;

        view.InsertCheckBoxControl();
        view.CommitToModel();
        Assert.Contains(view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs), r => r.Control is not null);

        Assert.True(view.CanUndo);
        view.Undo();

        Assert.Equal("Hello", PlainText(view));
        Assert.False(view.CanUndo, "the checkbox insert must be one undo entry");
        view.CommitToModel();
        Assert.DoesNotContain(view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs), r => r.Control is not null);
    }

    [StaFact]
    public void InsertDatePickerControl_OneUndo_RestoresPreGestureText()
    {
        var view = new DocumentView();
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Hello"));
        view.LoadModel(document);

        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.CaretPosition = paragraph.ContentEnd;

        view.InsertDatePickerControl();

        Assert.True(view.CanUndo);
        view.Undo();

        Assert.Equal("Hello", PlainText(view));
    }
}
