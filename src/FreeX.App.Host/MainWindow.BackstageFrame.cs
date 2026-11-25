using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
/// The content panes (<c>SsHomeView</c>/<c>SsOpenView</c>/<c>SsInfoView</c>/<c>SsPrintView</c>) are kept exactly as they
/// were in XAML, parked in a hidden holder inside <c>StartScreenOverlay</c>; each pane entry's
/// <see cref="BackstageEntry.ContentFactory"/> first runs that pane's live-refresh logic (the same calls the
/// old <c>Show*View</c> methods made) and then reparents the pane element into the frame's content host.
/// </summary>
public partial class MainWindow
{
    private static readonly FreeXBackstageFramePlan BackstageFramePlan = FreeXBackstageFramePlanner.Build();

    private BackstageFrame? _backstageFrame;
    private string _backstageOpenRecentFilter = string.Empty;

    private void InitializeBackstageFrame()
    {
        var frame = BackstageFrameComposer.Build(new BackstageFrameComposerSpec(
            // Excel's File screen uses a quiet neutral rail. Keep FreeX's workbook accent inside the
            // content panes, while this frame uses dense, readable chrome that does not compete with them.
            new BackstageAccent(
                Sidebar: Color.FromRgb(0xF3, 0xF4, 0xF6),
                Hover: Color.FromRgb(0xE5, 0xE9, 0xED),
                Selected: Color.FromRgb(0xD8, 0xE5, 0xE9),
                Separator: Color.FromRgb(0xD7, 0xDC, 0xE2),
                Foreground: Color.FromRgb(0x24, 0x2A, 0x31)),
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
            OptionsAsync: () => RunBackstageCommand(() => SsOptionsBtn_Click(this, new RoutedEventArgs())))
        {
            FeedbackAsync = () => RunBackstageCommand(() => FeedbackBtn_Click(this, new RoutedEventArgs())),
        };

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
            FreeXBackstagePaneId.Open => BuildBackstageOpenPane(),
            FreeXBackstagePaneId.Info => SsInfoView,
            FreeXBackstagePaneId.Print => SsPrintView,
            _ => throw new InvalidOperationException($"Unsupported Backstage pane '{pane}'.")
        };

    private FrameworkElement BuildBackstageOpenPane()
    {
        var plan = FreeXBackstageOpenPanePlanner.Build();
        var content = new Grid { Margin = new Thickness(40, 30, 40, 24) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(new TextBlock
        {
            Text = UiText.Get(plan.TitleKey),
            FontSize = 28,
            FontWeight = FontWeights.Light,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)),
            Margin = new Thickness(0, 0, 0, 18),
        });

        var browse = new Button
        {
            Content = UiText.Get(plan.Locations.Single(location => location.Id == FreeXBackstageOpenLocationId.Browse).LabelKey),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(18, 9, 18, 9),
            Margin = new Thickness(0, 0, 0, 22),
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(browse, "BackstageOpenBrowseButton");
        browse.Click += (_, _) => RunGuardedUiCommand("Browse for Workbook", OpenFromBackstageAsync);
        Grid.SetRow(browse, 1);
        content.Children.Add(browse);

        var recent = plan.Locations.Single(location => location.Id == FreeXBackstageOpenLocationId.Recent);
        var recentHeader = new TextBlock
        {
            Text = UiText.Get(recent.LabelKey),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)),
        };
        var searchBox = new TextBox
        {
            Text = _backstageOpenRecentFilter,
            Width = 198,
            Height = 26,
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(searchBox, "BackstageOpenSearchBox");
        System.Windows.Automation.AutomationProperties.SetName(
            searchBox,
            UiText.Get(plan.SearchAutomationNameKey));

        var recentHeaderRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        recentHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        recentHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        System.Windows.Automation.AutomationProperties.SetAutomationId(recentHeader, recent.AutomationId);
        recentHeaderRow.Children.Add(recentHeader);
        Grid.SetColumn(searchBox, 1);
        recentHeaderRow.Children.Add(searchBox);
        Grid.SetRow(recentHeaderRow, 2);
        content.Children.Add(recentHeaderRow);

        var files = new StackPanel();
        void PopulateFiles()
        {
            var recentFiles = BackstageRecentFileListPlanner.Build(
                ReloadRecentFilesStore().Snapshot(),
                _backstageOpenRecentFilter,
                _recentFilePathExistenceCache.Exists).RecentItems;
            files.Children.Clear();
            var emptyStateKey = FreeXBackstageOpenPanePlanner.ResolveEmptyStateKey(
                !string.IsNullOrWhiteSpace(_backstageOpenRecentFilter),
                recentFiles.Count);
            if (emptyStateKey is not null)
            {
                var emptyState = new TextBlock
                {
                    Text = UiText.Get(emptyStateKey),
                    Margin = new Thickness(8, 14, 8, 0),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    TextWrapping = TextWrapping.Wrap,
                };
                System.Windows.Automation.AutomationProperties.SetAutomationId(emptyState, "BackstageOpenEmptyState");
                files.Children.Add(emptyState);
                return;
            }

            foreach (var file in recentFiles)
            {
                var button = new Button
                {
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Padding = new Thickness(8, 10, 8, 10),
                    MinHeight = 54,
                    Content = new DockPanel
                    {
                        LastChildFill = true,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = file.LastOpenedText,
                                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                                VerticalAlignment = VerticalAlignment.Center,
                                Width = 150,
                            },
                            new StackPanel
                            {
                                Children =
                                {
                                    new TextBlock { Text = file.FileName, FontSize = 13 },
                                    new TextBlock
                                    {
                                        Text = file.DisplayDirectory,
                                        FontSize = 11,
                                        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                                        TextTrimming = TextTrimming.CharacterEllipsis,
                                    },
                                },
                            },
                        },
                    },
                };
                System.Windows.Automation.AutomationProperties.SetAutomationId(button, "BackstageOpenRecentFileButton");
                System.Windows.Automation.AutomationProperties.SetName(button, file.OpenAutomationName);
                button.Click += (_, _) => RunGuardedUiCommand("Open Recent Workbook", () => OpenFileAsync(file.Path));
                files.Children.Add(button);
            }
        }

        searchBox.TextChanged += (_, _) =>
        {
            var filter = searchBox.Text ?? string.Empty;
            if (string.Equals(filter, _backstageOpenRecentFilter, StringComparison.Ordinal))
                return;

            _backstageOpenRecentFilter = filter;
            PopulateFiles();
        };
        PopulateFiles();

        var recentScroll = new ScrollViewer { Content = files, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        System.Windows.Automation.AutomationProperties.SetAutomationId(recentScroll, recent.AutomationId);
        Grid.SetRow(recentScroll, 3);
        content.Children.Add(recentScroll);
        return content;
    }

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
