using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

/// <summary>Shared Avalonia realization of the WPF product About dialog.</summary>
public class AvaloniaAboutDialog : AvaloniaDialogWindow
{
    private const string AboutViewportClass = "free-about-document-viewport";
    private Button _okButton = null!;
    private readonly TextBox _aboutTextBox;
    private readonly double _textPaddingRight;
    private readonly double _textFontSize;
    private readonly double _textPaddingTop;
    private readonly bool _defaultButtonAccent;
    private readonly double _textLineHeight;

    public AvaloniaAboutDialog(AboutDialogPresentation presentation)
        : this(
            presentation.WindowTitle,
            presentation.AboutText,
            presentation.DialogAutomationId,
            presentation.TextAutomationId,
            presentation.OkAutomationId,
            presentation.HelpText,
            presentation.AvaloniaRootRightMargin,
            presentation.AvaloniaTextPaddingRight,
            presentation.AvaloniaTextFontSize,
            presentation.AvaloniaTextPaddingTop,
            presentation.AvaloniaDefaultButtonAccent,
            presentation.AvaloniaTextLineHeight)
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
        double? rightContentMargin = null,
        double? textPaddingRight = null,
        double? textFontSize = null,
        double? textPaddingTop = null,
        bool defaultButtonAccent = false,
        double? textLineHeight = null)
    {
        Title = windowTitle;
        Width = AboutDialogMetrics.Width;
        Height = AboutDialogMetrics.Height;
        MinWidth = AboutDialogMetrics.MinWidth;
        MinHeight = AboutDialogMetrics.MinHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        ShowInTaskbar = false;
        _textPaddingRight = textPaddingRight ?? AboutDialogMetrics.AvaloniaTextPaddingRight;
        _textFontSize = textFontSize ?? AboutDialogMetrics.AvaloniaTextFontSize;
        _textPaddingTop = textPaddingTop ?? AboutDialogMetrics.AvaloniaTextPaddingTop;
        _defaultButtonAccent = defaultButtonAccent;
        _textLineHeight = textLineHeight ?? AboutDialogMetrics.AvaloniaTextLineHeight;

        AutomationProperties.SetName(this, windowTitle);
        AutomationProperties.SetAutomationId(this, dialogAutomationId);
        AutomationProperties.SetHelpText(this, helpText);

        _aboutTextBox = new TextBox
        {
            Text = aboutText,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = _textFontSize,
            Padding = new Thickness(
                AboutDialogMetrics.AvaloniaTextPaddingLeft,
                _textPaddingTop,
                _textPaddingRight,
                AboutDialogMetrics.TextPadding),
            LineHeight = _textLineHeight,
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
            isDefault: _defaultButtonAccent);
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
            _textPaddingTop,
            _textPaddingRight,
            AboutDialogMetrics.TextPadding);
        _aboutTextBox.VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
        _aboutTextBox.FontSize = _textFontSize;
        _aboutTextBox.LineHeight = _textLineHeight;
        // The WPF authority centers the short About document inside its read-only viewport.
        // Avalonia's outer TextBox alignment does not reach the template-owned ScrollViewer,
        // so style that realized document host through the control's local template scope.
        if (!_aboutTextBox.Classes.Contains(AboutViewportClass))
        {
            _aboutTextBox.Classes.Add(AboutViewportClass);
            _aboutTextBox.Styles.Add(new Style(selector => selector.OfType<ScrollViewer>())
            {
                Setters =
                {
                    new Setter(
                        ScrollViewer.VerticalContentAlignmentProperty,
                        global::Avalonia.Layout.VerticalAlignment.Center),
                },
            });
        }
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
