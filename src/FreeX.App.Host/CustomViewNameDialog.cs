using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host;

public sealed record CustomViewNameDialogResult(
    string ViewName,
    bool IncludePrintSettings = true,
    bool IncludeHiddenRowsColumnsAndFilterSettings = true);

public sealed class CustomViewNameDialog : Window
{
    private readonly TextBox _nameBox = new();
    private readonly CheckBox _printSettingsBox = new() { Content = UiText.Get("CustomViewName_PrintSettingsCheckBox"), IsChecked = true };
    private readonly CheckBox _hiddenFilterSettingsBox = new() { Content = UiText.Get("CustomViewName_HiddenFilterSettingsCheckBox"), IsChecked = true };

    public CustomViewNameDialogResult Result { get; private set; }

    public CustomViewNameDialog(string defaultValue)
    {
        Result = CreateResult(defaultValue);
        Title = UiText.Get("CustomViewName_Title");
        Width = 320;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new Label { Content = UiText.Get("CustomViewName_NameLabel"), Target = _nameBox, Margin = new Thickness(0, 0, 0, 4) };
        _nameBox.Text = Result.ViewName;
        AutomationProperties.SetName(_nameBox, UiText.Get("CustomViewName_NameAutomationName"));
        AutomationProperties.SetAutomationId(_nameBox, "CustomViewNameBox");
        AutomationProperties.SetHelpText(_nameBox, UiText.Get("CustomViewName_NameHelpText"));
        AutomationProperties.SetName(_printSettingsBox, UiText.Get("CustomViewName_PrintSettingsAutomationName"));
        AutomationProperties.SetAutomationId(_printSettingsBox, "CustomViewPrintSettingsCheckBox");
        AutomationProperties.SetHelpText(_printSettingsBox, UiText.Get("CustomViewName_PrintSettingsHelpText"));
        AutomationProperties.SetName(_hiddenFilterSettingsBox, UiText.Get("CustomViewName_HiddenFilterSettingsAutomationName"));
        AutomationProperties.SetAutomationId(_hiddenFilterSettingsBox, "CustomViewHiddenFilterSettingsCheckBox");
        AutomationProperties.SetHelpText(_hiddenFilterSettingsBox, UiText.Get("CustomViewName_HiddenFilterSettingsHelpText"));
        _printSettingsBox.Margin = new Thickness(0, 8, 0, 4);
        _hiddenFilterSettingsBox.Margin = new Thickness(0, 0, 0, 4);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button { Content = UiText.Ok, Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = UiText.Cancel, Width = 72, IsCancel = true };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        Grid.SetRow(label, 0);
        Grid.SetRow(_nameBox, 1);
        Grid.SetRow(_printSettingsBox, 2);
        Grid.SetRow(_hiddenFilterSettingsBox, 3);
        Grid.SetRow(buttons, 4);
        grid.Children.Add(label);
        grid.Children.Add(_nameBox);
        grid.Children.Add(_printSettingsBox);
        grid.Children.Add(_hiddenFilterSettingsBox);
        grid.Children.Add(buttons);
        Content = grid;

        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static CustomViewNameDialogResult CreateResult(
        string viewName,
        bool includePrintSettings = true,
        bool includeHiddenRowsColumnsAndFilterSettings = true) =>
        new(
            viewName.Trim(),
            includePrintSettings,
            includeHiddenRowsColumnsAndFilterSettings);

    private void Accept()
    {
        Result = CreateResult(
            _nameBox.Text,
            _printSettingsBox.IsChecked == true,
            _hiddenFilterSettingsBox.IsChecked == true);
        if (string.IsNullOrWhiteSpace(Result.ViewName))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("CustomViewName_BlankNameMessage"), Title);
            FocusNameInput();
            return;
        }

        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusNameInput();
    }

    private void FocusNameInput()
    {
        DialogFocus.FocusAndSelect(_nameBox);
    }
}
