using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Shared WPF Legal Notices renderer. Product content and accessibility semantics arrive as
/// one host-neutral presentation while this type owns only native controls and interaction.
/// </summary>
public partial class SharedLegalNoticesDialog : DialogWindow
{
    private readonly TabControl _tabControl = new();

    public SharedLegalNoticesDialog(LegalNoticesDialogPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        Title = presentation.WindowTitle;
        Width = LegalNoticesDialogMetrics.Width;
        Height = LegalNoticesDialogMetrics.Height;
        MinWidth = LegalNoticesDialogMetrics.MinWidth;
        MinHeight = LegalNoticesDialogMetrics.MinHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        AutomationProperties.SetName(this, presentation.WindowTitle);
        AutomationProperties.SetAutomationId(this, LegalNoticesDialogPresentation.DialogAutomationId);
        AutomationProperties.SetHelpText(this, presentation.HelpText);

        Content = CreateContent(presentation);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private UIElement CreateContent(LegalNoticesDialogPresentation presentation)
    {
        var root = new DockPanel { Margin = new Thickness(LegalNoticesDialogMetrics.ContentMargin) };

        var intro = new TextBlock
        {
            Text = presentation.SummaryText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, LegalNoticesDialogMetrics.IntroBottomMargin)
        };
        AutomationProperties.SetName(intro, presentation.SummaryAutomationName);
        AutomationProperties.SetAutomationId(intro, LegalNoticesDialogPresentation.SummaryAutomationId);
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var buttonRow = DialogButtonRowFactory.CreateOkOnly(
            Close,
            buttonWidth: 84,
            rowMargin: new Thickness(0, LegalNoticesDialogMetrics.ActionRowTopMargin, 0, 0),
            acceptContent: presentation.CloseButtonContent);
        if (buttonRow.Children[0] is Button closeButton)
        {
            closeButton.IsDefault = presentation.CloseIsDefault;
            closeButton.IsCancel = presentation.CloseIsCancel;
            AutomationProperties.SetAutomationId(closeButton, LegalNoticesDialogPresentation.CloseButtonAutomationId);
            AutomationProperties.SetHelpText(closeButton, presentation.HelpText);
        }

        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);

        _tabControl.ItemsSource = presentation.Sections
            .Select(section => CreateTabItem(section, presentation))
            .ToList();
        _tabControl.SelectedIndex = presentation.Sections.Count > 0 ? 0 : -1;
        DialogTabChrome.Apply(_tabControl);
        AutomationProperties.SetName(_tabControl, presentation.SectionsAutomationName);
        AutomationProperties.SetAutomationId(_tabControl, LegalNoticesDialogPresentation.SectionsAutomationId);
        AutomationProperties.SetHelpText(_tabControl, presentation.SectionLinkHelpText);
        root.Children.Add(_tabControl);

        return root;
    }

    private static TabItem CreateTabItem(
        LegalNoticeSectionPresentation section,
        LegalNoticesDialogPresentation presentation)
    {
        var textBox = new TextBox
        {
            Text = section.Body,
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
        AutomationProperties.SetName(textBox, section.Heading);
        AutomationProperties.SetAutomationId(textBox, section.BodyAutomationId);
        AutomationProperties.SetHelpText(textBox, presentation.ReadOnlyBodyHelpText);

        var tabItem = new TabItem
        {
            Header = section.Heading,
            Content = textBox
        };
        AutomationProperties.SetName(tabItem, section.Heading);
        AutomationProperties.SetAutomationId(tabItem, section.LinkAutomationId);
        AutomationProperties.SetHelpText(tabItem, presentation.SectionLinkHelpText);

        return tabItem;
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
