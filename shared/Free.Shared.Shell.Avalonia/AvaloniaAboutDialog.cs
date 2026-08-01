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
    private static readonly IBrush FocusedInputBorderBrush =
        new SolidColorBrush(Color.FromRgb(0x56, 0x9D, 0xE5));
    private static readonly IBrush InputBorderBrush =
        new SolidColorBrush(Color.FromRgb(0xAB, 0xAD, 0xB3));
    private Button _okButton = null!;
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
            FontSize = 12.3,
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
            // WPF's layout rounding leaves the right edge one device pixel farther right than
            // Avalonia's symmetric 16 DIP margin. The local root compensation keeps both bounds
            // and the single action button on the same authority pixels.
            _aboutTextBox.FontSize = 12.3;
            _aboutTextBox.Padding = new Thickness(10, 8, 8, 8);
            _aboutTextBox.BorderBrush = FocusedInputBorderBrush;
            _okButton.BorderBrush = InputBorderBrush;
            FocusInitialKeyboardTarget();
        };
        _aboutTextBox.GotFocus += (_, _) => _aboutTextBox.BorderBrush = FocusedInputBorderBrush;
        _aboutTextBox.LostFocus += (_, _) => _aboutTextBox.BorderBrush = InputBorderBrush;
    }

    internal TextBox AboutTextBoxForTest => _aboutTextBox;

    private Control CreateContent(string okAutomationId, string helpText)
    {
        var root = new DockPanel { Margin = new Thickness(16, 16, 15, 16) };
        var ok = _okButton = new Button
        {
            Content = "_OK",
            IsDefault = true,
            IsCancel = true,
        };
        AvaloniaCompactDialogChrome.ApplyButton(
            ok,
            AvaloniaCompactDialogChrome.WindowsStyle,
            minWidth: 84,
            // Preserve IsDefault for keyboard behavior, but match the WPF resting border. WPF's
            // default button is neutral in the authority capture while the text box owns focus.
            isDefault: false);
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
