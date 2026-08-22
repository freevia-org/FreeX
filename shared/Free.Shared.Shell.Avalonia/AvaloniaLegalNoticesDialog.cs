using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// Shared Avalonia Legal Notices dialog matching the WPF sister surface's structure,
/// automation metadata, sizing, keyboard focus, and read/copy behavior.
/// </summary>
public class AvaloniaLegalNoticesDialog : AvaloniaDialogWindow
{
    private const double TextFontSizeCompensation = 12.1;
    // Avalonia's native multiline line box is taller than WPF's at the shared
    // 12 px Consolas size. Keep both short and overflowing notices on the
    // measured WPF-equivalent line box so long documents expose the same rows.
    private const double ShortDocumentLineHeightCompensation = 14.6;
    private const double OverflowDocumentLineHeightCompensation = 15.0;
    private readonly TabControl _tabControl = new();
    private readonly List<TextBox> _noticeTextBoxes = [];
    private readonly Button _closeButton = new();
    private readonly bool _acceptsTab;

    public AvaloniaLegalNoticesDialog(
        LegalNoticesDialogPresentation presentation,
        bool acceptsTab = true,
        bool enableKeyboardLifecycle = false)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        _acceptsTab = acceptsTab;

        if (presentation.TextRenderingPolicy == LegalNoticesTextRenderingPolicy.GrayscaleAntialias)
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);

        Title = presentation.WindowTitle;
        Width = LegalNoticesDialogMetrics.Width;
        Height = LegalNoticesDialogMetrics.Height;
        MinWidth = LegalNoticesDialogMetrics.MinWidth;
        MinHeight = LegalNoticesDialogMetrics.MinHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        ShowInTaskbar = false;

        AutomationProperties.SetName(this, presentation.WindowTitle);
        AutomationProperties.SetAutomationId(this, LegalNoticesDialogPresentation.DialogAutomationId);
        AutomationProperties.SetHelpText(this, presentation.HelpText);

        Content = CreateContent(presentation);
        if (enableKeyboardLifecycle)
            ConfigureKeyboardLifecycle(this, _tabControl, _closeButton);
        Opened += (_, _) =>
        {
            foreach (var textBox in _noticeTextBoxes)
            {
                ApplyReadOnlyDocumentLayout(textBox);
                ApplyWpfAuthorityDocumentInset(textBox);
                ScheduleShortDocumentInset(textBox, LegalNoticesDialogMetrics.TextPadding);
                ApplyTextRenderingPolicy(textBox, presentation.TextRenderingPolicy);
            }
            AvaloniaCompactDialogChrome.ApplyLegalNoticesDefaultButtonChrome(_closeButton);
            FocusInitialKeyboardTarget();
        };
    }

    internal TabControl SectionTabsForTest => _tabControl;

    private Control CreateContent(
        LegalNoticesDialogPresentation presentation)
    {
        // WPF's dialog authority registers the content one pixel higher while
        // keeping the outer bottom margin unchanged. Keep this compensation
        // local to the Avalonia host instead of changing shared dialog metrics.
        var root = new DockPanel
        {
            Margin = new Thickness(
                LegalNoticesDialogMetrics.ContentMargin,
                LegalNoticesDialogMetrics.ContentMargin - 1,
                LegalNoticesDialogMetrics.ContentMargin,
                LegalNoticesDialogMetrics.ContentMargin),
        };

        var intro = new TextBlock
        {
            Text = presentation.SummaryText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, LegalNoticesDialogMetrics.IntroBottomMargin),
            Foreground = Brushes.Black,
        };
        AvaloniaCompactDialogChrome.ApplyAvaloniaDocumentIntroTemplateCompensation(
            intro,
            LegalNoticesDialogMetrics.IntroBottomMargin);
        AutomationProperties.SetName(intro, presentation.SummaryAutomationName);
        AutomationProperties.SetAutomationId(intro, LegalNoticesDialogPresentation.SummaryAutomationId);
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var close = _closeButton;
        close.Content = presentation.CloseButtonContent;
        close.IsDefault = presentation.CloseIsDefault;
        close.IsCancel = presentation.CloseIsCancel;
        AvaloniaCompactDialogChrome.ApplyButton(
            close,
            AvaloniaCompactDialogChrome.WindowsStyle,
            minWidth: 84,
            isDefault: presentation.CloseIsDefault);
        close.Width = 84;
        AutomationProperties.SetAutomationId(close, LegalNoticesDialogPresentation.CloseButtonAutomationId);
        AutomationProperties.SetHelpText(close, presentation.HelpText);
        close.Click += (_, _) => Close();
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [close],
            new Thickness(0, LegalNoticesDialogMetrics.ActionRowTopMargin, 0, 0));
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);

        foreach (var section in presentation.Sections)
            _tabControl.Items.Add(CreateTabItem(section, presentation));
        _tabControl.SelectedIndex = presentation.Sections.Count > 0 ? 0 : -1;
        // Avalonia's default TabControl template adds a 12px content inset. The WPF
        // authority keeps the tab body aligned with the dialog content edge.
        _tabControl.Padding = new Thickness(0);
        _tabControl.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _tabControl.VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(_tabControl, presentation.SectionsAutomationName);
        AutomationProperties.SetAutomationId(_tabControl, LegalNoticesDialogPresentation.SectionsAutomationId);
        AutomationProperties.SetHelpText(
            _tabControl,
            presentation.SectionLinkHelpText);
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(
            _tabControl,
            AvaloniaCompactDialogChrome.WindowsStyle with { ControlHeight = LegalNoticesDialogMetrics.TabControlHeight },
            contentPaneMargin: new Thickness(0, -1, 0, 0));
        // WPF's tab header has a two-pixel leading inset while its body remains aligned
        // to the dialog content edge. Keep that compensation local to this authority pair.
        _tabControl.Styles.Add(new Style(s => s
            .OfType<TabControl>()
            .Template()
            .OfType<ItemsPresenter>()
            .Name("PART_ItemsPresenter"))
        {
            Setters = { new Setter(Layoutable.MarginProperty, new Thickness(2, 0, 0, 0)) },
        });
        root.Children.Add(_tabControl);

        return root;
    }

    private TabItem CreateTabItem(
        LegalNoticeSectionPresentation section,
        LegalNoticesDialogPresentation presentation)
    {
        var textBox = new TextBox
        {
            Text = section.Body,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = _acceptsTab,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = LegalNoticesDialogMetrics.TextFontSize,
            Padding = new Thickness(LegalNoticesDialogMetrics.TextPadding),
            MinHeight = LegalNoticesDialogMetrics.TextMinHeight,
            Foreground = Brushes.Black,
        };
        ApplyReadOnlyDocumentLayout(textBox);
        ApplyWpfAuthorityDocumentInset(textBox);
        AutomationProperties.SetName(textBox, section.Heading);
        AutomationProperties.SetAutomationId(
            textBox,
            section.BodyAutomationId);
        AutomationProperties.SetHelpText(
            textBox,
            presentation.ReadOnlyBodyHelpText);

        textBox.SetValue(
            ScrollViewer.VerticalScrollBarVisibilityProperty,
            ScrollBarVisibility.Auto);
        textBox.SetValue(
            ScrollViewer.HorizontalScrollBarVisibilityProperty,
            ScrollBarVisibility.Disabled);
        _noticeTextBoxes.Add(textBox);
        var tabItem = new TabItem { Header = section.Heading, Content = textBox };
        AutomationProperties.SetName(tabItem, section.Heading);
        AutomationProperties.SetAutomationId(
            tabItem,
            section.LinkAutomationId);
        AutomationProperties.SetHelpText(
            tabItem,
            presentation.SectionLinkHelpText);
        return tabItem;
    }

    /// <summary>Installs the shared Legal Notices Enter, Escape, and focus-cycle contract.</summary>
    public static void ConfigureKeyboardLifecycle(
        Window dialog,
        TabControl tabControl,
        Button closeButton)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(tabControl);
        ArgumentNullException.ThrowIfNull(closeButton);

        KeyboardNavigation.SetIsTabStop(closeButton, true);
        closeButton.Focusable = true;

        dialog.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) =>
            {
                if (args.KeyModifiers == KeyModifiers.None && args.Key == Key.Enter)
                {
                    args.Handled = true;
                    closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, closeButton));
                    if (dialog.IsVisible)
                        dialog.Close();
                    return;
                }

                if (args.KeyModifiers == KeyModifiers.None && args.Key == Key.Escape)
                {
                    args.Handled = true;
                    dialog.Close();
                    return;
                }

                if (args.Key != Key.Tab ||
                    (args.KeyModifiers != KeyModifiers.None && args.KeyModifiers != KeyModifiers.Shift))
                {
                    return;
                }

                var tabStops = GetKeyboardTabStops(tabControl, closeButton);
                if (tabStops.Count == 0)
                    return;

                var focused = dialog.FocusManager?.GetFocusedElement() as Control;
                var currentIndex = focused is null ? -1 : tabStops.IndexOf(focused);
                var nextIndex = args.KeyModifiers == KeyModifiers.Shift
                    ? currentIndex <= 0 ? tabStops.Count - 1 : currentIndex - 1
                    : currentIndex < 0 || currentIndex == tabStops.Count - 1 ? 0 : currentIndex + 1;

                tabStops[nextIndex].Focus();
                args.Handled = true;
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private static List<Control> GetKeyboardTabStops(TabControl tabControl, Button closeButton)
    {
        var tabStops = new List<Control>();
        if (tabControl.SelectedItem is TabItem tabItem &&
            FindDocumentTextBox(tabItem) is { } textBox &&
            textBox.IsVisible &&
            textBox.IsEffectivelyEnabled)
        {
            textBox.Focusable = true;
            KeyboardNavigation.SetIsTabStop(textBox, true);
            tabStops.Add(textBox);
        }

        if (closeButton.IsVisible && closeButton.IsEffectivelyEnabled)
            tabStops.Add(closeButton);

        return tabStops;
    }

    private static TextBox? FindDocumentTextBox(TabItem tabItem) =>
        tabItem.Content as TextBox ??
        (tabItem.Content is ScrollViewer { Content: TextBox wrappedTextBox } ? wrappedTextBox : null) ??
        tabItem.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();

    private static void ApplyReadOnlyDocumentLayout(TextBox textBox)
    {
        AvaloniaCompactDialogChrome.ApplyAvaloniaReadOnlyDocumentTemplatePadding(
            textBox,
            LegalNoticesDialogMetrics.TextPadding,
            rightMargin: 2);
        // Generic dialog chrome supplies the normal product text brush on Opened. Legal-document
        // surfaces follow the WPF authority's explicit black foreground, so restore it after that pass.
        textBox.Foreground = Brushes.Black;
        // Avalonia's Consolas metrics are fractionally narrower than WPF's at the shared
        // 12px size. Compensate the glyph width without imposing a line box absent in WPF.
        textBox.FontSize = TextFontSizeCompensation;
    }

    private static void ApplyTextRenderingPolicy(
        TextBox textBox,
        LegalNoticesTextRenderingPolicy policy)
    {
        if (policy != LegalNoticesTextRenderingPolicy.GrayscaleAntialias)
            return;

        TextOptions.SetTextRenderingMode(textBox, TextRenderingMode.Antialias);
        foreach (var presenter in textBox.GetVisualDescendants().OfType<TextPresenter>())
            TextOptions.SetTextRenderingMode(presenter, TextRenderingMode.Antialias);
    }

    private static void ApplyWpfAuthorityDocumentInset(TextBox textBox)
    {
        // WPF applies the shared eight-pixel content padding directly. Avalonia's
        // realized template contributes one additional leading pixel, so keep the
        // authority compensation in the shared Avalonia renderer for every product.
        textBox.Padding = new Thickness(
            LegalNoticesDialogMetrics.TextPadding + 1,
            textBox.Padding.Top,
            LegalNoticesDialogMetrics.TextPadding,
            LegalNoticesDialogMetrics.TextPadding);
    }

    private static void ScheduleShortDocumentInset(TextBox textBox, double basePadding)
    {
        EventHandler? onLayoutUpdated = null;
        onLayoutUpdated = (_, _) =>
        {
            var scrollViewer = textBox
                .GetVisualDescendants()
                .OfType<ScrollViewer>()
                .SingleOrDefault();
            var presenter = textBox
                .GetVisualDescendants()
                .OfType<TextPresenter>()
                .SingleOrDefault();
            if (scrollViewer is null ||
                presenter is null ||
                scrollViewer.Viewport.Height <= 0 ||
                presenter.DesiredSize.Height <= 0)
            {
                return;
            }

            textBox.LayoutUpdated -= onLayoutUpdated;
            if (AvaloniaCompactDialogChrome.RequiresReadOnlyDocumentOverflowLineHeight(
                scrollViewer.Viewport.Height,
                presenter.TextLayout.TextLines.Count,
                LegalNoticesDialogMetrics.TextLineHeight,
                basePadding * 2))
            {
                // Reserving the WPF-sized overflow estimate makes Auto expose its
                // scrollbar lane; the realized line box remains the shared correction.
                textBox.LineHeight = OverflowDocumentLineHeightCompensation;
                return;
            }

            var inset = AvaloniaCompactDialogChrome.CalculateReadOnlyDocumentInset(
                scrollViewer.Viewport.Height,
                presenter.DesiredSize.Height);
            // Preserve the native-layout inset while closing Avalonia's six-pixel
            // cumulative baseline shortfall across the short WPF authority document.
            textBox.LineHeight = ShortDocumentLineHeightCompensation;
            if (inset > 0)
            {
                textBox.Padding = new Thickness(
                    textBox.Padding.Left,
                    basePadding + inset,
                    textBox.Padding.Right,
                    textBox.Padding.Bottom);
            }
        };
        textBox.LayoutUpdated += onLayoutUpdated;
    }

    private void FocusInitialKeyboardTarget()
    {
        if (_tabControl.SelectedItem is not TabItem tabItem ||
            FindDocumentTextBox(tabItem) is not { } textBox)
        {
            return;
        }

        textBox.Focus();
        textBox.CaretIndex = 0;
    }
}
