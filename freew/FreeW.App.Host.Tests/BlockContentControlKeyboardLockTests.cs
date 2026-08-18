using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round 143 remediation (content-control-lock-ignores-block-level-sdt): round 142's content-control
/// keyboard lock (<see cref="ContentControlKeyboardLockTests"/>) only ever inspected a RUN-level
/// <see cref="ModelContentControl"/> (<c>Run.Control</c>) at the caret. A body-level (whole-paragraph)
/// <c>w:sdt</c> -- the shape Word produces for "lock whole paragraph", carried on
/// <see cref="Paragraph.BlockContentControl"/> instead of any run -- had no run-level marker at all, so it
/// was freely typed into, backspaced through, Entered into, and pasted over regardless of its
/// <see cref="ContentControlLockMode"/>.
///
/// <para>
/// Two independent gaps combined to produce that hole, and this suite proves both are closed:
/// </para>
/// <list type="bullet">
/// <item>the choke points (<c>DocumentView.TryPrepareNativeFallback</c>/<c>IsCaretOnLockedContentControl</c>/
/// <c>TryEvaluateContentControlLock</c>) never consulted the caret paragraph's
/// <see cref="Paragraph.BlockContentControl"/> at all -- proven directly via reflection on the real
/// choke-point methods, same style as <see cref="ContentControlKeyboardLockTests"/>;</item>
/// <item>even once a choke point knows to look, <c>Paragraph.BlockContentControl</c> had no Tag-borne
/// round-trip (unlike run-level <c>Control</c>, restored via <c>RunMarkers</c>), so it was silently
/// dropped the moment any edit anywhere in the document forced a <c>CommitToModel</c>/<c>Render</c> cycle
/// -- proven end-to-end via the real public <see cref="DocumentView.InsertText"/>, whose structural
/// fallback path commits the model (<c>TryApplyBodyTextInput</c>) before ever reaching the choke point,
/// exactly like a real keystroke would.</item>
/// </list>
///
/// <para>
/// Follows <see cref="ContentControlKeyboardLockTests"/>'s established approach: reflection-based direct
/// invocation of the private choke points for the pieces that need real OS keyboard focus to engage
/// natively (this headless host cannot make that deterministic -- see that class's doc comment), and real
/// production call sites (<c>InsertText</c>, <c>ApplicationCommands.Paste/Cut.CanExecute</c>,
/// <c>OnPreviewKeyDown</c> for Enter) everywhere a real dispatch is reliable.
/// </para>
/// </summary>
public sealed class BlockContentControlKeyboardLockTests
{
    private static readonly MethodInfo TryPrepareNativeFallbackMethod =
        typeof(DocumentView).GetMethod("TryPrepareNativeFallback", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            "DocumentView.TryPrepareNativeFallback not found -- the choke point this test targets was renamed or removed.");

    private static readonly MethodInfo IsCaretOnLockedContentControlMethod =
        typeof(DocumentView).GetMethod("IsCaretOnLockedContentControl", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            "DocumentView.IsCaretOnLockedContentControl not found -- the predicate backing the Paste/Cut gate was renamed or removed.");

    private static readonly MethodInfo OnPreviewKeyDownMethod =
        typeof(DocumentView).GetMethod("OnPreviewKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("DocumentView.OnPreviewKeyDown not found -- it was renamed or removed.");

    /// <summary>
    /// Loads a single paragraph ("Alice") whose <see cref="Paragraph.BlockContentControl"/> is a
    /// whole-paragraph (block-level) content control with the given lock mode -- deliberately WITHOUT
    /// setting any run's <see cref="ModelContentControl"/>, so only the block-level path can catch it.
    /// </summary>
    private static DocumentView LoadWithBlockContentControl(string text, ContentControlLockMode lockMode)
    {
        var paragraph = new Paragraph(text)
        {
            BlockContentControl = new BlockContentControl(BlockContentControlKind.RichText, LockMode: lockMode)
        };
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);
        document.Blocks.Add(new Paragraph("Body text"));

        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    /// <summary>Same run-walking approach as <c>ContentControlKeyboardLockTests.PlaceCaretAfterText</c>.</summary>
    private static void PlaceCaretAfterText(DocumentView view, string text, int paragraphIndex = 0)
    {
        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ElementAt(paragraphIndex);
        var remaining = text.Length;
        var pointer = paragraph.ContentStart;
        while (pointer is not null && pointer.CompareTo(paragraph.ContentEnd) < 0)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var runText = pointer.GetTextInRun(LogicalDirection.Forward);
                if (remaining <= runText.Length)
                {
                    var target = pointer.GetPositionAtOffset(remaining)!;
                    view.CaretPosition = target;
                    view.Selection.Select(target, target);
                    return;
                }
                remaining -= runText.Length;
            }

            pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
        }

