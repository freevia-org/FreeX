using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// Shared Avalonia Legal Notices dialog matching the WPF sister surface's structure,
/// automation metadata, sizing, keyboard focus, and read/copy behavior.
/// </summary>
public class AvaloniaLegalNoticesDialog : AvaloniaDialogWindow
{
    private static readonly Regex NonAutomationIdCharacter =
        new("[^A-Za-z0-9]+", RegexOptions.Compiled);

    private readonly TabControl _tabControl = new();
    private readonly List<TextBox> _noticeTextBoxes = [];

    public AvaloniaLegalNoticesDialog(
        string windowTitle,
        IReadOnlyList<(string Title, string Text)> notices,
        string introText,
        string closeButtonContent,
        string helpText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowTitle);
        ArgumentNullException.ThrowIfNull(notices);

        Title = windowTitle;
        Width = 840;
        Height = 620;
        MinWidth = 620;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        ShowInTaskbar = false;

        AutomationProperties.SetName(this, windowTitle);
        AutomationProperties.SetAutomationId(this, "LegalNoticesDialog");
        AutomationProperties.SetHelpText(this, helpText);

        Content = CreateContent(notices, introText, closeButtonContent, helpText);
        Opened += (_, _) =>
        {
            foreach (var textBox in _noticeTextBoxes)
                textBox.Padding = new Thickness(8);
            FocusInitialKeyboardTarget();
        };
    }

    internal TabControl SectionTabsForTest => _tabControl;

    private Control CreateContent(
        IReadOnlyList<(string Title, string Text)> notices,
        string introText,
        string closeButtonContent,
        string helpText)
    {
        var root = new DockPanel { Margin = new Thickness(16) };

        var intro = new TextBlock
        {
            Text = introText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        AutomationProperties.SetName(intro, "Legal Notices summary");
        AutomationProperties.SetAutomationId(intro, "LegalNoticesSummaryText");
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var close = new Button
        {
            Content = closeButtonContent,
            IsDefault = true,
            IsCancel = true,
        };
        AvaloniaCompactDialogChrome.ApplyButton(
            close,
            new AvaloniaCompactDialogChromeStyle(FontFamily.Default),
            minWidth: 84,
            isDefault: true);
        AutomationProperties.SetAutomationId(close, "LegalNoticesCloseButton");
        AutomationProperties.SetHelpText(close, helpText);
        close.Click += (_, _) => Close();
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [close],
            new Thickness(0, 12, 0, 0));
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);

        foreach (var notice in notices)
            _tabControl.Items.Add(CreateTabItem(notice));
        _tabControl.SelectedIndex = notices.Count > 0 ? 0 : -1;
        AutomationProperties.SetName(_tabControl, "Legal notice sections");
        AutomationProperties.SetAutomationId(_tabControl, "LegalNoticesSectionTabs");
        AutomationProperties.SetHelpText(
            _tabControl,
            "Choose a legal notice section to read and copy.");
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(_tabControl);
        root.Children.Add(_tabControl);

        return root;
    }

    private TabItem CreateTabItem((string Title, string Text) notice)
    {
        var automationIdSegment = CreateAutomationIdSegment(notice.Title);
        var textBox = new TextBox
        {
            Text = notice.Text,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(8),
            MinHeight = 280,
        };
        AutomationProperties.SetName(textBox, notice.Title);
        AutomationProperties.SetAutomationId(
            textBox,
            $"LegalNotices{automationIdSegment}Text");
        AutomationProperties.SetHelpText(
            textBox,
            "Read-only legal notice text. Use Ctrl+C to copy selected text.");

        textBox.SetValue(
            ScrollViewer.VerticalScrollBarVisibilityProperty,
            ScrollBarVisibility.Auto);
        textBox.SetValue(
            ScrollViewer.HorizontalScrollBarVisibilityProperty,
            ScrollBarVisibility.Disabled);
        _noticeTextBoxes.Add(textBox);
        var tabItem = new TabItem { Header = notice.Title, Content = textBox };
        AutomationProperties.SetName(tabItem, notice.Title);
        AutomationProperties.SetAutomationId(
            tabItem,
            $"LegalNotices{automationIdSegment}Tab");
        AutomationProperties.SetHelpText(
            tabItem,
            "Choose a legal notice section to read and copy.");
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
        {
            return;
        }

        textBox.Focus();
        textBox.CaretIndex = 0;
    }
}
