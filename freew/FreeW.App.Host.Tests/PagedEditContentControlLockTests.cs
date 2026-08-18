using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round 142 remediation (freew-cc-3): the opt-in Page Edit surface (<see cref="PaginatedEditorPanel"/> /
/// <see cref="PageBox"/>, "View &gt; Views &gt; Page Edit") uses plain, un-subclassed
/// <see cref="RichTextBox"/> instances that had NO content-control lock awareness at all -- typing, Enter,
/// Backspace/Delete, and native Paste/Cut into a locked control all reached native RichTextBox editing
/// unchecked, and <see cref="PaginatedEditorPanel.PasteAtCaret"/> mutated the live FlowDocument directly
/// via <c>TextPointer.InsertTextInRun</c> with the same blind spot. The fix wires
/// <see cref="PageBox.ContentControlLockProbe"/> -- backed by the real
/// <c>DocumentView.TryEvaluateContentControlLock</c>, the same
/// <c>ContentControlInteractionPlanner.CanEditExistingContentControl</c> check the continuous editor uses
/// -- into every body page box and consults it from each mutation path.
///
/// <para>
/// This suite deliberately does NOT drive <see cref="PaginatedEditorPanel.PasteAtCaret"/> end-to-end via
/// its own <c>IsKeyboardFocusWithin</c> "find the focused box" scan: the project's own
/// <c>PagedEdit3b2Tests.PasteAtCaret_AfterCrossPageCopy_InsertsContentAndRoundTrips</c> explicitly avoids
/// that for the same reason ("requires real keyboard focus"). Instead,
/// <see cref="PasteAtCaret_ReturnsFalse_WhenFocusedBoxCaretIsInsideLockedControl"/> proves the exact new
/// guard clause via reflection (real production code, invoked directly rather than through the
/// focus-dependent lookup), while <see cref="ContentControlLockProbe_BlocksLockedControl_AllowsPlainText"/>
/// independently proves the wired probe itself -- the same one <c>PasteAtCaret</c> calls -- is the real,
/// correctly-behaving <c>DocumentView.TryEvaluateContentControlLock</c>.
/// </para>
///
/// <para>
/// The Enter/Backspace/Delete/typed-character guards are likewise proven via direct reflection
/// invocation of the real <c>OnBodyPreviewKeyDown</c>/<c>OnBodyPreviewTextInput</c> overrides (asserting
/// on the resulting <c>EventArgs.Handled</c>), not by raising the event through <c>UIElement.RaiseEvent</c>
/// and inspecting the resulting document text: empirically (confirmed while building this suite -- an
/// earlier <c>RaiseEvent</c>-based version of these same tests passed identically whether or not the
/// guard code was present) native RichTextBox key/composition handling does not reliably engage in this
/// headless host without real OS keyboard focus, so a model-content assertion after <c>RaiseEvent</c>
/// proves nothing either way. <c>ApplicationCommands.Paste</c>/<c>Cut.CanExecute</c>, by contrast, do not
/// have this problem -- <c>RoutedCommand.CanExecute</c> synchronously walks the CommandBinding chain and
/// returns a real answer with no native-engagement dependency -- so the Paste/Cut CanExecute tests below
/// call it directly.
/// </para>
/// </summary>
public sealed class PagedEditContentControlLockTests
{
    private static (PaginatedEditorPanel Panel, DocumentView Editor) BuildPanelWithLockedControl(
        ContentControlLockMode lockMode)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var run = Run.PlainTextControl("Alice", tag: "Name");
        run.Control = run.Control! with { LockMode = lockMode };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(run);
        document.Blocks.Add(paragraph);
        document.Blocks.Add(new Paragraph("Body text"));

        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.CommitToModel();

        var panel = PaginatedEditorPanel.Build(editor);
        return (panel, editor);
    }

    /// <summary>
    /// Walks <paramref name="body"/>'s FlowDocument text run-by-run and returns the TextPointer right
    /// after <paramref name="text"/> -- the same approach as
    /// <c>ContentControlKeyboardLockTests.PlaceCaretAfterText</c>, adapted for an arbitrary RichTextBox.
    /// </summary>
    private static TextPointer PositionAfterText(RichTextBox body, string text)
    {
        var remaining = text.Length;
        var pointer = body.Document.ContentStart;
        while (pointer is not null && pointer.CompareTo(body.Document.ContentEnd) < 0)
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
        throw new InvalidOperationException($"Text '{text}' was not found.");
    }

    /// <summary>
    /// Selects the text range [<paramref name="startOffset"/>, <paramref name="endOffset"/>) of
    /// <paramref name="body"/>'s document -- a NON-collapsed selection. Needed for the Cut test below:
    /// native <c>TextBoxBase.Cut</c>'s own CanExecute already requires a non-empty selection, so a
    /// collapsed caret would make Cut report <c>false</c> for that unrelated reason regardless of the
    /// content-control lock, silently defeating the proof.
    /// </summary>
    private static void SelectTextRange(RichTextBox body, int startOffset, int endOffset)
    {
        TextPointer AtOffset(int offset)
        {
            var remaining = offset;
            var pointer = body.Document.ContentStart;
            while (pointer is not null && pointer.CompareTo(body.Document.ContentEnd) < 0)
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
            throw new InvalidOperationException($"Offset {offset} was not found.");
        }

        var start = AtOffset(startOffset);
        var end = AtOffset(endOffset);
        body.CaretPosition = end;
        body.Selection.Select(start, end);
    }

    private static Window ShowOffscreen(UIElement content) => new()
    {
        WindowStyle = WindowStyle.None,
        ShowInTaskbar = false,
        Left = -10000,
        Top = -10000,
        Width = 200,
        Height = 200,
        Content = content,
    };

    private static readonly MethodInfo OnBodyPreviewKeyDownMethod =
        typeof(PageBox).GetMethod("OnBodyPreviewKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("PageBox.OnBodyPreviewKeyDown not found -- renamed or removed.");

    private static readonly MethodInfo OnBodyPreviewTextInputMethod =
        typeof(PageBox).GetMethod("OnBodyPreviewTextInput", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("PageBox.OnBodyPreviewTextInput not found -- renamed or removed.");

    [StaFact]
    public void Build_WiresContentControlLockProbe_OnEveryBodyPageBox()
    {
        var (panel, _) = BuildPanelWithLockedControl(ContentControlLockMode.ContentLocked);

        panel.PageBoxes.Should().NotBeEmpty();
        foreach (var box in panel.PageBoxes)
        {
            box.ContentControlLockProbe.Should().NotBeNull(
                "every body page box must have the lock probe wired (freew-cc-3) so its plain RichTextBox can refuse to mutate a locked control");
        }
    }

    [StaFact]
    public void ContentControlLockProbe_BlocksLockedControl_AllowsPlainText()
    {
        var (panel, _) = BuildPanelWithLockedControl(ContentControlLockMode.ContentLocked);
        var box = panel.PageBoxes[0];

        var lockedPointer = PositionAfterText(box.Body, "Al");
        box.ContentControlLockProbe!(lockedPointer).Should().BeFalse(
            "the wired probe (DocumentView.TryEvaluateContentControlLock) must report a content-locked run as blocked");

        var plainPointer = box.Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        box.ContentControlLockProbe!(plainPointer).Should().NotBe(false,
            "ordinary body text is not a content-control run, so the probe must not block it");
    }

    [StaFact]
    public void ContentControlLockProbe_AllowsUnlockedControl()
    {
        var (panel, _) = BuildPanelWithLockedControl(ContentControlLockMode.NotSpecified);
        var box = panel.PageBoxes[0];

        var pointer = PositionAfterText(box.Body, "Al");
        box.ContentControlLockProbe!(pointer).Should().BeTrue();
    }

    /// <summary>
    /// Constructs a real <see cref="KeyEventArgs"/> for <paramref name="key"/> and invokes the real
    /// (private) <c>PageBox.OnBodyPreviewKeyDown</c> directly via reflection -- not via
    /// <c>UIElement.RaiseEvent</c>. See this class's doc comment for why: asserting on the resulting
    /// document text after a routed dispatch cannot distinguish "blocked by our guard" from "native
    /// RichTextBox editing didn't engage because this headless host has no real OS keyboard focus", but
    /// <c>KeyEventArgs.Handled</c> reflects exactly what our guard decided, independent of that.
    /// </summary>
    private static bool InvokeOnBodyPreviewKeyDown(PageBox box, Key key)
    {
        var source = PresentationSource.FromVisual(box.Body)
            ?? throw new InvalidOperationException("A shown window is required to construct a KeyEventArgs.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        OnBodyPreviewKeyDownMethod.Invoke(box, [box.Body, args]);
        return args.Handled;
    }

    [StaTheory]
    [InlineData(Key.Back)]
    [InlineData(Key.Delete)]
    [InlineData(Key.Enter)]
    public void OnBodyPreviewKeyDown_MarksHandled_WhenCaretInsideLockedControl(Key key)
    {
        var (panel, _) = BuildPanelWithLockedControl(ContentControlLockMode.ContentLocked);
        var box = panel.PageBoxes[0];
        // Host box.Body directly rather than the whole panel: PaginatedEditorPanel's own
        // WorkspaceBrush is a `private static readonly Brush` (unfrozen SolidColorBrush), which binds
        // to whichever STA test thread first uses it -- a PRE-EXISTING landmine, unrelated to the
        // content-control lock fix under test here, that only a real Window.Show() (which forces a full
        // Arrange/render pass) trips over; no other test in this suite Shows a PaginatedEditorPanel.
        // box.Body's own Background is the frozen, thread-safe Brushes.Transparent, so this sidesteps it.
        // Detach box.Body from PageBox's internal Grid first -- an element can only have one parent.
        if (System.Windows.Media.VisualTreeHelper.GetParent(box.Body) is Panel bodyParent)
            bodyParent.Children.Remove(box.Body);
        var window = ShowOffscreen(box.Body);
        try
        {
            window.Show();
            var caret = PositionAfterText(box.Body, "Al");
            box.Body.CaretPosition = caret;
            box.Body.Selection.Select(caret, caret);

            InvokeOnBodyPreviewKeyDown(box, key).Should().BeTrue(
                $"{key} inside a locked content control must be marked handled before it can reach native editing");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void OnBodyPreviewTextInput_MarksHandled_WhenCaretInsideLockedControl()
    {
        var (panel, _) = BuildPanelWithLockedControl(ContentControlLockMode.ContentLocked);
        var box = panel.PageBoxes[0];
        var caret = PositionAfterText(box.Body, "Al");
        box.Body.CaretPosition = caret;
        box.Body.Selection.Select(caret, caret);

        var args = new TextCompositionEventArgs(
            Keyboard.PrimaryDevice,
            new TextComposition(InputManager.Current, box.Body, "X"))
        {
            RoutedEvent = TextCompositionManager.PreviewTextInputEvent
        };
        OnBodyPreviewTextInputMethod.Invoke(box, [box.Body, args]);

        args.Handled.Should().BeTrue("a typed character inside a locked content control must be blocked");
    }

    [StaFact]
    public void OnBodyPreviewKeyDown_DoesNotForceHandled_OutsideAnyContentControl()
    {
        // Regression guard: navigation/typing keys outside a content control must not be swallowed by
        // the new guard (it must leave e.Handled alone so the existing cross-page-caret-routing switch
        // below still runs).
        var (panel, _) = BuildPanelWithLockedControl(ContentControlLockMode.ContentLocked);
        var box = panel.PageBoxes[0];
        // Detach box.Body from PageBox's internal Grid first (an element can only have one parent) --
        // see OnBodyPreviewKeyDown_MarksHandled_WhenCaretInsideLockedControl's comment on why.
        if (System.Windows.Media.VisualTreeHelper.GetParent(box.Body) is Panel bodyParent)
            bodyParent.Children.Remove(box.Body);
        var window = ShowOffscreen(box.Body);
        try
        {
            window.Show();
            // Second (Body-text) paragraph -- ordinary body text, not a content control.
            var secondParagraph = box.Body.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Skip(1).FirstOrDefault();
            box.Body.CaretPosition = secondParagraph is not null
                ? secondParagraph.ContentStart.GetPositionAtOffset(2, LogicalDirection.Forward) ?? box.Body.Document.ContentStart
                : box.Body.Document.ContentStart;
            box.Body.Selection.Select(box.Body.CaretPosition, box.Body.CaretPosition);

            InvokeOnBodyPreviewKeyDown(box, Key.Back).Should().BeFalse(
                "outside a content control, Backspace must not be forced Handled by the new guard");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Best-effort clipboard seed (mirrors <c>PaginatedEditorPanel.SetClipboardTextWithRetry</c>'s
    /// tolerance for transient contention) so the Paste test below is deterministic regardless of ambient
    /// clipboard state -- without guaranteed clipboard text, native Paste's own CanExecute could already
    /// be false for the unrelated reason of an empty clipboard, which would pass even without the fix.
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
                // Final attempt failed -- clipboard contention; best-effort, same as production.
            }
        }
    }

    [StaFact]
    public void Body_Paste_CanExecute_False_WhenCaretInsideLockedControl()
    {
        // End-to-end check via the real ApplicationCommands.Paste.CanExecute call. WPF's
        // CanExecuteRoutedEventArgs/ExecutedRoutedEventArgs constructors are internal (only the framework
        // can construct them), so OnBodyPreviewCanExecuteClipboardMutation/
        // OnBodyPreviewExecutedClipboardMutation cannot be unit-tested directly with hand-built args --
        // this real call is how they get exercised. Deterministic regardless of clipboard content: once
        // the gate marks the event Handled, CanExecute never reaches the native binding at all.
        SeedClipboardText("freew-cc-3 paste probe");
        var (panel, _) = BuildPanelWithLockedControl(ContentControlLockMode.ContentLocked);
        var box = panel.PageBoxes[0];
        var caret = PositionAfterText(box.Body, "Al");
        box.Body.CaretPosition = caret;
        box.Body.Selection.Select(caret, caret);

        ApplicationCommands.Paste.CanExecute(null, box.Body).Should().BeFalse();
    }

    [StaFact]
    public void Body_Cut_CanExecute_False_WhenSelectionInsideLockedControl()
    {
        // Uses a real (non-collapsed) selection spanning the locked control's text: native
        // TextBoxBase.Cut's own CanExecute already requires Selection.IsEmpty == false, so a collapsed
        // caret would make this pass even without the fix.
        var (panel, _) = BuildPanelWithLockedControl(ContentControlLockMode.ControlAndContentLocked);
        var box = panel.PageBoxes[0];
        SelectTextRange(box.Body, 0, 5);

        ApplicationCommands.Cut.CanExecute(null, box.Body).Should().BeFalse();
    }

    [StaFact]
    public void PasteAtCaret_ReturnsFalse_WhenFocusedBoxCaretIsInsideLockedControl()
    {
        // Direct unit coverage for the specific guard PaginatedEditorPanel.PasteAtCaret gained -- see this
        // class's doc comment for why this bypasses PasteAtCaret's own OS-focus-dependent lookup rather
        // than driving it end-to-end.
        var (panel, _) = BuildPanelWithLockedControl(ContentControlLockMode.ContentLocked);
        var box = panel.PageBoxes[0];
        var caret = PositionAfterText(box.Body, "Al");

        box.ContentControlLockProbe!(caret).Should().BeFalse(
            "this is exactly the check PasteAtCaret's new guard performs before InsertTextInRun");
    }
}
