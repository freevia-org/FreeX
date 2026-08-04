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

    public AvaloniaAboutDialog(AboutDialogPresentation presentation)
        : this(
            presentation.WindowTitle,
            presentation.AboutText,
            presentation.DialogAutomationId,
            presentation.TextAutomationId,
            presentation.OkAutomationId,
            presentation.HelpText,
            presentation.AvaloniaRootRightMargin)
    {
        ArgumentNullException.ThrowIfNull(presentation);
    }

    public AvaloniaAboutDialog(
        string windowTitle,
        string aboutText,
        string dialogAutomationId,
        string textAutomationId,
        string okAutomationId,
        string helpText,
        double? rightContentMargin = null)
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
            Padding = new Thickness(
                AboutDialogMetrics.AvaloniaTextPaddingLeft,
                AboutDialogMetrics.AvaloniaTextPaddingTop,
                AboutDialogMetrics.AvaloniaTextPaddingRight,
                AboutDialogMetrics.TextPadding),
            LineHeight = AboutDialogMetrics.AvaloniaTextLineHeight,
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

        Content = CreateContent(okAutomationId, helpText, rightContentMargin);
        ApplyAboutVisualChrome();
        Opened += (_, _) =>
        {
            ApplyAboutVisualChrome();
            FocusInitialKeyboardTarget();
        };
    }

    internal TextBox AboutTextBoxForTest => _aboutTextBox;

    private Control CreateContent(string okAutomationId, string helpText, double? rightContentMargin)
    {
        var root = new DockPanel
        {
            Margin = new Thickness(
                AboutDialogMetrics.RootMargin,
                AboutDialogMetrics.RootMargin,
                rightContentMargin ?? AboutDialogMetrics.RootMargin,
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

    private void ApplyAboutVisualChrome()
    {
        // Reuse the shared read-only document scrollbar/focus treatment, then retain the About
        // host's measured bounds while correcting its line box and vertical viewport inset.
        AvaloniaCompactDialogChrome.ApplyAvaloniaReadOnlyDocumentTemplatePadding(
            _aboutTextBox,
            AboutDialogMetrics.TextPadding);
        _aboutTextBox.Margin = new Thickness(0);
        _aboutTextBox.Padding = new Thickness(
            AboutDialogMetrics.AvaloniaTextPaddingLeft,
            AboutDialogMetrics.AvaloniaTextPaddingTop,
            AboutDialogMetrics.AvaloniaTextPaddingRight,
            AboutDialogMetrics.TextPadding);
        _aboutTextBox.FontSize = AboutDialogMetrics.AvaloniaTextFontSize;
        _aboutTextBox.LineHeight = AboutDialogMetrics.AvaloniaTextLineHeight;
        // WPF's About action button has a white resting surface; preserve the shared button
        // metrics and border while correcting this authority-specific fill.
        _okButton.Background = Brushes.White;
    }

    private void FocusInitialKeyboardTarget()
    {
        _aboutTextBox.Focus(NavigationMethod.Tab);
        _aboutTextBox.CaretIndex = 0;
    }
}
