using System.Reflection;
using System.Windows.Documents;
using System.Windows.Input;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for freew-autocorrect-bypasses-restrict-editing-wpf: <c>DocumentView.TryAutoCorrect</c>
/// mutates the live FlowDocument directly (a raw <see cref="TextRange"/> assignment), completely outside
/// the portable body-edit session's lock checks. <see cref="DocumentView.OnPreviewTextInput"/> used to call
/// it BEFORE either the Restrict Editing gate (<c>TryApplyBodyTextInput</c>'s
/// <c>AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit)</c> check) or the
/// content-control lock gate (<c>TryPrepareNativeFallback</c>) ever ran, so a keystroke that happened to
/// trigger AutoCorrect (day-name capitalization, smart quotes, "--" to em dash, etc.) silently mutated text
/// protected by "Restrict Editing" or a locked content control, while the identical keystroke with no
/// AutoCorrect trigger was correctly blocked. Runs on STA (<c>[StaFact]</c>, via Xunit.StaFact) because the
/// RichTextBox/FlowDocument need it.
/// </summary>
public sealed class AutoCorrectRestrictEditingLockTests
{
    private static readonly MethodInfo TryAutoCorrectMethod =
        typeof(DocumentView).GetMethod("TryAutoCorrect", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            "DocumentView.TryAutoCorrect not found -- the method this test targets was renamed or removed.");

    private static bool InvokeTryAutoCorrect(DocumentView view, char justTyped) =>
        (bool)TryAutoCorrectMethod.Invoke(view, [justTyped])!;

    /// <summary>
    /// Raises a real <see cref="TextCompositionManager.PreviewTextInputEvent"/> at <paramref name="view"/>
    /// via <see cref="TextCompositionManager.StartComposition"/> -- the same staging WPF uses for a typed
    /// character -- so the test exercises <c>DocumentView.OnPreviewTextInput</c> itself (the real
    /// production entry point), not a test-only shortcut.
    /// </summary>
    private static void TypeCharacter(DocumentView view, string text) =>
        TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, view, text));

    private static DocumentView LoadWithText(string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        var view = new DocumentView
        {
            AutoCorrectOptions = AutoCorrectOptions.Default,
            // Isolate the AutoCorrect-tab day-name rule under test from AutoFormat's own rules (e.g.
            // sentence capitalization), matching the isolation idiom used by AutoCorrectAsYouTypeTests.
            AutoFormatOptions = AutoFormatOptions.AllOff,
        };
        view.LoadModel(doc);
        var paragraph = view.Document.Blocks.OfType<WpfParagraph>().Single();
        var end = paragraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward) ?? paragraph.ContentEnd;
        view.CaretPosition = end;
        view.Selection.Select(end, end);
        return view;
    }

    private static DocumentView LoadWithLockedContentControl(string text, ContentControlLockMode lockMode)
    {
        var run = Run.PlainTextControl(text, tag: "Day");
        run.Control = run.Control! with { LockMode = lockMode };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(run);
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(paragraph);

        var view = new DocumentView
        {
            AutoCorrectOptions = AutoCorrectOptions.Default,
            AutoFormatOptions = AutoFormatOptions.AllOff,
        };
        view.LoadModel(doc);

        var wpfParagraph = view.Document.Blocks.OfType<WpfParagraph>().Single();
        var end = wpfParagraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward) ?? wpfParagraph.ContentEnd;
        view.CaretPosition = end;
        view.Selection.Select(end, end);
        return view;
    }

    private static string PlainText(DocumentView view)
    {
        view.CommitToModel();
        return view.Model.Paragraphs.First().PlainText;
    }

    [StaFact]
    public void AutoCorrect_RespectsRestrictEditingReadOnlyProtection_ViaRealKeystroke()
    {
        // "No changes (Read only)" Restrict Editing: ordinary body typing is blocked via
        // AllowsRestrictEditingOperation(BodyTextEdit). Before the fix, TryAutoCorrect ignored this
        // entirely and mutated the FlowDocument directly.
        var view = LoadWithText("I saw her on monday");
        view.SetProtection(ProtectionMode.ReadOnly);
        view.IsReadOnly.Should().BeTrue("Restrict Editing -> No changes (Read only) is active");

        // Re-resolve the caret after Render() (SetProtection re-renders the FlowDocument, invalidating the
        // pointer captured during LoadWithText).
        var paragraph = view.Document.Blocks.OfType<WpfParagraph>().Single();
        var end = paragraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward) ?? paragraph.ContentEnd;
        view.CaretPosition = end;
        view.Selection.Select(end, end);

        // The day-name rule fires on a trailing space after "monday".
        TypeCharacter(view, " ");

        PlainText(view).Should().Be("I saw her on monday",
            "AutoCorrect must not mutate text while Restrict Editing blocks ordinary body edits");
    }

    [StaFact]
    public void AutoCorrect_RespectsLockedContentControl_DirectInvocation()
    {
        // A run-level content control locked the ordinary way Word does it (w:lock="contentLocked"). No
        // document-wide Restrict Editing is active (IsReadOnly stays false), exactly the scenario where
        // TryAutoCorrect's only guard used to be "is there a paragraph at all", never the per-control lock.
        var view = LoadWithLockedContentControl("monday", ContentControlLockMode.ContentLocked);
        view.IsReadOnly.Should().BeFalse("no document-level protection is active");

        var applied = InvokeTryAutoCorrect(view, ' ');

        applied.Should().BeFalse("a content-locked control must never be mutated by AutoCorrect");
        PlainText(view).Should().Be("monday");
    }

    [StaFact]
    public void AutoCorrect_StillAppliesWhenUnprotected_ViaRealKeystroke()
    {
        // Sibling/regression check: the exact same keystroke, with no protection and no content-control
        // lock, must still trigger AutoCorrect as before -- the fix must not over-block ordinary editing.
        var view = LoadWithText("I saw her on monday");
        view.IsReadOnly.Should().BeFalse();

        TypeCharacter(view, " ");

        PlainText(view).Should().Be("I saw her on Monday ",
            "AutoCorrect must still fire normally when nothing blocks body text editing");
    }
}
