using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

public sealed record UnhideSheetDialogResult(string SheetName);

public sealed class UnhideSheetDialog : Window
{
    private readonly ListBox _sheetBox = new();
    private readonly Button _okButton = new() { Content = "_OK", Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
    private readonly Button _cancelButton = new() { Content = "_Cancel", Width = 72, IsCancel = true };

    public UnhideSheetDialogResult Result { get; private set; }

    public UnhideSheetDialog(IEnumerable<string> hiddenSheetNames)
    {
        var names = hiddenSheetNames.ToList();
        var selected = names.FirstOrDefault() ?? "";
        Result = CreateResult(selected);
        Title = "Unhide Sheet";
        Width = 340;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _sheetBox.ItemsSource = names;
        _sheetBox.SelectedItem = selected;
        _sheetBox.SelectionMode = SelectionMode.Single;
        AutomationProperties.SetName(_sheetBox, "Unhide sheet");
        AutomationProperties.SetAutomationId(_sheetBox, "UnhideSheetList");
        AutomationProperties.SetHelpText(_sheetBox, "Select the hidden worksheet to make visible.");
        _sheetBox.SelectionChanged += (_, _) => UpdateButtonState();
        _sheetBox.MouseDoubleClick += (_, _) => Accept();

        AutomationProperties.SetName(_okButton, "OK");
        AutomationProperties.SetAutomationId(_okButton, "UnhideSheetOkButton");
        AutomationProperties.SetHelpText(_okButton, "Unhide the selected worksheet.");
        _okButton.Click += (_, _) => Accept();
        AutomationProperties.SetName(_cancelButton, "Cancel");
        AutomationProperties.SetAutomationId(_cancelButton, "UnhideSheetCancelButton");
        AutomationProperties.SetHelpText(_cancelButton, "Close the Unhide Sheet dialog without changing worksheet visibility.");

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = "_Unhide sheet:", Target = _sheetBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        _sheetBox.Margin = new Thickness(0, 0, 0, 12);
        _sheetBox.MinHeight = 64;
        stack.Children.Add(_sheetBox);
        stack.Children.Add(CreateButtonRow());
        Content = stack;
        UpdateButtonState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static UnhideSheetDialogResult CreateResult(string sheetName) => new(sheetName.Trim());

    private void FocusInitialKeyboardTarget()
    {
        _sheetBox.Focus();
        Keyboard.Focus(_sheetBox);
    }

    private UIElement CreateButtonRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        row.Children.Add(_okButton);
        row.Children.Add(_cancelButton);
        return row;
    }

    private void UpdateButtonState()
    {
        _okButton.IsEnabled = _sheetBox.SelectedItem is string sheetName && !string.IsNullOrWhiteSpace(sheetName);
    }

    private void Accept()
    {
        if (_sheetBox.SelectedItem is not string sheetName)
            return;

        Result = CreateResult(sheetName);
        DialogResult = true;
    }
}
