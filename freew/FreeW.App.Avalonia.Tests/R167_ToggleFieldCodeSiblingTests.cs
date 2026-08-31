using System.Linq;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round 167 correction (meta F1): the original round-167 wave wrongly recorded Shift+F9 as a deliberate
/// no-op for a simple <see cref="RunFieldKind"/> field, on the reasoning that a <see cref="RunFieldKind"/>
/// run carries no wrapper object and no ShowCode-equivalent flag to hold a code/result display mode. That
/// is wrong: real Word toggles field-code display for a simple field exactly like a complex one -- a PAGE
/// field shows <c>{ PAGE }</c>, a DATE field shows <c>{ DATE }</c> -- and the keyword each maps to was
/// already documented on <see cref="RunFieldKind"/> itself. The flag now lives directly on
/// <see cref="Run.FieldCodeVisible"/>, mirroring how <see cref="Run.FieldLocked"/> already carries the
/// Ctrl+F11 lock for the identical run shape (round 165's fix, the sibling this one now matches instead of
/// deliberately diverging from).
/// </summary>
public sealed class R167_ToggleFieldCodeSiblingTests
{
    [Fact]
    public void ToggleFieldCodeAtCaret_OnASimpleRunFieldKindField_TogglesCodeDisplayAndBack()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph());
        var view = new DocumentView();
        view.LoadDocument(document);

        // Mirrors Insert > Quick Parts > Date (FreeWAvaloniaRibbonCommands wires the Date ribbon button to
        // exactly this call) -- a RunFieldKind field with no ComplexField wrapper, the exact shape
        // SelectedOrCurrentComplexFields cannot see, so ToggleFieldCodeAtCaret must fall back to
        // ToggleSimpleFieldCodeAtCaret for it.
        view.InsertField(RunFieldKind.Date);
        var run = ((Paragraph)view.Document.Blocks[0]).Runs.Single();
        run.FieldKind.Should().Be(RunFieldKind.Date);
        var textBefore = run.Text;
        var lockedBefore = run.FieldLocked;

        view.MoveCaretToBlockForTest(0, 1);

        view.ToggleFieldCodeAtCaret();

        var runAfterFirstToggle = ((Paragraph)view.Document.Blocks[0]).Runs.Single();
        runAfterFirstToggle.FieldKind.Should().Be(RunFieldKind.Date);
        runAfterFirstToggle.FieldCodeVisible.Should().BeTrue(
            "Shift+F9 must show a simple field's code just like a complex field's");
        // The displayed text this drives: DocumentFieldDisplayPlanner.ResolveCode is exactly what
        // ResolveSimpleFieldDisplayText (the DisplayCells/WrapCellLines render path) returns while
        // FieldCodeVisible is set.
        DocumentFieldDisplayPlanner.ResolveCode(runAfterFirstToggle.FieldKind).Should().Be("{ DATE }");

        view.MoveCaretToBlockForTest(0, 1);
        view.ToggleFieldCodeAtCaret();

        var runAfterSecondToggle = ((Paragraph)view.Document.Blocks[0]).Runs.Single();
        runAfterSecondToggle.FieldCodeVisible.Should().BeFalse("toggling again must restore the result view");
        runAfterSecondToggle.Text.Should().Be(textBefore, "unrelated to display mode, the cached result must be untouched");
        runAfterSecondToggle.FieldLocked.Should().Be(lockedBefore);
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
        // Keep the unrelated sibling's rendered and cached lengths identical. A live DATE field is a
        // poor fixture here because its rendered length varies with the current date and culture while
        // this test positions the synthetic caret in display-offset space. That made the caret miss both
        // fields in short-date cultures and masked the actual sibling-targeting assertion.
        var complex = Run.ComplexFieldRun(" MERGEFIELD Neighbor ", "cached sibling text");
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
        var simpleRunAfter = runs.Single(r => r.FieldKind == RunFieldKind.Date);
        simpleRunAfter.FieldCodeVisible.Should().BeTrue(
            "the simple field beside the untouched complex field must be the one that toggled");
    }

    /// <summary>
    /// Document-wide surface (Alt+F9, <see cref="DocumentView.ToggleFieldCodes"/>): a ribbon-inserted
    /// (Insert &gt; Header &amp; Footer &gt; Page Number) PAGE field toggles its code display and back,
    /// matching the WPF host's identical fix.
    /// </summary>
    [Fact]
    public void ToggleFieldCodes_RibbonInsertedPageNumberField_TogglesCodeDisplayAndBack()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph());
        var view = new DocumentView();
        view.LoadDocument(document);
        view.InsertField(RunFieldKind.PageNumber);
        var textBefore = ((Paragraph)view.Document.Blocks[0]).Runs.Single().Text;

        view.ToggleFieldCodes();

        var runAfterFirstToggle = ((Paragraph)view.Document.Blocks[0]).Runs.Single();
        runAfterFirstToggle.FieldCodeVisible.Should().BeTrue();
        DocumentFieldDisplayPlanner.ResolveCode(runAfterFirstToggle.FieldKind).Should().Be("{ PAGE }");

        view.ToggleFieldCodes();

        var runAfterSecondToggle = ((Paragraph)view.Document.Blocks[0]).Runs.Single();
        runAfterSecondToggle.FieldCodeVisible.Should().BeFalse();
        runAfterSecondToggle.Text.Should().Be(textBefore);
    }
}
