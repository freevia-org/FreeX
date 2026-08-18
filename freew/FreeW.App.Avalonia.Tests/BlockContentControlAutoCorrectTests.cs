using System.Threading;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round 144 remediation (autocorrect-bypasses-block-content-control-lock): r143 taught
/// <c>DocumentEditingSession.IsPortableBodyTextParagraph</c> to decline a paragraph whose
/// <see cref="Paragraph.BlockContentControl"/> is locked (<see cref="ContentControlLockMode.ContentLocked"/>
/// / <see cref="ContentControlLockMode.ControlAndContentLocked"/>), so ordinary typing/Backspace/Enter
/// against a body-level <c>w:sdt</c> is refused. <c>DocumentView.TryAutoCorrect</c> mutates the paragraph
/// directly via <c>ReplaceParagraphRunsCommand</c> and was guarded only by <c>IsEditable</c> -- which checks
/// only run-level markers -- so AutoCorrect/AutoFormat-as-you-type could still silently edit text inside a
/// locked block-level content control. These tests exercise the real, unmodified typed-character path
/// (<see cref="DocumentView.SimulateTextInputForTest"/> -&gt; <c>OnTextInput</c> -&gt; <c>TryAutoCorrect</c>)
/// with no reflection or bypass of production code.
/// </summary>
public sealed class BlockContentControlAutoCorrectTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task AutoCorrect_DoesNotMutate_ContentLockedBlockControl()
    {
        string? text = null;
        await Session.Dispatch(() =>
        {
            var view = NewEditorWithBlockControl("I teh", ContentControlLockMode.ContentLocked);
            view.MoveCaretToBlockForTest(0, "I teh".Length);

            // The trailing space both would trigger "teh " -> "the " AutoCorrect AND is itself an
            // ordinary keystroke; a body-level content-locked paragraph must refuse both -- nothing at
            // all should reach the model.
            view.SimulateTextInputForTest(" ");

            text = view.Document.PlainText;
        }, CancellationToken.None);

        text.Should().Be("I teh",
            "a body-level w:sdt locked with sdtContentLocked must block AutoCorrect the same way it " +
            "already blocks ordinary typing (r143) -- TryAutoCorrect must not reach ReplaceParagraphRunsCommand");
    }

    [Fact]
    public async Task AutoCorrect_DoesNotMutate_ControlAndContentLockedBlockControl()
    {
        string? text = null;
        await Session.Dispatch(() =>
        {
            var view = NewEditorWithBlockControl("I teh", ContentControlLockMode.ControlAndContentLocked);
            view.MoveCaretToBlockForTest(0, "I teh".Length);

            view.SimulateTextInputForTest(" ");

            text = view.Document.PlainText;
        }, CancellationToken.None);

        text.Should().Be("I teh");
    }

    /// <summary>
    /// Sibling/regression guard: an UNLOCKED block-level content control is not the run-level
    /// checkbox/date-picker/drop-down case (no dedicated interactive UI) -- ordinary typing, and
    /// therefore AutoCorrect, must keep working inside it exactly as it did before this fix.
    /// </summary>
    [Fact]
    public async Task AutoCorrect_StillApplies_InsideUnlockedBlockControl()
    {
        string? text = null;
        await Session.Dispatch(() =>
        {
            var view = NewEditorWithBlockControl("I teh", ContentControlLockMode.NotSpecified);
            view.MoveCaretToBlockForTest(0, "I teh".Length);

            view.SimulateTextInputForTest(" ");

            text = view.Document.PlainText;
        }, CancellationToken.None);

        text.Should().Be("I the ",
            "an unlocked block-level content control has no dedicated interactive UI, so ordinary " +
            "typing -- and AutoCorrect along with it -- must remain unaffected by this fix");
    }

    private static DocumentView NewEditorWithBlockControl(string text, ContentControlLockMode lockMode)
    {
        var paragraph = new Paragraph(text)
        {
            BlockContentControl = new BlockContentControl(BlockContentControlKind.RichText, LockMode: lockMode)
        };
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);
        return view;
    }
}
