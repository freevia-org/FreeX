using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round 165 fix wave, four findings in <c>freew/FreeW.App.Avalonia/Editing/DocumentView.cs</c> --
/// all four are the same story: a feature the WPF host has that this shell was silently missing.
///
/// (meta F1) <c>SetFieldLockAtCaret</c> (Ctrl+F11 / Ctrl+Shift+F11) was a no-op for a
/// <see cref="RunFieldKind"/> (simple) field -- it only ever looked for a <c>ComplexField</c> run at the
/// caret. Fixed by falling back to a new <c>SetSimpleFieldLockAtCaret</c>, mirroring the WPF host.
///
/// (freew-avalonia-fields F2) Body/table complex-field rendering (<c>BuildBodyComplexFieldDisplayPlan</c>,
/// also reached from the table-cell wrap path <c>WrapCellLines</c>) ignored <c>ComplexField.IsLocked</c>,
/// so a locked DATE/TIME/PAGE/... complex field kept recomputing on every render. Fixed by returning the
/// cached text immediately when locked, matching the WPF host's <c>ResolveComplexFieldText</c> guard.
///
/// (freew-avalonia-fields F3) Body/table simple (<c>RunFieldKind</c>) fields never re-resolved at all --
/// <c>DisplayCells</c> (and <c>WrapCellLines</c> for table cells) had no branch for <c>run.FieldKind</c>,
/// so they always showed the stale cached <c>Run.Text</c> regardless of lock state. Fixed with a new
/// <c>ResolveSimpleField</c> resolver used by both.
///
/// (shared-find-replace F1) <c>ReplaceAllCore</c> did not wrap its per-match replace loop in an undo
/// group, so an N-match Replace All produced N separate undo-stack entries instead of one -- unlike Word
/// and unlike the WPF host's <c>FindReplaceDialog.ReplaceAll</c>. Fixed by wrapping the loop in
/// <c>BeginUndoGroup</c>/<c>CommitUndoGroup("Replace All")</c>.
/// </summary>
public sealed class R165_FieldLockAndReplaceAllUndoTests
{
    // ==== meta F1: Ctrl+F11 must lock a simple RunFieldKind field, not just a ComplexField ===========

