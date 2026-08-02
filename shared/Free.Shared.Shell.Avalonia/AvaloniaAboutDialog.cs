using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

/// <summary>Shared Avalonia realization of the WPF product About dialog.</summary>
public class AvaloniaAboutDialog : AvaloniaDialogWindow
{
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
        Width = AboutDialogMetrics.Width;
        Height = AboutDialogMetrics.Height;
        MinWidth = AboutDialogMetrics.MinWidth;
        MinHeight = AboutDialogMetrics.MinHeight;
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
            FontSize = AboutDialogMetrics.AvaloniaTextFontSize,
            Padding = new Thickness(AboutDialogMetrics.TextPadding),
            BorderThickness = new Thickness(1),
            MinHeight = AboutDialogMetrics.TextMinHeight,
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
            _aboutTextBox.FontSize = AboutDialogMetrics.AvaloniaTextFontSize;
            _aboutTextBox.Padding = new Thickness(
                AboutDialogMetrics.TextPadding + 2,
                AboutDialogMetrics.TextPadding,
                AboutDialogMetrics.TextPadding,
                AboutDialogMetrics.TextPadding);
            AvaloniaCompactDialogChrome.ApplyNeutralDefaultButtonChrome(_okButton);
            FocusInitialKeyboardTarget();
        };
    }

    internal TextBox AboutTextBoxForTest => _aboutTextBox;

    private Control CreateContent(string okAutomationId, string helpText)
    {
        var root = new DockPanel
        {
            Margin = new Thickness(
                AboutDialogMetrics.RootMargin,
                AboutDialogMetrics.RootMargin,
                AboutDialogMetrics.RootMargin - 1,
                AboutDialogMetrics.RootMargin),
        };
        var ok = _okButton = new Button
        {
            Content = "_OK",
            IsDefault = true,
            IsCancel = true,
        };
        AvaloniaCompactDialogChrome.ApplyButton(
            ok,
            AvaloniaCompactDialogChrome.WindowsStyle,
            minWidth: AboutDialogMetrics.ButtonWidth,
            // Preserve IsDefault for keyboard behavior, but match the WPF resting border. WPF's
            // default button is neutral in the authority capture while the text box owns focus.
            isDefault: false);
        AutomationProperties.SetAutomationId(ok, okAutomationId);
        AutomationProperties.SetHelpText(ok, helpText);
        ok.Click += (_, _) => Close();

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [ok],
            new Thickness(0, AboutDialogMetrics.ActionTopMargin, 0, 0));
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
