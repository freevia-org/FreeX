using System.Windows;
using System.Windows.Controls;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Host;

/// <summary>
/// FreeX's backstage (the full-window File screen) rebuilt on top of the shared, app-neutral
/// <see cref="BackstageFrame"/> — the de-brittling pilot for the unification program (P1). The hand-rolled
/// <c>StartScreenSidebar</c> rail is gone; this wrapper builds the frame, supplies the 12 rail entries with
/// their full FreeX metadata (key-tips, automation ids/names/help, rich tooltips, command-icon names) and
/// routes each entry back into FreeX's existing handlers.
///
/// The three content panes (<c>SsHomeView</c>/<c>SsInfoView</c>/<c>SsPrintView</c>) are kept exactly as they
/// were in XAML, parked in a hidden holder inside <c>StartScreenOverlay</c>; each pane entry's
/// <see cref="BackstageEntry.ContentFactory"/> first runs that pane's live-refresh logic (the same calls the
/// old <c>Show*View</c> methods made) and then reparents the pane element into the frame's content host.
/// </summary>
public partial class MainWindow
{
    private BackstageFrame? _backstageFrame;

    // Language-invariant pane identifiers (the frame's automation ids) so ShowInfoView()/ShowPrintView()
    // and Ctrl+P can land on a specific pane regardless of the current UI language.
    private const string BackstageHomePaneId = FreeXBackstageNavigationPlanner.HomePaneAutomationId;
    private const string BackstageInfoPaneId = FreeXBackstageNavigationPlanner.InfoPaneAutomationId;
    private const string BackstagePrintPaneId = FreeXBackstageNavigationPlanner.PrintPaneAutomationId;

    private void InitializeBackstageFrame()
    {
        var frame = BackstageFrameComposer.Build(new BackstageFrameComposerSpec(
            new BackstageAccent(
                System.Windows.Media.Color.FromRgb(0x10, 0x25, 0x3A),
                System.Windows.Media.Color.FromRgb(0x1C, 0x3A, 0x55),
                System.Windows.Media.Color.FromRgb(0x24, 0x44, 0x5E),
                System.Windows.Media.Color.FromRgb(0x24, 0x44, 0x5E)),
            BuildBackstageEntries())
        {
            // FreeX's panes (SsHomeView/SsInfoView/SsPrintView) carry their own internal padding, so drop
            // the frame's default content inset to land them exactly where the hand-rolled rail did.
            ContentPadding = new Thickness(0),
            BackButton = new BackstageBackButtonSpec(
                AutomationId: "BackstageBackButton",
                AutomationName: UiText.Get("MainWindow_TooltipTitle_Back"),
                AutomationHelpText: UiText.Get("MainWindow_ToolTip_BackToWorkbook"),
                ToolTip: UiText.Get("MainWindow_ToolTip_BackToWorkbook"),
                TooltipTitle: UiText.Get("MainWindow_TooltipTitle_Back"),
                KeyTip: "B"),
            DecorateNavButtons = DecorateBackstageNavButton,
            Closed = OnBackstageFrameClosed
        });

        StartScreenFrameHost.Content = frame;
        _backstageFrame = frame;
    }

    private void DecorateBackstageNavButton(BackstageEntry? entry, Button button)
    {
        // The shared frame stamps the shared RibbonTooltip attached properties on each nav button, but
        // FreeX's Alt-keytip overlay (MainWindow.KeyTips.cs) reads FreeX's own RibbonTooltip attached
        // properties. Mirror key-tip/title/description onto the FreeX properties so the rail still lights up
        // under Alt and shows the Excel-style hover card, exactly as the hand-rolled rail did.
        var keyTip = entry?.KeyTip ?? "B"; // null entry == back arrow
        var title = entry?.TooltipTitle ?? UiText.Get("MainWindow_TooltipTitle_Back");
        RibbonTooltip.SetKeyTip(button, keyTip);
        RibbonTooltip.SetTitle(button, title);
        if (entry?.TooltipDescription is { } description)
            RibbonTooltip.SetDescription(button, description);
    }

    private void OnBackstageFrameClosed()
    {
        // The frame closes itself (Esc / back arrow / an action entry). Funnel that through HideStartScreen
        // so the overlay collapses and worksheet focus is restored, exactly as the old rail did.
        StartScreenOverlay.Visibility = Visibility.Collapsed;
        SheetGrid.Focus();
    }

    private IEnumerable<BackstageEntry> BuildBackstageEntries()
    {
        // Pane entries swap the content host to the existing FreeX pane after running its refresh logic;
        // command entries fire an existing handler and the frame closes itself first (matching FreeW).
        // IconCommandName routes each rail glyph to FreeX's Office SVG of that name (the same CommandName
        // the old XAML RibbonIcon used); Icon is the geometry fallback.
        return FreeXBackstageNavigationPlanner.Build().Select(MapBackstageNavigationEntry);
    }

