using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Host;

public sealed class ScreenTipDialog : TextEntryDialog
{
    public ScreenTipDialog(string? initialText = "")
        : base(
            UiText.Get("Hyperlink_SetHyperlinkScreenTipTitle"),
            UiText.Get("Hyperlink_ScreenTipTextLabel"),
            initialText,
            "SetHyperlinkScreenTipTextBox",
            UiText.Get("Hyperlink_ScreenTipTextAutomationName"),
            UiText.Get("Hyperlink_ScreenTipTextHelpText"))
    {
    }
}

public sealed class BookmarkDialog : TextEntryDialog
{
    public BookmarkDialog(string? initialText = "")
        : base(
            UiText.Get("Hyperlink_SelectPlaceInDocument"),
            UiText.Get("Hyperlink_BookmarkOrCellReferenceLabel"),
            initialText,
            "SelectPlaceinDocumentTextBox",
            UiText.Get("Hyperlink_BookmarkOrCellReferenceAutomationName"),
            UiText.Get("Hyperlink_BookmarkOrCellReferenceHelpText"))
    {
    }
}

public class TextEntryDialog : Window
{
    private readonly TextBox _textBox = new();

    public TextEntryDialogResult Result { get; private set; }

    public TextEntryDialog(
        string title,
        string label,
        string? initialText = "",
        string? automationId = null,
        string? automationName = null,
        string? helpText = null)
    {
        Result = CreateResult(initialText);
        Title = title;
        Width = 420;
        Height = 170;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _textBox.Text = initialText ?? "";
        AutomationProperties.SetName(_textBox, automationName ?? CreateAutomationName(label));
        AutomationProperties.SetAutomationId(_textBox, automationId ?? CreateAutomationId(title));
        AutomationProperties.SetHelpText(_textBox, helpText ?? CreateHelpText(label));
        Content = ObjectSizeDialog.CreateSingleInputContent(label, _textBox, () =>
        {
            Result = CreateResult(_textBox.Text);
            DialogResult = true;
        });
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static TextEntryDialogResult CreateResult(string? text) =>
        TextEntryDialogPlanner.CreateResult(text);

    private static string CreateAutomationName(string label) =>
        label.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static string CreateAutomationId(string title) =>
        string.Concat(title.Where(char.IsLetterOrDigit)) + "TextBox";

    private static string CreateHelpText(string label) =>
        UiText.Format("TextEntry_EnterValueHelpTextFormat", CreateAutomationName(label));

    private void FocusInitialKeyboardTarget()
    {
        DialogFocus.FocusAndSelect(_textBox);
    }
}
