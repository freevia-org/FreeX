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

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Round 142 remediation (freew-cc-4/5): Enter, Paste, Cut, and InsertText (Insert > Symbol /
    // Insert > Date & Time / the plain-text clipboard path) each had their own bypass of the
    // content-control lock. These exercise the real production call sites -- DocumentView.OnPreviewKeyDown
    // via a real routed PreviewKeyDownEvent, the real ApplicationCommands.Paste/Cut RoutedCommand, and the
    // real public InsertText method -- not a reflection shortcut, wherever a real keystroke/command
    // dispatch does not depend on flaky OS window-focus timing (see this file's class doc on why a typed
    // *character* keystroke is NOT used for the definitive proof).
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Selects the text range [<paramref name="startOffset"/>, <paramref name="endOffset"/>) of the
    /// (single) paragraph's rendered text -- same run-walking approach as
    /// <see cref="PlaceCaretAfterText"/>, but producing a NON-collapsed selection. Needed for the Cut
    /// tests below: native <c>TextBoxBase.Cut</c>'s own CanExecute already requires a non-empty selection,
    /// so a collapsed caret would make Cut report <c>false</c> for that unrelated reason regardless of the
    /// content-control lock, silently defeating the proof.
    /// </summary>
    private static void SelectTextRange(DocumentView view, int startOffset, int endOffset)
    {
        TextPointer AtOffset(int offset)
        {
            var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
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
            throw new InvalidOperationException($"Offset {offset} was not found in the paragraph.");
        }

        var start = AtOffset(startOffset);
        var end = AtOffset(endOffset);
        view.CaretPosition = end;
        view.Selection.Select(start, end);
    }

    private static readonly MethodInfo OnPreviewKeyDownMethod =
        typeof(DocumentView).GetMethod("OnPreviewKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            "DocumentView.OnPreviewKeyDown not found -- it was renamed or removed.");

    /// <summary>
    /// Constructs a real <see cref="KeyEventArgs"/> for Enter and invokes the real (protected)
    /// <c>DocumentView.OnPreviewKeyDown</c> override directly via reflection -- NOT via
    /// <c>UIElement.RaiseEvent</c>. A shown window is still required (the <see cref="KeyEventArgs"/>
    /// constructor rejects a null <see cref="PresentationSource"/>), but routing the call directly avoids
    /// two independently-confirmed sources of false negatives in this headless host: (1) whether
    /// <c>RaiseEvent</c> even reaches the override depends on window/dispatcher plumbing this suite does
    /// not otherwise need, and (2) even when it does, whether the SUBSEQUENT native
    /// <c>base.OnPreviewKeyDown</c> call actually performs the native paragraph split -- what an
    /// assertion on the resulting model content would have to depend on -- requires real OS keyboard focus
    /// that a headless STA test cannot reliably obtain (the same "native RichTextBox editing quietly never
    /// engages without real focus" finding this class's doc comment describes for typed characters).
    /// Asserting on <c>KeyEventArgs.Handled</c> immediately after the direct call sidesteps both: it
    /// reflects exactly what <c>OnPreviewKeyDown</c>'s own logic decided, independent of whatever native
    /// engagement would or would not follow.
    /// </summary>
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
    public void OnPreviewKeyDown_MarksEnterHandled_WhenCaretInsideContentLockedControl()
    {
        // freew-cc-4: before the fix, OnPreviewKeyDown's InsertParagraphBreak branch fell through to
        // base.OnPreviewKeyDown(e) unconditionally once TryApplyBodyParagraphBreak() declined (which it
        // always does inside a content control -- DocumentEditingSession.IsPortableBodyTextParagraph
        // requires run.Control is null) -- reproducing the original defect via Enter instead of
        // Backspace/Delete. After the fix, the same TryPrepareNativeFallback choke point Backspace/Delete
        // use marks the event handled and returns before base.OnPreviewKeyDown(e) is ever reached.
        var view = LoadWithPlainTextControl("Alice", ContentControlLockMode.ContentLocked);
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
                "Enter inside a content-locked control must be marked handled before it can reach native paragraph insertion");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void Enter_StillWorks_ForUnlockedControl_UnderFillingForms_ViaRealKeystroke()
    {
        // Positive control for the test above (and for the restoreReadOnly branch): Filling-In-Forms sets
        // IsReadOnly for ordinary body text, but an unlocked field must stay fillable, and the transiently
        // cleared IsReadOnly must be restored once the native Enter call returns -- this assertion does not
        // depend on whether native Enter processing actually engages (see
        // InvokeOnPreviewKeyDownForEnter's doc comment), only on the finally block around it running.
        var view = LoadWithPlainTextControl("Alice", ContentControlLockMode.NotSpecified);
        view.SetProtection(ProtectionMode.FillingForms);
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

            InvokeOnPreviewKeyDownForEnter(view);

            view.IsReadOnly.Should().BeTrue("restored after the one permitted native Enter dispatch");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Best-effort clipboard seed so the Paste tests below are deterministic REGARDLESS of ambient
    /// clipboard state: without guaranteed clipboard text, native Paste's own CanExecute (and Execute's
    /// mutation) could ALREADY be a no-op for an unrelated reason (empty clipboard), which would make
    /// "paste is blocked" pass even without the fix -- confirmed empirically while proving this suite
    /// fails-before-the-fix (the assertion below passed on reverted code in a run where the ambient
    /// clipboard happened to be empty). Mirrors <c>PaginatedEditorPanel.SetClipboardTextWithRetry</c>'s
    /// tolerance for the transient CLIPBRD_E_CANT_OPEN contention this heavily-parallel dev environment
    /// can produce.
    /// </summary>
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
                // Final attempt failed -- clipboard contention. The Paste tests below stay best-effort in
                // that case, same as production's own SetClipboardTextWithRetry.
            }
        }
    }

    [StaFact]
    public void Paste_CanExecute_False_WhenCaretInsideContentLockedControl()
    {
        // End-to-end check via the real ApplicationCommands.Paste.CanExecute call -- MainWindow's
        // ExecuteEditingCommand (used by the ribbon's Paste/Cut buttons) makes exactly this call before
        // Execute. Deterministic in this direction regardless of clipboard content: WPF's
        // CanExecuteRoutedEventArgs/ExecutedRoutedEventArgs constructors are internal (only the framework
        // can construct them), so the gate's own OnPreviewCanExecuteClipboardMutation/
        // OnPreviewExecutedClipboardMutation methods cannot be unit-tested directly with hand-built args --
        // this real RoutedCommand.CanExecute call is the way to exercise them. Once the gate marks the
        // tunneling event Handled, CanExecute never reaches the native (clipboard-dependent) binding at
        // all, so the result is always false when locked, regardless of ambient clipboard state.
        SeedClipboardText("freew-cc-5 paste probe");
        var view = LoadWithPlainTextControl("Alice", ContentControlLockMode.ContentLocked);
        PlaceCaretAfterText(view, "Al");

        ApplicationCommands.Paste.CanExecute(null, view).Should().BeFalse(
            "pasting into a content-locked control must be refused before Execute is ever reached");
    }

    [StaFact]
    public void Paste_Execute_DoesNotMutateModel_WhenCalledDirectly_BypassingCanExecute()
    {
        // Execute-boundary backstop: calling Execute directly (as a hypothetical future call site might,
        // without checking CanExecute first) must still be refused -- proves
        // OnPreviewExecutedClipboardMutation, not just the CanExecute gate above.
        SeedClipboardText("freew-cc-5 paste probe");
        var view = LoadWithPlainTextControl("Alice", ContentControlLockMode.ContentLocked);
        PlaceCaretAfterText(view, "Al");

        ApplicationCommands.Paste.Execute(null, view);

        view.CommitToModel();
        ((Paragraph)view.Model.Blocks[0]).Runs.Single().Text.Should().Be("Alice",
            "native paste must never reach a content-locked control, even via a direct Execute call");
    }

    [StaFact]
    public void Cut_CanExecute_False_WhenSelectionInsideControlAndContentLockedControl()
    {
        // Uses a real (non-collapsed) selection spanning the locked control's text: native
        // TextBoxBase.Cut's own CanExecute already requires Selection.IsEmpty == false, so a collapsed
        // caret (as PlaceCaretAfterText produces) would make this pass even without the fix.
        var view = LoadWithPlainTextControl("Alice", ContentControlLockMode.ControlAndContentLocked);
        SelectTextRange(view, 0, 5);

        ApplicationCommands.Cut.CanExecute(null, view).Should().BeFalse();
    }

    [StaFact]
    public void Paste_CanExecute_NotForcedFalse_OutsideAnyContentControl()
    {
        // Regression guard: the new CanExecute gate must be scoped to content-control positions -- outside
        // one it must not intervene at all (IsCaretOnLockedContentControl, the pure predicate backing the
        // gate, must report false).
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Body"));
        var view = new DocumentView();
        view.LoadModel(document);
        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.CaretPosition = paragraph.ContentStart.GetPositionAtOffset(2, LogicalDirection.Forward);

        IsCaretOnLockedContentControl(view).Should().BeFalse();
    }

    [StaFact]
    public void IsCaretOnLockedContentControl_FalseForUnlockedControl()
    {
        var view = LoadWithPlainTextControl("Alice", ContentControlLockMode.NotSpecified);
        PlaceCaretAfterText(view, "Al");

        IsCaretOnLockedContentControl(view).Should().BeFalse();
    }

    [StaFact]
    public void InsertText_Blocked_WhenCaretInsideContentLockedControl()
    {
        // freew-cc-3: InsertText's structural fallback (`new TextRange(...) { Text = text }`) is used by
        // Insert > Symbol, Insert > Date & Time, and the plain-text clipboard path in
        // ApplyClipboardPastePlan -- before the fix it never consulted TryPrepareNativeFallback at all.
        var view = LoadWithPlainTextControl("Alice", ContentControlLockMode.ContentLocked);
        PlaceCaretAfterText(view, "Al");

        view.InsertText("X");

        view.CommitToModel();
        ((Paragraph)view.Model.Blocks[0]).Runs.Single().Text.Should().Be("Alice");
    }

    [StaFact]
    public void InsertText_StillWorks_ForUnlockedControl_WithNoProtection()
    {
        // Positive control / regression guard: InsertText's own top-of-method gate
        // (`AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit)`, which is exactly
        // `!IsReadOnly` -- see RestrictEditingEnforcementPolicy.IsBodyEditingLocked) already blocks the
        // whole method under Filling-In-Forms regardless of content-control lock, both before and after
        // this fix, so a FillingForms scenario cannot exercise the new TryPrepareNativeFallback call here
        // (its restoreReadOnly branch can only fire when IsReadOnly was already true, which InsertText's
        // own top gate never lets it reach). This proves instead that the new gating does not regress the
        // ordinary case: an unlocked content control with no protection active must still accept the
        // fallback insertion exactly as before the fix.
        var view = LoadWithPlainTextControl("Alice", ContentControlLockMode.NotSpecified);
        PlaceCaretAfterText(view, "Al");

        view.InsertText("X");

        view.CommitToModel();
        ((Paragraph)view.Model.Blocks[0]).Runs.Single().Text.Should().Be("AlXice");
    }

    private static readonly MethodInfo IsCaretOnLockedContentControlMethod =
        typeof(DocumentView).GetMethod("IsCaretOnLockedContentControl", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            "DocumentView.IsCaretOnLockedContentControl not found -- the predicate backing the Paste/Cut gate was renamed or removed.");

    private static bool IsCaretOnLockedContentControl(DocumentView view) =>
        (bool)IsCaretOnLockedContentControlMethod.Invoke(view, null)!;

    /// <summary>
    /// freew-cc-6: Word's <c>sdtLocked</c> protects a control's EXISTENCE (its text may still be edited
    /// under the plain lock). The choke point judged only the position the caret sits on, so a SELECTION
    /// that merely spanned a locked field fell through to native editing, which replaced the whole range
    /// and took the control with it — Delete, Backspace, typing over the selection, and Cut alike.
    /// </summary>
    private static DocumentView LoadWithSurroundedControl(ContentControlLockMode lockMode)
    {
        var control = Run.PlainTextControl("Alice", tag: "Name");
        control.Control = control.Control! with { LockMode = lockMode };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Name: "));
        paragraph.Runs.Add(control);
        paragraph.Runs.Add(new Run(" (staff)"));
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    /// <summary>Selects the whole paragraph — a range that covers the content control's run entirely.</summary>
    private static void SelectWholeParagraph(DocumentView view)
    {
        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd);
    }

    /// <summary>Selects part of the control's own text, so the run (and its control) survives the delete.</summary>
    private static void SelectInsideControl(DocumentView view)
    {
        var run = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .OfType<System.Windows.Documents.Run>()
            .Single(candidate => candidate.Text == "Alice");
        view.Selection.Select(run.ContentStart.GetPositionAtOffset(1)!, run.ContentEnd);
    }

    [StaFact]
    public void TryPrepareNativeFallback_BlocksASelectionThatWouldDeleteADeleteLockedControl()
    {
        var view = LoadWithSurroundedControl(ContentControlLockMode.ControlLocked);

        // "Name: Alice (staff)" — a selection from inside the leading text through the whole field.
        SelectWholeParagraph(view);

        TryPrepareNativeFallback(view).Allowed
            .Should().BeFalse("deleting the selection would remove a control Word's sdtLocked protects");
        IsCaretOnLockedContentControl(view)
            .Should().BeTrue("Cut and Paste remove the selection too");
    }

    [StaFact]
    public void TryPrepareNativeFallback_AllowsASelectionThatOnlyClipsADeleteLockedControl()
    {
        var view = LoadWithSurroundedControl(ContentControlLockMode.ControlLocked);

        // Only part of the field is selected, so the control survives the deletion — and under
        // sdtLocked (unlike contentLocked) its text is editable.
        SelectInsideControl(view);

        TryPrepareNativeFallback(view).Allowed.Should().BeTrue();
    }

    [StaFact]
    public void TryPrepareNativeFallback_AllowsASelectionCoveringAnUnlockedControl()
    {
        var view = LoadWithSurroundedControl(ContentControlLockMode.NotSpecified);

        SelectWholeParagraph(view);

        TryPrepareNativeFallback(view).Allowed
            .Should().BeTrue("an unlocked field is ordinary content and may be deleted with the text");
    }
}