    private BackstageEntry MapBackstageNavigationEntry(FreeXBackstageNavigationEntry entry)
    {
        if (entry.Kind == FreeXBackstageNavigationEntryKind.Divider)
            return BackstageEntry.Divider(entry.DockBottom);

        var label = ResolveBackstageText(entry.LabelKey);
        var automationName = ResolveOptionalBackstageText(entry.AutomationNameKey);
        var automationHelpText = ResolveOptionalBackstageText(entry.AutomationHelpTextKey);
        var tooltipTitle = ResolveOptionalBackstageText(entry.TooltipTitleKey);
        var tooltipDescription = ResolveOptionalBackstageText(entry.TooltipDescriptionKey);

        return entry.Kind switch
        {
            FreeXBackstageNavigationEntryKind.Pane => BackstageEntry.Pane(
                label,
                entry.Icon,
                ResolveBackstagePane(entry.Pane!.Value),
                entry.DockBottom,
                entry.KeyTip,
                entry.AutomationId,
                automationName,
                automationHelpText,
                tooltipTitle,
                tooltipDescription,
                entry.IconCommandName),

            FreeXBackstageNavigationEntryKind.Command => BackstageEntry.Command(
                label,
                entry.Icon,
                ResolveBackstageCommand(entry.Command!.Value),
                entry.DockBottom,
                entry.KeyTip,
                entry.AutomationId,
                automationName,
                automationHelpText,
                tooltipTitle,
                tooltipDescription,
                entry.IconCommandName),

            _ => throw new InvalidOperationException($"Unsupported Backstage entry kind '{entry.Kind}'.")
        };
    }

    private static string ResolveBackstageText(string? key) =>
        key is null ? string.Empty : UiText.Get(key);

    private static string? ResolveOptionalBackstageText(string? key) =>
        key is null ? null : UiText.Get(key);

    private Func<UIElement> ResolveBackstagePane(FreeXBackstagePaneId pane) =>
        pane switch
        {
            FreeXBackstagePaneId.Home => BuildHomePane,
            FreeXBackstagePaneId.Info => BuildInfoPane,
            FreeXBackstagePaneId.Print => BuildPrintPane,
            _ => throw new InvalidOperationException($"Unsupported Backstage pane '{pane}'.")
        };

    private Action ResolveBackstageCommand(FreeXBackstageCommandId command) =>
        command switch
        {
            FreeXBackstageCommandId.New => async () => await RequestNewWorkbookAsync(),
            FreeXBackstageCommandId.Open => () => OpenButton_Click(this, new RoutedEventArgs()),
            FreeXBackstageCommandId.Share => async () => await ShareWorkbookAsync(),
            FreeXBackstageCommandId.Save => () => SaveButton_Click(this, new RoutedEventArgs()),
            FreeXBackstageCommandId.SaveAs => () => SaveAsButton_Click(this, new RoutedEventArgs()),
            FreeXBackstageCommandId.Export => () => ExportPdfButton_Click(this, new RoutedEventArgs()),
            FreeXBackstageCommandId.Close => Close,
            FreeXBackstageCommandId.Account => () => SsAccountBtn_Click(this, new RoutedEventArgs()),
            FreeXBackstageCommandId.Options => () => SsOptionsBtn_Click(this, new RoutedEventArgs()),
            _ => throw new InvalidOperationException($"Unsupported Backstage command '{command}'.")
        };

    // ── Pane content factories ──────────────────────────────────────────────────
    // Each runs the same live-refresh the old Show*View methods did, then hands the existing pane element to
    // the frame (after detaching it from its current parent — a WPF element has exactly one logical parent).

    private UIElement BuildHomePane()
    {
        UpdateSsGreeting();
        SwitchToRecentTab();
        UpdateSsRecentList();
        return ReparentForBackstage(SsHomeView);
    }

    private UIElement BuildInfoPane()
    {
        UpdateInfoView();
        return ReparentForBackstage(SsInfoView);
    }

    private UIElement BuildPrintPane()
    {
        var activeSheet = _workbook.GetSheet(_currentSheetId);
        _backstagePrintPreviewSettings = new PrintPreviewSettings();
        ConfigureBackstagePrintOptions(activeSheet);
        RefreshBackstagePrintPreview();
        var pane = ReparentForBackstage(SsPrintView);
        // The print pane lands focus on Print Now (Ctrl+P / the screenshot tour rely on this).
        Dispatcher.BeginInvoke(() =>
        {
            SsBackstagePrintNowButton.Focus();
            System.Windows.Input.Keyboard.Focus(SsBackstagePrintNowButton);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
        return pane;
    }

    // Make a pane visible (the holder kept them collapsed) and detach it from whatever parent currently
    // owns it, so the frame's ContentControl can adopt it without a "already has a logical parent" error.
    private static UIElement ReparentForBackstage(FrameworkElement pane)
    {
        Detach(pane);
        pane.Visibility = Visibility.Visible;
        return pane;
    }

    // Remove a FrameworkElement from its current logical/visual parent, handling the parent shapes the
    // backstage panes can sit under (Panel / ContentControl / ContentPresenter / Decorator / ItemsControl).
    private static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, element):
                contentControl.Content = null;
                break;
            case ContentPresenter presenter when ReferenceEquals(presenter.Content, element):
                presenter.Content = null;
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
            case ItemsControl itemsControl:
                itemsControl.Items.Remove(element);
                break;
        }
    }
}
