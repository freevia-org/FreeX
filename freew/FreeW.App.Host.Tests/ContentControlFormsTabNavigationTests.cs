using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// keyboard-reach F1: Tab must move between form fields while "Filling in Forms" Restrict Editing
/// protection (<see cref="ProtectionMode.FillingForms"/>) is active in the WPF host, matching Word and
/// <c>FreeW.App.Avalonia.Editing.DocumentView</c>'s <c>TabToContentControl</c> (added there in commit
/// 707cb89f45). Before this fix, <c>DocumentView.OnPreviewKeyDown</c> had no branch for
/// <c>DocumentEditorInputIntent.NavigateTab</c>, so Tab always fell through to native
/// <c>RichTextBox</c> handling -- a no-op while <c>ApplyProtection</c> has set <c>IsReadOnly</c> true for
/// Filling-In-Forms protection (<c>AcceptsTab</c> keeps Tab from being treated as WPF's own focus
/// navigation) -- leaving a keyboard user stuck in the first field they could only reach with the mouse.
///
/// <para>
/// Follows <see cref="BlockContentControlKeyboardLockTests"/>'s approach: a real, reflection-invoked
/// <c>OnPreviewKeyDown</c> dispatch (with a shown window, so <c>PresentationSource.FromVisual</c>
/// resolves) proves the production call site actually reaches the fix for the plain-Tab/forward case,
/// while the private <c>TabToContentControl</c> choke point is invoked directly for the
/// backward/wrap-around cases -- <c>Keyboard.Modifiers</c> reflects real OS-level key state and cannot be
/// spoofed in a headless test host to exercise Shift+Tab through the routed-event path.
/// </para>
/// </summary>
public sealed class ContentControlFormsTabNavigationTests
{
    private static readonly MethodInfo OnPreviewKeyDownMethod =
        typeof(DocumentView).GetMethod("OnPreviewKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("DocumentView.OnPreviewKeyDown not found -- it was renamed or removed.");

    private static readonly MethodInfo TabToContentControlMethod =
        typeof(DocumentView).GetMethod("TabToContentControl", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            "DocumentView.TabToContentControl not found -- the Tab-between-fields choke point this test targets was renamed or removed.");

    /// <summary>
    /// Two Plain-Text form fields ("Name"/"Email") with a plain body paragraph between them, matching a
    /// typical Word form layout -- a Tab-stop walk must skip the non-field middle paragraph.
    /// </summary>
    private static DocumentView LoadWithTwoTextFields()
    {
        // Each field paragraph carries ONLY its content-control run (no label prefix run) so
        // PlaceCaretAfterText's offset-from-paragraph-start walk lands inside the field itself, matching
        // how ContentControlKeyboardLockTests/BlockContentControlKeyboardLockTests use that same helper.
        var first = new Paragraph();
        first.Runs.Add(Run.PlainTextControl("Alice", tag: "Name"));

        var middle = new Paragraph();
        middle.Runs.Add(new Run("Notes (not a field)"));

        var last = new Paragraph();
        last.Runs.Add(Run.PlainTextControl("a@b.com", tag: "Email"));

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(first);
        document.Blocks.Add(middle);
        document.Blocks.Add(last);

        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    /// <summary>Same run-walking approach as <c>ContentControlKeyboardLockTests.PlaceCaretAfterText</c>.</summary>
    private static void PlaceCaretAfterText(DocumentView view, string text, int paragraphIndex)
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

    private static Window ShowHosted(DocumentView view) => new()
    {
        WindowStyle = WindowStyle.None,
        ShowInTaskbar = false,
        Left = -10000,
        Top = -10000,
        Width = 300,
        Height = 300,
        Content = view,
    };

    private static bool InvokeOnPreviewKeyDownForTab(DocumentView view)
    {
        var source = PresentationSource.FromVisual(view)
            ?? throw new InvalidOperationException("A shown window is required to construct a KeyEventArgs.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Tab)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        OnPreviewKeyDownMethod.Invoke(view, [args]);
        return args.Handled;
    }

    private static bool InvokeTabToContentControl(DocumentView view, bool forward) =>
        (bool)TabToContentControlMethod.Invoke(view, [forward])!;

    [StaFact]
    public void OnPreviewKeyDown_Tab_UnderFillingForms_MovesToNextField_ViaRealDispatch()
    {
        var view = LoadWithTwoTextFields();
        var window = ShowHosted(view);
        try
        {
            window.Show();
            view.SetProtection(ProtectionMode.FillingForms);
            PlaceCaretAfterText(view, "Al", paragraphIndex: 0);

            InvokeOnPreviewKeyDownForTab(view).Should().BeTrue(
                "Tab must be consumed while Filling-In-Forms protection is active, not fall through to " +
                "native tab-character insertion (which is a no-op anyway while IsReadOnly is set)");

            view.Selection.Text.Should().Be("a@b.com",
                "Tab from inside the first field must move the caret to the next field and select its " +
                "content, matching Word's form-fill Tab and the Avalonia shell's TabToContentControl");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void TabToContentControl_Forward_WrapsFromLastFieldBackToFirst()
    {
        var view = LoadWithTwoTextFields();
        view.SetProtection(ProtectionMode.FillingForms);
        PlaceCaretAfterText(view, "a@b.com", paragraphIndex: 2);

        InvokeTabToContentControl(view, forward: true).Should().BeTrue();

        view.Selection.Text.Should().Be("Alice",
            "Tab past the last field must wrap around to the first field, as Word does");
    }

    [StaFact]
    public void TabToContentControl_Backward_MovesToPreviousField_AndWrapsFromFirst()
    {
        var view = LoadWithTwoTextFields();
        view.SetProtection(ProtectionMode.FillingForms);
        PlaceCaretAfterText(view, "a@b.com", paragraphIndex: 2);

        InvokeTabToContentControl(view, forward: false).Should().BeTrue();
        view.Selection.Text.Should().Be("Alice",
            "Shift+Tab from the last (second) field must move to the previous field");

        InvokeTabToContentControl(view, forward: false).Should().BeTrue();
        view.Selection.Text.Should().Be("a@b.com",
            "Shift+Tab past the first field must wrap around to the last field, as Word does");
    }

    /// <summary>
    /// No-regression guard: the new NavigateTab branch must only engage while Filling-In-Forms protection
    /// (<c>RestrictEditingPolicy.IsFormFieldEditingOnly</c>) is active. Without it (the document's default
    /// <see cref="ProtectionMode.None"/>), Tab from inside a content-control field must NOT jump the
    /// selection to the next field -- it must fall through to whatever native handling applied before this
    /// fix, exactly as before. (Asserting on native tab-character insertion itself is deliberately avoided
    /// here: <see cref="ContentControlKeyboardLockTests"/>'s class doc comment already established that
    /// native <c>RichTextBox</c> editing does not engage deterministically without real OS focus in this
    /// headless test host, so the reliable, fix-scoped observable is that our own guard condition keeps
    /// the selection untouched.)
    /// </summary>
    [StaFact]
    public void OnPreviewKeyDown_Tab_WithoutFillingFormsProtection_DoesNotJumpToNextField()
    {
        var view = LoadWithTwoTextFields();
        var window = ShowHosted(view);
        try
        {
            window.Show();
            PlaceCaretAfterText(view, "Al", paragraphIndex: 0);
            view.Selection.Text.Should().BeEmpty("the caret was placed, not a selection, before Tab");

            InvokeOnPreviewKeyDownForTab(view);

            view.Selection.Text.Should().BeEmpty(
                "outside Filling-In-Forms protection, Tab must not move the caret/selection to the next " +
                "content-control field the way it now does under that protection mode");
        }
        finally
        {
            window.Close();
        }
    }
}
