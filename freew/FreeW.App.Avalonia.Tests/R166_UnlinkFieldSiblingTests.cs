using System.Linq;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round 166 (meta F2): <c>UnlinkFieldAtCaret</c> (Ctrl+Shift+F9) is the sibling of round 165's
/// <c>SetFieldLockAtCaret</c> fix and has the identical <see cref="RunFieldKind"/> blind spot --
/// <c>SelectedOrCurrentComplexFields</c> only ever returns a run with <c>ComplexField: not null</c>, so a
/// simple field (Insert &gt; Header &amp; Footer &gt; Page Number, Insert &gt; Quick Parts &gt; Date/etc.)
/// was a silent no-op under Ctrl+Shift+F9. Fixed by falling back to a new
/// <c>UnlinkSimpleFieldAtCaret</c>, mirroring how round 165 added <c>SetSimpleFieldLockAtCaret</c>.
/// </summary>
public sealed class R166_UnlinkFieldSiblingTests
{
    [Fact]
    public void UnlinkFieldAtCaret_ConvertsASimpleRunFieldKindFieldToStaticText()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph());
        var view = new DocumentView();
        view.LoadDocument(document);

        // Mirrors Insert > Quick Parts > Date (FreeWAvaloniaRibbonCommands wires the Date ribbon button
        // to exactly this call) -- a RunFieldKind field with no ComplexField wrapper, the exact shape
        // SelectedOrCurrentComplexFields cannot see.
        view.InsertField(RunFieldKind.Date);
        var run = ((Paragraph)view.Document.Blocks[0]).Runs.Single();
        run.FieldKind.Should().Be(RunFieldKind.Date);
        var liveText = run.Text;

        view.MoveCaretToBlockForTest(0, 1);
        view.UnlinkFieldAtCaret();

        var unlinkedRun = ((Paragraph)view.Document.Blocks[0]).Runs.Single();
        unlinkedRun.FieldKind.Should().Be(
            RunFieldKind.None,
            "Ctrl+Shift+F9 must convert the field run into plain static text, the same way it already " +
            "does for a ComplexField");
        unlinkedRun.Text.Should().Be(
            liveText,
            "the static text left behind must be the field's resolved display value");
    }

    /// <summary>Sibling no-regression: unlinking a ComplexField at the caret still works exactly as
    /// before (the branch this fix's fallback must not disturb).</summary>
    [Fact]
    public void UnlinkFieldAtCaret_StillUnlinksAComplexFieldAtTheCaret()
    {
        var field = Run.ComplexFieldRun(" DATE ", "cached date text");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph { Runs = { field } });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.MoveCaretToBlockForTest(0, 1);
        view.UnlinkFieldAtCaret();

        var run = ((Paragraph)view.Document.Blocks[0]).Runs.Single();
        run.ComplexField.Should().BeNull("the complex-field wrapper must be detached, converting it to static text");
    }

    /// <summary>Sibling no-regression: a caret sitting on ordinary text (no field of either kind under it)
    /// keeps the original silent no-op -- there is nothing to unlink.</summary>
    [Fact]
    public void UnlinkFieldAtCaret_OnPlainTextCaret_RemainsANoOp()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Just plain text"));
        var view = new DocumentView();
        view.LoadDocument(document);

        view.MoveCaretToBlockForTest(0, 3);

        view.UnlinkFieldAtCaret();

        var run = ((Paragraph)view.Document.Blocks[0]).Runs.Single();
        run.Text.Should().Be("Just plain text");
    }
}