        throw new InvalidOperationException($"Text '{text}' was not found in paragraph {paragraphIndex}.");
    }

    private static void SelectTextRange(DocumentView view, int startOffset, int endOffset, int paragraphIndex = 0)
    {
        TextPointer AtOffset(int offset)
        {
            var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ElementAt(paragraphIndex);
            var remaining = offset;
            var pointer = paragraph.ContentStart;
            while (pointer is not null && pointer.CompareTo(paragraph.ContentEnd) < 0)
            {
                if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var runText = pointer.GetTextInRun(LogicalDirection.Forward);
                    if (remaining <= runText.Length)
                        return pointer.GetPositionAtOffset(remaining)!;
                    remaining -= runText.Length;
                }
                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
            }
            throw new InvalidOperationException($"Offset {offset} was not found in paragraph {paragraphIndex}.");
        }

        var start = AtOffset(startOffset);
        var end = AtOffset(endOffset);
        view.CaretPosition = end;
        view.Selection.Select(start, end);
    }

    private static (bool Allowed, bool RestoreReadOnly) TryPrepareNativeFallback(DocumentView view)
    {
        var args = new object?[] { null };
        var allowed = (bool)TryPrepareNativeFallbackMethod.Invoke(view, args)!;
        return (allowed, (bool)args[0]!);
    }

    private static bool IsCaretOnLockedContentControl(DocumentView view) =>
        (bool)IsCaretOnLockedContentControlMethod.Invoke(view, null)!;

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // TryPrepareNativeFallback: the same choke point ContentControlKeyboardLockTests proves for
    // run-level controls, now proven for a block-level (whole-paragraph) one.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void TryPrepareNativeFallback_BlocksContentLockedBlockControl_EvenWithNoProtectionActive()
    {
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.ContentLocked);
        view.IsReadOnly.Should().BeFalse("no document-level protection is active");
        PlaceCaretAfterText(view, "Al");

        var (allowed, _) = TryPrepareNativeFallback(view);

        allowed.Should().BeFalse(
            "a body-level w:sdt locked with sdtContentLocked must never reach native RichTextBox editing, " +
            "the same as a run-level content-locked control");
    }

    [StaFact]
    public void TryPrepareNativeFallback_BlocksControlAndContentLockedBlockControl()
    {
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.ControlAndContentLocked);
        PlaceCaretAfterText(view, "Al");

        var (allowed, _) = TryPrepareNativeFallback(view);

        allowed.Should().BeFalse();
    }

    [StaFact]
    public void TryPrepareNativeFallback_AllowsUnlockedBlockControl_AndLeavesIsReadOnlyAlone()
    {
        // Sibling/regression guard: an unlocked block-level content control is not the run-level checkbox/
        // date-picker/drop-down case (no dedicated interactive UI), so ordinary typing must keep working --
        // the choke point must be a no-op here exactly as it is outside any content control at all.
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.NotSpecified);
        view.IsReadOnly.Should().BeFalse();
        PlaceCaretAfterText(view, "Al");

        var (allowed, restoreReadOnly) = TryPrepareNativeFallback(view);

        allowed.Should().BeTrue();
        restoreReadOnly.Should().BeFalse("IsReadOnly was already false, so nothing needs restoring");
        view.IsReadOnly.Should().BeFalse();
    }

    [StaFact]
    public void IsCaretOnLockedContentControl_TrueForLockedBlockControl()
    {
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.ContentLocked);
        PlaceCaretAfterText(view, "Al");

        IsCaretOnLockedContentControl(view).Should().BeTrue();
    }

    [StaFact]
    public void IsCaretOnLockedContentControl_FalseForUnlockedBlockControl()
    {
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.NotSpecified);
        PlaceCaretAfterText(view, "Al");

        IsCaretOnLockedContentControl(view).Should().BeFalse();
    }

    [StaFact]
    public void IsCaretOnLockedContentControl_FalseOutsideTheLockedParagraph()
    {
        // Regression guard: the second (ordinary) paragraph must not be affected by the first paragraph's
        // block-level lock.
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.ContentLocked);
        PlaceCaretAfterText(view, "Body", paragraphIndex: 1);

        IsCaretOnLockedContentControl(view).Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Enter: real routed OnPreviewKeyDown dispatch (reflection-invoked, matching
    // ContentControlKeyboardLockTests -- see that class's doc comment on why a shown window plus direct
    // invocation, not RaiseEvent, is what makes this deterministic in a headless host).
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static bool InvokeOnPreviewKeyDownForEnter(DocumentView view)
    {
        var source = PresentationSource.FromVisual(view)
            ?? throw new InvalidOperationException("A shown window is required to construct a KeyEventArgs.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        OnPreviewKeyDownMethod.Invoke(view, [args]);
        return args.Handled;
    }

    [StaFact]
    public void OnPreviewKeyDown_MarksEnterHandled_WhenCaretInsideContentLockedBlockControl()
    {
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.ContentLocked);
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Left = -10000,
            Top = -10000,
            Width = 200,
            Height = 200,
            Content = view,
        };
        try
        {
            window.Show();
            PlaceCaretAfterText(view, "Al");

            InvokeOnPreviewKeyDownForEnter(view).Should().BeTrue(
                "Enter inside a body-level content-locked paragraph must be marked handled before it can " +
                "reach native paragraph insertion, the same as a run-level content-locked control");

            // e.Handled alone is not proof: TryApplyBodyParagraphBreak's portable path also marks the
            // event handled when it SUCCEEDS, so the real proof is that the paragraph was never split.
            view.CommitToModel();
            view.Model.Blocks.Should().HaveCount(2,
                "Enter must not have split the locked paragraph in two");
            ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Be("Alice",
                "the locked paragraph's text must be unchanged");
        }
        finally
        {
            window.Close();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // InsertText: the real, non-reflection production call site. Critically, InsertText's structural
    // fallback path (see DocumentView.TryApplyBodyTextInput) commits the model BEFORE ever reaching
    // TryPrepareNativeFallback -- exactly like a real keystroke via OnPreviewTextInput. This is the test
    // that would have failed before EITHER half of the fix: before the choke points learned to check
    // BlockContentControl, and independently before ParagraphTag/ReadParagraph learned to round-trip it
    // through that commit.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void InsertText_Blocked_WhenCaretInsideContentLockedBlockControl()
    {
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.ContentLocked);
        PlaceCaretAfterText(view, "Al");

        view.InsertText("X");

        view.CommitToModel();
        ((Paragraph)view.Model.Blocks[0]).Runs.Single().Text.Should().Be("Alice",
            "InsertText's structural fallback must never reach a body-level content-locked paragraph, even " +
            "after the CommitToModel call TryApplyBodyTextInput performs on the way there");
    }

    [StaFact]
    public void InsertText_StillWorks_ForUnlockedBlockControl_WithNoProtection()
    {
        // Positive control / regression guard, same shape as
        // ContentControlKeyboardLockTests.InsertText_StillWorks_ForUnlockedControl_WithNoProtection: an
        // unlocked block-level content control must keep accepting ordinary typing (here it goes through
        // the portable body-edit session itself, since IsPortableBodyTextParagraph only excludes a LOCKED
        // block-level control -- see that method's doc comment).
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.NotSpecified);
        PlaceCaretAfterText(view, "Al");

        view.InsertText("X");

        view.CommitToModel();
        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Be("AlXice");
    }

    [StaFact]
    public void InsertText_BlockLock_SurvivesAnUnrelatedEditElsewhereForcingCommitToModel()
    {
        // Direct proof of the round-trip half of the fix: an ordinary edit to the SECOND (unrelated)
        // paragraph forces its own CommitToModel/Render cycle first. Paragraph.BlockContentControl has no
        // structural FlowDocument slot, so without ParagraphTag/ReadParagraph carrying it across that
        // cycle, the first paragraph's lock would already be gone by the time this second InsertText call
        // is checked.
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.ContentLocked);
        PlaceCaretAfterText(view, "Body", paragraphIndex: 1);
        view.InsertText("!");
        view.CommitToModel();
        ((Paragraph)view.Model.Blocks[1]).PlainText.Should().Be("Body! text",
            "sanity check: the unrelated edit itself must have applied");

        PlaceCaretAfterText(view, "Al");
        view.InsertText("X");

        view.CommitToModel();
        ((Paragraph)view.Model.Blocks[0]).Runs.Single().Text.Should().Be("Alice",
            "the block-level lock on the first paragraph must still be enforced after an unrelated commit");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Paste/Cut CanExecute: the real ApplicationCommands.Paste/Cut.CanExecute call, same as
    // ContentControlKeyboardLockTests -- WPF's CanExecuteRoutedEventArgs constructor is internal, so this
    // real call is the only way to exercise OnPreviewCanExecuteClipboardMutation.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static void SeedClipboardText(string text)
    {
        const int MaxAttempts = 3;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch when (attempt < MaxAttempts - 1)
            {
                Thread.Sleep(10);
            }
            catch
            {
                // Final attempt failed -- clipboard contention; best-effort, same as production.
            }
        }
    }

    [StaFact]
    public void Paste_CanExecute_False_WhenCaretInsideContentLockedBlockControl()
    {
        SeedClipboardText("r143 block-lock paste probe");
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.ContentLocked);
        PlaceCaretAfterText(view, "Al");

        ApplicationCommands.Paste.CanExecute(null, view).Should().BeFalse(
            "pasting into a body-level content-locked paragraph must be refused before Execute is ever reached");
    }

    [StaFact]
    public void Paste_Execute_DoesNotMutateModel_WhenCalledDirectly_BypassingCanExecute()
    {
        SeedClipboardText("r143 block-lock paste probe");
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.ContentLocked);
        PlaceCaretAfterText(view, "Al");

        ApplicationCommands.Paste.Execute(null, view);

        view.CommitToModel();
        ((Paragraph)view.Model.Blocks[0]).Runs.Single().Text.Should().Be("Alice",
            "native paste must never reach a body-level content-locked paragraph, even via a direct Execute call");
    }

    [StaFact]
    public void Cut_CanExecute_False_WhenSelectionInsideControlAndContentLockedBlockControl()
    {
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.ControlAndContentLocked);
        SelectTextRange(view, 0, 5);

        ApplicationCommands.Cut.CanExecute(null, view).Should().BeFalse();
    }

    [StaFact]
    public void Paste_CanExecute_NotForcedFalse_OutsideTheLockedParagraph()
    {
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.ContentLocked);
        PlaceCaretAfterText(view, "Body", paragraphIndex: 1);

        IsCaretOnLockedContentControl(view).Should().BeFalse(
            "the Paste/Cut CanExecute gate must be scoped to the locked paragraph, not the whole document");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // TryEvaluateContentControlLock: the internal probe the opt-in Page Edit surface
    // (PaginatedEditorPanel/PageBox) wires -- called directly (internal, visible to this test assembly),
    // same approach PagedEditContentControlLockTests uses for the run-level case.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void TryEvaluateContentControlLock_ReturnsFalse_ForLockedBlockControl()
    {
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.ContentLocked);
        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        var pointer = paragraph.ContentStart.GetPositionAtOffset(2, LogicalDirection.Forward);

        view.TryEvaluateContentControlLock(pointer).Should().BeFalse(
            "the Page Edit surface's lock probe must also see a body-level content-locked paragraph, not just a run-level one");
    }

    [StaFact]
    public void TryEvaluateContentControlLock_ReturnsNull_OutsideAnyContentControl()
    {
        var view = LoadWithBlockContentControl("Alice", ContentControlLockMode.ContentLocked);
        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ElementAt(1);
        var pointer = paragraph.ContentStart.GetPositionAtOffset(2, LogicalDirection.Forward);

        view.TryEvaluateContentControlLock(pointer).Should().BeNull(
            "a position with neither a run-level nor a block-level content control has nothing to gate");
    }
}
