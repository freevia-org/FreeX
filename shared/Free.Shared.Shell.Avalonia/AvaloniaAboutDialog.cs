using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace Free.Shared.Shell.Avalonia;

/// <summary>Shared Avalonia realization of the WPF product About dialog.</summary>
public class AvaloniaAboutDialog : AvaloniaDialogWindow
{
    private readonly TextBox _aboutTextBox;

    public AvaloniaAboutDialog(
        string windowTitle,
        string aboutText,
        string dialogAutomationId,
        string textAutomationId,
        string okAutomationId,
        string helpText)
    {
        Title = windowTitle;
        Width = 560;
        Height = 420;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        ShowInTaskbar = false;

        AutomationProperties.SetName(this, windowTitle);
        AutomationProperties.SetAutomationId(this, dialogAutomationId);
        AutomationProperties.SetHelpText(this, helpText);

        _aboutTextBox = new TextBox
        {
            Text = aboutText,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            MinHeight = 220,
        };
        _aboutTextBox.SetValue(
            ScrollViewer.VerticalScrollBarVisibilityProperty,
            ScrollBarVisibility.Auto);
        _aboutTextBox.SetValue(
            ScrollViewer.HorizontalScrollBarVisibilityProperty,
            ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_aboutTextBox, windowTitle);
        AutomationProperties.SetAutomationId(_aboutTextBox, textAutomationId);
        AutomationProperties.SetHelpText(_aboutTextBox, helpText);

        Content = CreateContent(okAutomationId, helpText);
        Opened += (_, _) =>
        {
            _aboutTextBox.Padding = new Thickness(8);
            FocusInitialKeyboardTarget();
        };
    }

    internal TextBox AboutTextBoxForTest => _aboutTextBox;

    private Control CreateContent(string okAutomationId, string helpText)
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        var ok = new Button
        {
            Content = "_OK",
            IsDefault = true,
            IsCancel = true,
        };
        AvaloniaCompactDialogChrome.ApplyButton(
            ok,
            AvaloniaCompactDialogChrome.WindowsStyle,
            minWidth: 84,
            isDefault: true);
        AutomationProperties.SetAutomationId(ok, okAutomationId);
        AutomationProperties.SetHelpText(ok, helpText);
        ok.Click += (_, _) => Close();

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [ok],
            new Thickness(0, 12, 0, 0));
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);
        root.Children.Add(_aboutTextBox);
        return root;
    }

    private void FocusInitialKeyboardTarget()
    {
        _aboutTextBox.Focus(NavigationMethod.Tab);
        _aboutTextBox.CaretIndex = 0;
    }
}
