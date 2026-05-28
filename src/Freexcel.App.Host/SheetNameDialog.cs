using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public sealed record SheetNameDialogResult(string SheetName);

public sealed class SheetNameDialog : Window
{
    private static readonly char[] InvalidSheetNameChars = [':', '\\', '/', '?', '*', '[', ']'];

    private readonly TextBox _nameBox = new();

    public SheetNameDialogResult Result { get; private set; }

    public SheetNameDialog(string currentName)
    {
        Result = CreateResult(currentName);
        Title = "Rename Sheet";
        Width = 340;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _nameBox.Text = currentName;
        AutomationProperties.SetName(_nameBox, "Sheet name");
        Content = ObjectSizeDialog.CreateSingleInputContent("Sheet _name:", _nameBox, Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static SheetNameDialogResult CreateResult(string sheetName) => new(sheetName.Trim());

    public static bool TryCreateResult(string? sheetName, out SheetNameDialogResult result, out string? error)
    {
        result = CreateResult(sheetName ?? "");
        if (string.IsNullOrWhiteSpace(result.SheetName))
        {
            error = "Sheet name is invalid: it cannot be blank.";
            return false;
        }

        if (result.SheetName.Length > 31)
        {
            error = "Sheet name is invalid: it cannot exceed 31 characters.";
            return false;
        }

        if (result.SheetName.IndexOfAny(InvalidSheetNameChars) >= 0)
        {
            error = "Sheet name is invalid: it cannot contain : \\ / ? * [ or ].";
            return false;
        }

        if (result.SheetName.StartsWith('\'') || result.SheetName.EndsWith('\''))
        {
            error = "Sheet name is invalid: it cannot begin or end with an apostrophe.";
            return false;
        }

        error = null;
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_nameBox.Text, out var result, out var error))
        {
            ShowInvalidInputWarning(error ?? "Enter a valid sheet name.");
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void ShowInvalidInputWarning(string message)
    {
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        DialogFocus.FocusAndSelect(_nameBox);
    }

    private void FocusInitialKeyboardTarget()
    {
        DialogFocus.FocusAndSelect(_nameBox);
    }
}
