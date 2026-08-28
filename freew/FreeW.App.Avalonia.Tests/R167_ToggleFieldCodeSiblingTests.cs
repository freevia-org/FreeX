using System.Linq;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round 167 (meta F3): <c>ToggleFieldCodeAtCaret</c> (Shift+F9) is the third member of the same family as
/// round 165's <c>SetFieldLockAtCaret</c> fix and round 166's <c>UnlinkFieldAtCaret</c> fix --
/// <c>SelectedOrCurrentComplexFields</c> only ever returns a run with <c>ComplexField: not null</c>, so a
/// simple field (Insert &gt; Header &amp; Footer &gt; Page Number, Insert &gt; Quick Parts &gt; Date/etc.)
/// falls through untouched under Shift+F9. Unlike Lock and Unlink, this one does NOT get a
/// simple-field fallback that mutates the run: a <see cref="RunFieldKind"/> run carries no wrapper object
/// and no ShowCode-equivalent flag to hold a code/result display mode, so there is nothing to toggle
/// without adding a new, presentation-only model flag purely to make the command apply -- which
/// <see cref="FreeW.App.Presentation.Editing.DocumentReferenceEditingCoordinator.ToggleFieldCodes"/>
/// (Alt+F9, the document-wide sibling) already declines to do, for the identical reason, for the identical
/// run shape. The decision here matches that: Shift+F9 deliberately stays a no-op on a simple field. This
/// test locks that decision down explicitly (previously it held only by accident, as a side effect of
/// <c>SelectedOrCurrentComplexFields</c>'s type filter, with nothing naming or guarding the outcome) and
/// pins it as the same outcome the WPF host must produce for the same input.
/// </summary>
public sealed class R167_ToggleFieldCodeSiblingTests
{
    [Fact]
    public void ToggleFieldCodeAtCaret_OnASimpleRunFieldKindField_RemainsANoOp()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph());
        var view = new DocumentView();
        view.LoadDocument(document);

        // Mirrors Insert > Quick Parts > Date (FreeWAvaloniaRibbonCommands wires the Date ribbon button to
        // exactly this call) -- a RunFieldKind field with no ComplexField wrapper, the exact shape
        // SelectedOrCurrentComplexFields cannot see.
        view.InsertField(RunFieldKind.Date);
        var run = ((Paragraph)view.Document.Blocks[0]).Runs.Single();
        run.FieldKind.Should().Be(RunFieldKind.Date);
        var textBefore = run.Text;
        var lockedBefore = run.FieldLocked;

        view.MoveCaretToBlockForTest(0, 1);
        var canUndoBeforeToggle = view.CanUndo;

        view.ToggleFieldCodeAtCaret();

        var runAfter = ((Paragraph)view.Document.Blocks[0]).Runs.Single();
        runAfter.FieldKind.Should().Be(
            RunFieldKind.Date,
            "Shift+F9 has no code/result display mode to toggle for a RunFieldKind field -- it must leave " +
            "the field exactly as it was, not silently corrupt or unlink it");
        runAfter.Text.Should().Be(textBefore);
        runAfter.FieldLocked.Should().Be(lockedBefore);
        view.CanUndo.Should().Be(
            canUndoBeforeToggle,
            "a deliberate no-op must not push an undo entry out of nothing");
    }

    /// <summary>Sibling no-regression: toggling a ComplexField at the caret still works exactly as before
    /// (the branch this fix's explicit simple-field recognition must not disturb).</summary>
    [Fact]
    public void ToggleFieldCodeAtCaret_StillTogglesAComplexFieldAtTheCaret()
    {
        var field = Run.ComplexFieldRun(" DATE ", "cached date text");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph { Runs = { field } });
        var view = new DocumentView();
        view.LoadDocument(document);
        var showCodeBefore = field.ComplexField!.ShowCode;

        view.MoveCaretToBlockForTest(0, 1);
        view.ToggleFieldCodeAtCaret();

        var run = ((Paragraph)view.Document.Blocks[0]).Runs.Single();
        run.ComplexField!.ShowCode.Should().Be(
            !showCodeBefore,
            "Shift+F9 must still flip ShowCode for a ComplexField at the caret");
    }

    /// <summary>Sibling no-regression: a caret sitting on ordinary text (no field of either kind under it)
    /// keeps the original silent no-op -- there is nothing to toggle.</summary>
    [Fact]
    public void ToggleFieldCodeAtCaret_OnPlainTextCaret_RemainsANoOp()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Just plain text"));
        var view = new DocumentView();
        view.LoadDocument(document);

        view.MoveCaretToBlockForTest(0, 3);

        view.ToggleFieldCodeAtCaret();

        var run = ((Paragraph)view.Document.Blocks[0]).Runs.Single();
        run.Text.Should().Be("Just plain text");
        view.CanUndo.Should().BeFalse();
    }

    /// <summary>Sibling no-regression: a simple field sitting right next to an unrelated ComplexField in
    /// the same paragraph must not cause Shift+F9 on the simple field to mistarget the neighbor.</summary>
    [Fact]
    public void ToggleFieldCodeAtCaret_OnASimpleFieldNextToAComplexField_LeavesTheComplexFieldUntouched()
    {
        var complex = Run.ComplexFieldRun(" DATE ", "cached date text");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(complex);
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);

        view.MoveCaretToBlockForTest(0, complex.Text.Length);
        view.InsertField(RunFieldKind.Date);

        var runs = ((Paragraph)view.Document.Blocks[0]).Runs;
        var complexRun = runs.Single(r => r.ComplexField is not null);
        var showCodeBefore = complexRun.ComplexField!.ShowCode;

        view.MoveCaretToBlockForTest(0, complex.Text.Length + 1);
        view.ToggleFieldCodeAtCaret();

        var complexRunAfter = runs.Single(r => r.ComplexField is not null);
        complexRunAfter.ComplexField!.ShowCode.Should().Be(showCodeBefore);
    }
}
