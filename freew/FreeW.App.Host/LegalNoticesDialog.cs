using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FreeW.App.Host;

public sealed partial class LegalNoticesDialog : Window
{
    private static readonly Regex NonAutomationIdCharacter = new("[^A-Za-z0-9]+", RegexOptions.Compiled);
    private readonly TabControl _tabControl = new();

    public LegalNoticesDialog()
        : this(FreeWLegalNoticeProvider.GetDocuments())
    {
    }

    internal LegalNoticesDialog(IReadOnlyList<FreeWLegalNoticeDocument> documents)
    {
        Title = "Legal Notices";
        Width = 840;
        Height = 620;
        MinWidth = 620;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        AutomationProperties.SetName(this, "Legal Notices");
        AutomationProperties.SetAutomationId(this, "LegalNoticesDialog");
        AutomationProperties.SetHelpText(this, "Shows the legal, privacy, and third-party notices packaged with this FreeW executable.");

        Content = CreateContent(documents);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private UIElement CreateContent(IReadOnlyList<FreeWLegalNoticeDocument> documents)
    {
        var root = new DockPanel { Margin = new Thickness(16) };

        var intro = new TextBlock
        {
            Text = "These notices are packaged with FreeW for offline review.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        AutomationProperties.SetName(intro, "Legal Notices summary");
        AutomationProperties.SetAutomationId(intro, "LegalNoticesSummaryText");
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var buttonRow = DialogButtonRowFactory.CreateOkOnly(Close, buttonWidth: 84, rowMargin: new Thickness(0, 12, 0, 0), acceptContent: "Close");
        var close = (Button)buttonRow.Children[0];
        AutomationProperties.SetAutomationId(close, "LegalNoticesCloseButton");
        AutomationProperties.SetHelpText(close, "Close the Legal Notices dialog.");
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);

        _tabControl.ItemsSource = documents.Select(CreateTabItem).ToList();
        _tabControl.SelectedIndex = documents.Count > 0 ? 0 : -1;
        AutomationProperties.SetName(_tabControl, "Legal notice sections");
        AutomationProperties.SetAutomationId(_tabControl, "LegalNoticesSectionTabs");
        AutomationProperties.SetHelpText(_tabControl, "Choose a legal notice section to read and copy.");
        root.Children.Add(_tabControl);

        return root;
    }

    private static TabItem CreateTabItem(FreeWLegalNoticeDocument document)
    {
        var automationIdSegment = CreateAutomationIdSegment(document.Title);
        var textBox = new TextBox
        {
            Text = document.Text,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            MinHeight = 280
        };
        AutomationProperties.SetName(textBox, document.Title);
        AutomationProperties.SetAutomationId(textBox, $"LegalNotices{automationIdSegment}Text");
        AutomationProperties.SetHelpText(textBox, "Read-only legal notice text. Use Ctrl+C to copy selected text.");

        var tabItem = new TabItem
        {
            Header = document.Title,
            Content = textBox
        };
        AutomationProperties.SetName(tabItem, document.Title);
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
