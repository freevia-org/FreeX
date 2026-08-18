using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Enforcement coverage for the content-control lock (<see cref="ContentControlLockMode"/>) and the
/// Filling-In-Forms permission (<see cref="RestrictEditingOperationKind.FormFieldEdit"/>) at the ONE
/// choke point that governs keyboard editing of a content control's text in the WPF host:
/// <c>DocumentView.TryPrepareNativeFallback</c>, consulted from <c>OnPreviewTextInput</c> and the
/// Backspace/Delete branch of <c>OnPreviewKeyDown</c> before either falls through to native RichTextBox
/// editing. Before this choke point existed:
/// <list type="bullet">
/// <item>a content-locked control could be typed into / backspaced through anyway, because
/// <c>TryApplyBodyTextInput</c> declines any paragraph holding a content control and the WPF host fell
/// through unconditionally to native <c>RichTextBox</c> editing, which has no notion of the lock
/// (freew-cc-2);</item>
/// <item>Filling-In-Forms protection set the whole surface's <c>IsReadOnly</c> to block ordinary body
/// text, which also blocked the native fallback for the very Plain-Text/Rich-Text fields that protection
/// mode exists to let the user fill in (freew-cc-1).</item>
/// </list>
/// <see cref="TryPrepareNativeFallback"/> below invokes the real private choke-point method via
/// reflection on a real, model-loaded <see cref="DocumentView"/> -- exercising the actual production
/// decision, not a stub. This is deliberate rather than raising a composed keystroke and inspecting the
/// resulting text: whether WPF's native <c>TextEditor</c> physically inserts a character afterward
/// depends on real OS-level window activation/focus timing that this headless test host cannot make
/// deterministic (confirmed empirically -- an end-to-end keystroke into an *allowed* content control was
/// flaky across runs, and a keystroke into a *blocked* one "passed" even with the choke point removed,
/// because native RichTextBox editing quietly never engages without real focus regardless of the fix; a
/// test that passes on broken code proves nothing). <see cref="Typing_OutsideAnyContentControl_UnderFillingForms_StaysBlocked_ViaRealKeystroke"/>
/// is the one exception: it is a same-as-before regression guard (not required to fail before either fix),
/// so its independence from native-editor timing does not matter. Runs on an STA thread (<c>[StaFact]</c>,
/// via Xunit.StaFact) because the RichTextBox/FlowDocument need STA.
/// </summary>
public sealed class ContentControlKeyboardLockTests
{
    private static readonly MethodInfo TryPrepareNativeFallbackMethod =
        typeof(DocumentView).GetMethod("TryPrepareNativeFallback", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            "DocumentView.TryPrepareNativeFallback not found -- the choke point this test targets was renamed or removed.");