    [Fact]
    public void SetFieldLockAtCaret_LocksAndUnlocksASimpleRunFieldKindFieldAtTheBodyCaret()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph());
        var view = new DocumentView();
        view.LoadDocument(document);

        // Mirrors Insert > Quick Parts > Date (or Insert > Header & Footer > Page Number): a RunFieldKind
        // field with no ComplexField wrapper -- exactly the shape SelectedOrCurrentComplexFields cannot see.
        view.InsertField(RunFieldKind.Date);
        var run = ((Paragraph)view.Document.Blocks[0]).Runs.Single();
        run.FieldKind.Should().Be(RunFieldKind.Date);
        run.FieldLocked.Should().BeFalse();

        view.MoveCaretToBlockForTest(0, 1);
        view.SetFieldLockAtCaret(true);

        run.FieldLocked.Should().BeTrue(
            "Ctrl+F11 must lock a RunFieldKind (simple) field the same way it already locks a ComplexField");

        view.SetFieldLockAtCaret(false);

        run.FieldLocked.Should().BeFalse("Ctrl+Shift+F11 must unlock it again");
    }

    /// <summary>Sibling no-regression: locking a ComplexField at the caret still works exactly as before
    /// (the branch this fix's fallback must not disturb).</summary>
    [Fact]
    public void SetFieldLockAtCaret_StillLocksAComplexFieldAtTheCaret()
    {
        var field = Run.ComplexFieldRun(" DATE ", "stale date");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph { Runs = { field } });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.MoveCaretToBlockForTest(0, 1);
        view.SetFieldLockAtCaret(true);

        field.ComplexField!.IsLocked.Should().BeTrue();
    }

    /// <summary>Sibling no-regression: a caret sitting on ordinary text (no field of either kind under it)
    /// keeps the original silent no-op -- there is nothing to lock.</summary>
    [Fact]
    public void SetFieldLockAtCaret_OnPlainTextCaret_RemainsANoOp()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Just plain text"));
        var view = new DocumentView();
        view.LoadDocument(document);

        view.MoveCaretToBlockForTest(0, 3);

        // Must not throw, and must not create an undo entry out of nothing.
        view.SetFieldLockAtCaret(true);

        view.CanUndo.Should().BeFalse();
    }

    // ==== freew-avalonia-fields F2/F3: locked/unlocked field rendering =================================

    [Fact]
    public async Task Locked_DateComplexField_RendersCachedTextInsteadOfRecomputingTheLiveDate()
    {
        string? rendered = null;
        var ran = await HeadlessUiThread.Run(() =>
        {
            var field = Run.ComplexFieldRun(
                " DATE ",
                "1/1/2000",
                sequence: new ComplexFieldSequenceMetadata(IsLocked: true, IsDirty: true));
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph { Runs = { field } });
            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(900, 1200));

            rendered = string.Concat(view.GetPlacedForBlock(0).Select(item => item.Ch));
        });

        if (!ran)
            return;
        rendered.Should().Be(
            "1/1/2000",
            "a locked DATE complex field must render its cached text, not today's live date -- matching " +
            "the WPF host's ResolveComplexFieldText IsLocked guard");
    }

    /// <summary>Sibling no-regression: the same DATE complex field, left UNLOCKED, must still re-resolve
    /// live on render (the behavior this fix must not take away).</summary>
    [Fact]
    public async Task Unlocked_DateComplexField_StillRendersTheLiveDate()
    {
        string? rendered = null;
        var ran = await HeadlessUiThread.Run(() =>
        {
            var field = Run.ComplexFieldRun(" DATE ", "1/1/2000");
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph { Runs = { field } });
            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(900, 1200));

            rendered = string.Concat(view.GetPlacedForBlock(0).Select(item => item.Ch));
        });

        if (!ran)
            return;
        rendered.Should().NotBe("1/1/2000");
        rendered.Should().MatchRegex(@"^\d{1,2}/\d{1,2}/\d{4}$");
    }

    [Fact]
    public async Task Unlocked_SimpleDateField_RendersTheLiveDateInsteadOfStaleCachedText()
    {
        string? rendered = null;
        var ran = await HeadlessUiThread.Run(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph
            {
                Runs = { new Run("1/1/2000") { FieldKind = RunFieldKind.Date, FieldLocked = false } }
            });
            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(900, 1200));

            rendered = string.Concat(view.GetPlacedForBlock(0).Select(item => item.Ch));
        });

        if (!ran)
            return;
        rendered.Should().NotBe(
            "1/1/2000",
            "an unlocked simple DATE field (e.g. imported from a Word .docx with automatic update) must " +
            "re-resolve to the live current date on render, not stay frozen at its stale cached text");
        rendered.Should().MatchRegex(@"^\d{1,2}/\d{1,2}/\d{4}$");
    }

    /// <summary>Sibling no-regression: the same simple field, LOCKED, must stay frozen at its cached text
    /// (the F1/F2 guarantee this F3 fix must not break for the simple-field form).</summary>
    [Fact]
    public async Task Locked_SimpleDateField_StillRendersTheCachedText()
    {
        string? rendered = null;
        var ran = await HeadlessUiThread.Run(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph
            {
                Runs = { new Run("1/1/2000") { FieldKind = RunFieldKind.Date, FieldLocked = true } }
            });
            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(900, 1200));

            rendered = string.Concat(view.GetPlacedForBlock(0).Select(item => item.Ch));
        });

        if (!ran)
            return;
        rendered.Should().Be("1/1/2000");
    }

    // ==== shared-find-replace F1: Replace All undoes as one entry ======================================

    [Fact]
    public void ReplaceAll_UndoesAsOneEntry_RestoringEveryReplacementInOneCtrlZ()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("MAGIC one"));
        document.Blocks.Add(new Paragraph("MAGIC two"));
        document.Blocks.Add(new Paragraph("MAGIC three"));
        var view = new DocumentView();
        view.LoadDocument(document);

        var count = view.ReplaceAll("MAGIC", "CHANGED");

        count.Should().Be(3);
        view.CanUndo.Should().BeTrue();

        view.Undo();

        var restored = view.Document.Blocks.Cast<Paragraph>().Select(p => p.PlainText).ToArray();
        restored.Should().Equal(
            "MAGIC one",
            "MAGIC two",
            "MAGIC three");
        view.CanUndo.Should().BeFalse(
            "all three replacements must have collapsed into a single undo entry, matching Word and the " +
            "WPF shell's FindReplaceDialog.ReplaceAll grouping");
    }

    /// <summary>Sibling no-regression: a zero-match Replace All must not leave a phantom (empty) undo
    /// entry on the stack -- CommitUndoGroup is a no-op when nothing was collected.</summary>
    [Fact]
    public void ReplaceAll_WithNoMatches_LeavesNoUndoEntry()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("nothing to see here"));
        var view = new DocumentView();
        view.LoadDocument(document);

        var count = view.ReplaceAll("MAGIC", "CHANGED");

        count.Should().Be(0);
        view.CanUndo.Should().BeFalse();
    }

    /// <summary>Sibling no-regression: a single-match Replace All still undoes cleanly in one step (the
    /// ordinary case, not just the N&gt;1 grouping case this fix targets).</summary>
    [Fact]
    public void ReplaceAll_WithOneMatch_StillUndoesInOneStep()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("only one MAGIC here"));
        var view = new DocumentView();
        view.LoadDocument(document);

        var count = view.ReplaceAll("MAGIC", "CHANGED");
        count.Should().Be(1);

        view.Undo();

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Be("only one MAGIC here");
        view.CanUndo.Should().BeFalse();
    }
}
