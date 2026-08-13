using System.Windows;
using System.Windows.Controls;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.PageLayout;

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
    private static readonly FreeXBackstageFramePlan BackstageFramePlan = FreeXBackstageFramePlanner.Build();

    private BackstageFrame? _backstageFrame;

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
            Chrome = BackstageRibbonChrome.Create(),
            DecorateNavButtons = DecorateBackstageNavButton,
            Closed = OnBackstageFrameClosed
        });

        StartScreenFrameHost.Content = frame;
        _backstageFrame = frame;
    }

    private void DecorateBackstageNavButton(BackstageEntry? entry, Button button)
    {
        // The shared frame stamps RibbonTooltip metadata on each nav button. Reapply localized values here
        // so FreeX's Alt overlay and hover card keep the same key tips and copy as the hand-rolled rail.
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
        // Pane entries swap the content host to an existing FreeX pane; command entries resolve to existing
        // WPF handlers. The presentation frame plan owns ordering, selection targets, refresh policy, and
        // command workflow classification.
        return BackstageFramePlan.Entries.Select(MapBackstageFrameEntry);
    }

    private BackstageEntry MapBackstageFrameEntry(FreeXBackstageFrameEntryPlan entry)
    {
        var navigation = entry.Navigation;
        if (navigation.Kind == FreeXBackstageNavigationEntryKind.Divider)
            return WpfBackstageEntryProjection.FromPlan(
                SisterBackstageEntryPlan<UIElement>.Divider(navigation.DockBottom));

        var label = FreeXBackstageTextValue.ResolveKey(navigation.LabelKey, UiText.Get);
        var automationName = FreeXBackstageTextValue.ResolveOptionalKey(navigation.AutomationNameKey, UiText.Get);
        var automationHelpText = FreeXBackstageTextValue.ResolveOptionalKey(navigation.AutomationHelpTextKey, UiText.Get);
        var tooltipTitle = FreeXBackstageTextValue.ResolveOptionalKey(navigation.TooltipTitleKey, UiText.Get);
        var tooltipDescription = FreeXBackstageTextValue.ResolveOptionalKey(navigation.TooltipDescriptionKey, UiText.Get);

        var mapped = navigation.Kind switch
        {
            FreeXBackstageNavigationEntryKind.Pane => SisterBackstageEntryPlan<UIElement>.Pane(
                label,
                navigation.Icon!.Value,
                () => BuildBackstagePane(RequirePaneFlow(entry)),
                navigation.DockBottom,
                navigation.IconCommandName),

            FreeXBackstageNavigationEntryKind.Command => SisterBackstageEntryPlan<UIElement>.Command(
                label,
                navigation.Icon!.Value,
                ResolveBackstageCommand(RequireCommandWorkflow(entry)),
                navigation.DockBottom,
                navigation.IconCommandName),

            _ => throw new InvalidOperationException($"Unsupported Backstage entry kind '{navigation.Kind}'.")
        };

        return WpfBackstageEntryProjection.FromPlan(mapped with
        {
            StableId = entry.StableId,
            KeyTip = navigation.KeyTip,
            AutomationId = navigation.AutomationId,
            AutomationName = automationName,
            AutomationHelpText = automationHelpText,
            TooltipTitle = tooltipTitle,
            TooltipDescription = tooltipDescription,
        });
    }

    private static FreeXBackstagePaneFlowPlan RequirePaneFlow(FreeXBackstageFrameEntryPlan entry) =>
        entry.PaneFlow
        ?? throw new InvalidOperationException($"Backstage pane entry '{entry.Navigation.LabelKey}' is missing a flow plan.");

    private static FreeXBackstageCommandWorkflowPlan RequireCommandWorkflow(FreeXBackstageFrameEntryPlan entry) =>
        entry.CommandWorkflow
        ?? throw new InvalidOperationException($"Backstage command entry '{entry.Navigation.LabelKey}' is missing a workflow plan.");

    private Action ResolveBackstageCommand(FreeXBackstageCommandWorkflowPlan plan) =>
        async () => await FreeXBackstageCommandWorkflowExecutor.ExecuteAsync(
            plan,
            CreateBackstageCommandHandlers());

    private FreeXBackstageCommandHandlers CreateBackstageCommandHandlers() =>
        new(
            NewWorkbookAsync: RequestNewWorkbookAsync,
            OpenWorkbookAsync: () => RunBackstageCommand(() => OpenButton_Click(this, new RoutedEventArgs())),
            ShareWorkbookAsync: ShareWorkbookAsync,
            SaveWorkbookAsync: () => RunBackstageCommand(() => SaveButton_Click(this, new RoutedEventArgs())),
            SaveWorkbookAsAsync: () => RunBackstageCommand(() => SaveAsButton_Click(this, new RoutedEventArgs())),
            ExportWorkbookAsync: () => RunBackstageCommand(() => ExportPdfButton_Click(this, new RoutedEventArgs())),
            CloseWorkbookAsync: () => RunBackstageCommand(Close),
            AccountAsync: () => RunBackstageCommand(() => SsAccountBtn_Click(this, new RoutedEventArgs())),
            OptionsAsync: () => RunBackstageCommand(() => SsOptionsBtn_Click(this, new RoutedEventArgs())));

    private static Task RunBackstageCommand(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    // ── Pane content factories ──────────────────────────────────────────────────
    // Each runs the same live-refresh the old Show*View methods did, then hands the existing pane element to
    // the frame (after detaching it from its current parent — a WPF element has exactly one logical parent).

    private UIElement BuildBackstagePane(FreeXBackstagePaneFlowPlan plan)
    {
        ApplyBackstagePaneFlow(plan);
        var element = ReparentForBackstage(ResolveBackstagePaneElement(plan.Pane));
        ApplyBackstagePaneFocus(plan);
        return element;
    }

    private void ApplyBackstagePaneFlow(FreeXBackstagePaneFlowPlan plan)
    {
        if (plan.RefreshGreeting)
            UpdateSsGreeting();

        if (plan.ResetRecentTab)
            SwitchToRecentTab();

        if (plan.RefreshRecentFiles)
            UpdateSsRecentList();

        if (plan.RefreshInfo)
            UpdateInfoView();

        if (!plan.RefreshPrintOptions && !plan.RefreshPrintPreview)
            return;

        var activeSheet = _workbook.GetSheet(_currentSheetId);
        if (plan.ResetPrintPreviewSettings)
            _backstagePrintPreviewSettings = new PrintPreviewSettings();
        if (plan.RefreshPrintOptions)
            ConfigureBackstagePrintOptions(activeSheet);
        if (plan.RefreshPrintPreview)
            RefreshBackstagePrintPreview();
    }

    private FrameworkElement ResolveBackstagePaneElement(FreeXBackstagePaneId pane) =>
        pane switch
        {
            FreeXBackstagePaneId.Home => SsHomeView,
            FreeXBackstagePaneId.Info => SsInfoView,
            FreeXBackstagePaneId.Print => SsPrintView,
            _ => throw new InvalidOperationException($"Unsupported Backstage pane '{pane}'.")
        };

    private void ApplyBackstagePaneFocus(FreeXBackstagePaneFlowPlan plan)
    {
        if (plan.FocusTarget != FreeXBackstagePaneFocusTarget.PrintNowButton)
            return;

        // The print pane lands focus on Print Now (Ctrl+P / the screenshot tour rely on this).
        Dispatcher.BeginInvoke(() =>
        {
            SsBackstagePrintNowButton.Focus();
            System.Windows.Input.Keyboard.Focus(SsBackstagePrintNowButton);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
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
