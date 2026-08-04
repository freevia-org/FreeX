using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Shared WPF Legal Notices dialog. Accepts the loaded notice documents plus app-specific
/// strings (title, intro, help text) so each app preserves its exact automation IDs and
/// displayed text while sharing all structural and interaction logic.
/// </summary>
public partial class SharedLegalNoticesDialog : DialogWindow
{
    private static readonly Regex NonAutomationIdCharacter = new("[^A-Za-z0-9]+", RegexOptions.Compiled);
    private readonly TabControl _tabControl = new();

    /// <param name="windowTitle">Window title (e.g. "Legal Notices").</param>
    /// <param name="notices">Ordered list of (Title, Text) tuples from the app's legal resources.</param>
    /// <param name="introText">Sentence shown above the tabs (app-specific).</param>
    /// <param name="closeButtonContent">Content of the close/OK button (may include an access-key underscore).</param>
    /// <param name="helpText">AutomationHelpText for window, close button (app-specific).</param>
    public SharedLegalNoticesDialog(
        string windowTitle,
        IReadOnlyList<(string Title, string Text)> notices,
        string introText,
        string closeButtonContent,
        string helpText)
    {
        Title = windowTitle;
        Width = LegalNoticesDialogMetrics.Width;
        Height = LegalNoticesDialogMetrics.Height;
        MinWidth = LegalNoticesDialogMetrics.MinWidth;
        MinHeight = LegalNoticesDialogMetrics.MinHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        AutomationProperties.SetName(this, windowTitle);
        AutomationProperties.SetAutomationId(this, "LegalNoticesDialog");
        AutomationProperties.SetHelpText(this, helpText);

        Content = CreateContent(notices, introText, closeButtonContent, helpText);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private UIElement CreateContent(
        IReadOnlyList<(string Title, string Text)> notices,
        string introText,
        string closeButtonContent,
        string helpText)
    {
        var root = new DockPanel { Margin = new Thickness(LegalNoticesDialogMetrics.ContentMargin) };

        var intro = new TextBlock
        {
            Text = introText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, LegalNoticesDialogMetrics.IntroBottomMargin)
        };
        AutomationProperties.SetName(intro, "Legal Notices summary");
        AutomationProperties.SetAutomationId(intro, "LegalNoticesSummaryText");
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var buttonRow = DialogButtonRowFactory.CreateOkOnly(
            Close,
            buttonWidth: 84,
            rowMargin: new Thickness(0, LegalNoticesDialogMetrics.ActionRowTopMargin, 0, 0),
            acceptContent: closeButtonContent);
        if (buttonRow.Children[0] is Button closeButton)
        {
            AutomationProperties.SetAutomationId(closeButton, "LegalNoticesCloseButton");
            AutomationProperties.SetHelpText(closeButton, helpText);
        }

        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);

        _tabControl.ItemsSource = notices.Select(CreateTabItem).ToList();
        _tabControl.SelectedIndex = notices.Count > 0 ? 0 : -1;
        DialogTabChrome.Apply(_tabControl);
        AutomationProperties.SetName(_tabControl, "Legal notice sections");
        AutomationProperties.SetAutomationId(_tabControl, "LegalNoticesSectionTabs");
        AutomationProperties.SetHelpText(_tabControl, "Choose a legal notice section to read and copy.");
        root.Children.Add(_tabControl);

        return root;
    }

    private static TabItem CreateTabItem((string Title, string Text) notice)
    {
        var automationIdSegment = CreateAutomationIdSegment(notice.Title);
        var textBox = new TextBox
        {
            Text = notice.Text,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily = new FontFamily("Consolas"),
            FontSize = LegalNoticesDialogMetrics.TextFontSize,
            Padding = new Thickness(LegalNoticesDialogMetrics.TextPadding),
            BorderThickness = new Thickness(1),
            MinHeight = LegalNoticesDialogMetrics.TextMinHeight
        };
        AutomationProperties.SetName(textBox, notice.Title);
        AutomationProperties.SetAutomationId(textBox, $"LegalNotices{automationIdSegment}Text");
        AutomationProperties.SetHelpText(textBox, "Read-only legal notice text. Use Ctrl+C to copy selected text.");

        var tabItem = new TabItem
        {
            Header = notice.Title,
            Content = textBox
        };
        AutomationProperties.SetName(tabItem, notice.Title);
        AutomationProperties.SetAutomationId(tabItem, $"LegalNotices{automationIdSegment}Tab");
        AutomationProperties.SetHelpText(tabItem, "Choose a legal notice section to read and copy.");

        return tabItem;
    }

    private static string CreateAutomationIdSegment(string text)
    {
        var segment = NonAutomationIdCharacter.Replace(text, string.Empty);
        return string.IsNullOrWhiteSpace(segment) ? "Document" : segment;
    }

    private void FocusInitialKeyboardTarget()
    {
        if (_tabControl.SelectedItem is not TabItem { Content: TextBox textBox })
            return;

        textBox.Focus();
        Keyboard.Focus(textBox);
        textBox.CaretIndex = 0;
    }
}
