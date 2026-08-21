using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using FreeW.App.Host.Editing;
using Xunit;
using WpfRun = System.Windows.Documents.Run;

namespace FreeW.App.Host.Tests;

/// <summary>
/// F3: real Word selects the entire placeholder run the instant a plain-text/rich-text content control
/// showing w:showingPlcHdr is entered by click, so the first keystroke replaces the placeholder instead
/// of being spliced into the middle of its wording. Before the fix, <c>ApplyContentControlMarker</c>'s
/// kind switch wired a click handler only for CheckBox/DropDownList/ComboBox/DatePicker, leaving a
/// plain-text/rich-text field's native RichTextBox caret placement in effect. Runs on an STA thread
/// (<c>[StaFact]</c>, via Xunit.StaFact) because the RichTextBox/FlowDocument need STA. Mirrors
/// <see cref="CheckBoxContentControlTests"/>'s real-click pattern and the Avalonia-side coverage in
/// DocumentViewContentControlKeyboardTests (Clicking_into_a_placeholder_showing_field_selects_the_whole_placeholder).
/// </summary>
public sealed class ContentControlPlaceholderClickSelectionTests
{
    private const string PlaceholderText = "Click to enter text";

    // A real left-button-up on the field run, exactly the event OnPlaceholderControlClicked is wired to
    // via `wpf.MouseLeftButtonUp += OnPlaceholderControlClicked` -- not a direct call to Selection.Select.
    private static void ClickRun(WpfRun wpf) =>
        wpf.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent
        });

    private static WpfRun SingleFieldRun(DocumentView view) =>
        view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(p => p.Inlines)
            .OfType<WpfRun>()
            .Single();

    [StaFact]
    public void ClickingAPlaceholderShowingPlainTextField_SelectsTheWholeRun()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run(PlaceholderText)
        {
            Control = new ContentControl(
                ContentControlKind.PlainText,
                WordMetadata: new ContentControlWordMetadata(ShowingPlaceholder: true))
        });
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        ClickRun(SingleFieldRun(view));

        view.Selection.Text.Should().Be(PlaceholderText,
            "clicking a placeholder-showing field must select its whole run, matching Word and this " +
            "shell's own TabToContentControl, so the first keystroke replaces the placeholder instead of " +
            "landing mid-string");
    }

    /// <summary>
    /// Sibling no-regression coverage: a field that is NOT showing its placeholder (i.e. already filled
    /// in) must keep ordinary native caret placement -- ApplyContentControlMarker must not wire the
    /// select-all click handler for it at all, since select-all on every click into filled content would
    /// make it impossible to position the caret for a normal in-place edit.
    /// </summary>
    [StaFact]
    public void ClickingAnAlreadyFilledPlainTextField_DoesNotSelectTheWholeRun()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(Run.PlainTextControl(PlaceholderText)); // no WordMetadata -> not a placeholder
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        ClickRun(SingleFieldRun(view));

        view.Selection.Text.Should().BeEmpty(
            "an already-filled field must not wire the select-all click handler -- only a " +
            "placeholder-showing field does");
    }
}
