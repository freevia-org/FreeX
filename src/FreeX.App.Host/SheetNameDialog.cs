using FreeX.Core.Model;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

public sealed record SheetNameDialogResult(string SheetName);

public sealed class SheetNameDialog : Window
{
    private readonly TextBox _nameBox = new();

    public SheetNameDialogResult Result { get; private set; }

    public SheetNameDialog(string currentName)
    {
        Result = CreateResult(currentName);
        Title = UiText.Get("SheetName_RenameSheet");
        Width = 340;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _nameBox.Text = currentName;
        AutomationProperties.SetName(_nameBox, UiText.Get("SheetName_SheetName"));
        AutomationProperties.SetAutomationId(_nameBox, "SheetNameBox");
        AutomationProperties.SetHelpText(_nameBox, UiText.Get("SheetName_EnterAWorksheetNameUpTo31Characters"));
        Content = ObjectSizeDialog.CreateSingleInputContent(UiText.Get("SheetName_SheetName2"), _nameBox, Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static SheetNameDialogResult CreateResult(string sheetName) => new(sheetName.Trim());

    public static bool TryCreateResult(string? sheetName, out SheetNameDialogResult result, out string? error)
    {
        result = CreateResult(sheetName ?? "");
        if (string.IsNullOrWhiteSpace(result.SheetName))
        {
            error = UiText.Get("SheetName_InvalidBlank");
            return false;
        }

        if (result.SheetName.Length > 31)
        {
            error = UiText.Get("SheetName_InvalidTooLong");
            return false;
        }

        if (Workbook.ContainsInvalidSheetNameCharacter(result.SheetName))
        {
            error = UiText.Get("SheetName_InvalidCharacters");
            return false;
        }

        if (result.SheetName.StartsWith('\'') || result.SheetName.EndsWith('\''))
        {
            error = UiText.Get("SheetName_InvalidApostrophe");
            return false;
        }

        error = null;
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_nameBox.Text, out var result, out var error))
        {
            ShowInvalidInputWarning(error ?? UiText.Get("SheetName_EnterValidSheetName"));
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void ShowInvalidInputWarning(string message)
    {
        DialogMessageHelper.ShowWarning(this, message, Title);
        _nameBox.Focus();
        _nameBox.SelectAll();
        Keyboard.Focus(_nameBox);
    }

    private void FocusInitialKeyboardTarget()
    {
        DialogFocus.FocusAndSelect(_nameBox);
    }
}