    private static DocumentView LoadWithPlainTextControl(string text, ContentControlLockMode lockMode)
    {
        var run = Run.PlainTextControl(text, tag: "Name");
        run.Control = run.Control! with { LockMode = lockMode };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(run);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    /// <summary>
    /// Positions the WPF caret right after <paramref name="text"/> in the (single) paragraph's rendered
    /// text -- walking run-by-run like <c>ContentControlEditorTests.PositionAfterText</c>, so the
    /// resulting <see cref="TextPointer"/> reliably lands inside the same native <c>Run</c> the content
    /// control rendered into (a raw offset-based <c>GetPositionAtOffset</c> can straddle a boundary).
    /// </summary>
    private static void PlaceCaretAfterText(DocumentView view, string text)
    {
        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
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

        throw new InvalidOperationException($"Text '{text}' was not found in the paragraph.");
    }

    /// <summary>
    /// Invokes the real (private) <c>DocumentView.TryPrepareNativeFallback(out bool)</c> -- the exact
    /// choke point <c>OnPreviewTextInput</c>/<c>OnPreviewKeyDown</c> consult -- against the caret/selection
    /// already set on <paramref name="view"/>.
    /// </summary>
    private static (bool Allowed, bool RestoreReadOnly) TryPrepareNativeFallback(DocumentView view)
    {
        var args = new object?[] { null };
        var allowed = (bool)TryPrepareNativeFallbackMethod.Invoke(view, args)!;
        return (allowed, (bool)args[0]!);
    }

    /// <summary>
    /// Raises a real <see cref="TextCompositionManager.PreviewTextInputEvent"/> at <paramref name="view"/>
    /// via <see cref="TextCompositionManager.StartComposition"/> -- the same staging WPF uses for a typed
    /// character -- so the test exercises <c>DocumentView.OnPreviewTextInput</c> itself, not a test-only
    /// shortcut.
    /// </summary>
    private static void TypeCharacter(DocumentView view, string text) =>
        TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, view, text));

    [StaFact]
    public void TryPrepareNativeFallback_BlocksContentLockedControl_EvenWithNoProtectionActive()
    {
        // No Restrict Editing protection at all -- IsReadOnly is false, exactly the scenario where the
        // WPF host previously fell straight through to native RichTextBox editing with zero knowledge of
        // the per-control w:lock="contentLocked" (freew-cc-2).
        var view = LoadWithPlainTextControl("Alice", ContentControlLockMode.ContentLocked);
        view.IsReadOnly.Should().BeFalse("no document-level protection is active");
        PlaceCaretAfterText(view, "Al");

        var (allowed, _) = TryPrepareNativeFallback(view);

        allowed.Should().BeFalse("a content-locked control must never reach native RichTextBox editing");
    }

    [StaFact]
    public void TryPrepareNativeFallback_BlocksControlAndContentLockedControl()
    {
        var view = LoadWithPlainTextControl("Alice", ContentControlLockMode.ControlAndContentLocked);
        PlaceCaretAfterText(view, "Al");

        var (allowed, _) = TryPrepareNativeFallback(view);

        allowed.Should().BeFalse();
    }

    [StaFact]
    public void TryPrepareNativeFallback_AllowsUnlockedControl_UnderFillingForms_AndClearsIsReadOnly()
    {
        // Filling-In-Forms sets IsReadOnly for ordinary body text (see
        // FillingFormsProtection_BlocksNormalBodyEdits_AndReportsFormOnlyPolicy in
        // ProtectionEnforcementTests), but the whole point of the mode is that its Plain-Text/Rich-Text
        // fields stay fillable (freew-cc-1) -- which the native fallback can only do if IsReadOnly is
        // cleared for that one call.
        var view = LoadWithPlainTextControl("Alice", ContentControlLockMode.NotSpecified);
        view.SetProtection(ProtectionMode.FillingForms);
        view.IsReadOnly.Should().BeTrue("Filling-In-Forms blocks ordinary body text");
        PlaceCaretAfterText(view, "Al");

        var (allowed, restoreReadOnly) = TryPrepareNativeFallback(view);

        allowed.Should().BeTrue("an unlocked form field must stay fillable under Filling-In-Forms protection");
        restoreReadOnly.Should().BeTrue("IsReadOnly was set and must be restored by the caller after the one native edit");
        view.IsReadOnly.Should().BeFalse("cleared for the duration of the permitted native edit");
    }

    [StaFact]
    public void TryPrepareNativeFallback_AllowsUnlockedControl_WithNoProtection_AndLeavesIsReadOnlyAlone()
    {
        // Sibling/regression guard: an ordinary (unprotected, unlocked) content control must keep working
        // exactly as before -- the choke point must be a no-op (no IsReadOnly toggling needed) outside the
        // locked/Filling-In-Forms cases.
        var view = LoadWithPlainTextControl("Alice", ContentControlLockMode.NotSpecified);
        view.IsReadOnly.Should().BeFalse();
        PlaceCaretAfterText(view, "Al");

        var (allowed, restoreReadOnly) = TryPrepareNativeFallback(view);

        allowed.Should().BeTrue();
        restoreReadOnly.Should().BeFalse("IsReadOnly was already false, so nothing needs restoring");
        view.IsReadOnly.Should().BeFalse();
    }

    [StaFact]
    public void TryPrepareNativeFallback_OutsideAnyContentControl_UnderFillingForms_StaysGovernedByIsReadOnly()
    {
        // Sibling/regression guard: the choke point must not loosen the existing, correct block on
        // ordinary body text -- outside a content control it must report "allowed" (the caller still
        // falls through to native editing exactly as before) and must not touch IsReadOnly, which is what
        // actually blocks the native editor for this case.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Body"));
        var view = new DocumentView();
        view.LoadModel(document);
        view.SetProtection(ProtectionMode.FillingForms);
        view.IsReadOnly.Should().BeTrue();

        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.CaretPosition = paragraph.ContentStart.GetPositionAtOffset(2, LogicalDirection.Forward);

        var (allowed, restoreReadOnly) = TryPrepareNativeFallback(view);

        allowed.Should().BeTrue("positions outside a content control keep the pre-existing fallback behavior");
        restoreReadOnly.Should().BeFalse();
        view.IsReadOnly.Should().BeTrue("still blocks the native editor for ordinary body text, unchanged");
    }

    [StaFact]
    public void Typing_OutsideAnyContentControl_UnderFillingForms_StaysBlocked_ViaRealKeystroke()
    {
        // End-to-end companion to the test above: ordinary body text stays untyped via a real keystroke
        // too (IsReadOnly, left true by the choke point, is what the native editor itself honors).
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Body"));
        var view = new DocumentView();
        view.LoadModel(document);
        view.SetProtection(ProtectionMode.FillingForms);

        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.CaretPosition = paragraph.ContentStart.GetPositionAtOffset(2, LogicalDirection.Forward);
        TypeCharacter(view, "X");

        view.CommitToModel();
        ((Paragraph)view.Model.Blocks[0]).Runs.Single().Text.Should().Be("Body");
    }
}
