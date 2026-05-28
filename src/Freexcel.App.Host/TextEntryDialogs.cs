using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public sealed class ScreenTipDialog : TextEntryDialog
{
    public ScreenTipDialog(string? initialText = "")
        : base("Set Hyperlink ScreenTip", "_ScreenTip text:", initialText)
    {
    }
}

public sealed class BookmarkDialog : TextEntryDialog
{
    public BookmarkDialog(string? initialText = "")
        : base("Select Place in Document", "_Bookmark or cell reference:", initialText)
    {
    }
}

public sealed record TextEntryDialogResult(string Text);

public class TextEntryDialog : Window
{
    private readonly TextBox _textBox = new();

    public TextEntryDialogResult Result { get; private set; }

    public TextEntryDialog(string title, string label, string? initialText = "")
    {
        Result = CreateResult(initialText);
        Title = title;
        Width = 420;
        Height = 170;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _textBox.Text = initialText ?? "";
        AutomationProperties.SetName(_textBox, CreateAutomationName(label));
        AutomationProperties.SetAutomationId(_textBox, CreateAutomationId(title));
        AutomationProperties.SetHelpText(_textBox, CreateHelpText(label));
        Content = ObjectSizeDialog.CreateSingleInputContent(label, _textBox, () =>
        {
            Result = CreateResult(_textBox.Text);
            DialogResult = true;
        });
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static TextEntryDialogResult CreateResult(string? text) => new((text ?? "").Trim());

    private static string CreateAutomationName(string label) =>
        label.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static string CreateAutomationId(string title) =>
        string.Concat(title.Where(char.IsLetterOrDigit)) + "TextBox";

    private static string CreateHelpText(string label) =>
        $"Enter {CreateAutomationName(label).ToLowerInvariant()}.";

    private void FocusInitialKeyboardTarget()
    {
        DialogFocus.FocusAndSelect(_textBox);
    }
}
