using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using AvaloniaGrid = global::Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static readonly FreeXBackstageFramePlan LiveBackstageFramePlan =
        FreeXBackstageFramePlanner.Build();
    private static readonly FreeXBackstageHomePanePlan LiveBackstageHomePanePlan =
        FreeXBackstageHomePanePlanner.Build();
    private AvaloniaBackstageFrame _backstageOverlay = null!;

    // WPF File-screen pane metrics (src/FreeX.App.Host/MainWindow.xaml, SsHomeView/SsInfoView/SsPrintView).
    // The shared frame already insets pane content by BackstageVisualContract.Frame.ContentPadding (40,28),
    // so the panes only add the difference to WPF's own 30 (Home) / 36 (Info, Print) heading top margin.
    private const double LiveBackstageHomeTopInset = 2;
    private const double LiveBackstagePaneTopInset = 8;
    private static readonly IBrush LiveBackstageInk = Brush(0x1F, 0x1F, 0x1F);
    private static readonly IBrush LiveBackstageBodyInk = Brush(0x55, 0x55, 0x55);
    private static readonly IBrush LiveBackstageDateInk = Brush(0x66, 0x66, 0x66);
    private static readonly IBrush LiveBackstageMutedInk = Brush(0x88, 0x88, 0x88);
    private static readonly IBrush LiveBackstageRuleBrush = Brush(0xE4, 0xE4, 0xE4);
    private static readonly IBrush LiveBackstageRowHoverBrush = Brush(0xF3, 0xF6, 0xF9);
    private static readonly IBrush LiveBackstageCardHoverBrush = Brush(0xEE, 0xF8, 0xFA);
    private static IBrush LiveBackstageAccentBrush => SheetTabContourBrush;
    // The FreeX product letter drawn on each recent-file tile (WPF's hard-coded "X"); a brand mark,
    // not translatable text.
    private const string LiveBackstageFileTileGlyph = "X";

    // Chrome-less button template for the Home pane's card / tab / recent-row buttons: WPF replaces the
    // Button template there with a bare Border + ContentPresenter, so the Fluent button plate, border and
    // hover fill must not show through. The presenter is deliberately left unnamed so the theme's
    // PART_ContentPresenter pointer-over/pressed fills do not apply.
    private static readonly FuncControlTemplate<Button> LiveBackstageFlatButtonTemplate = new((button, _) =>
    {
        var presenter = new ContentPresenter();
        presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = button });
        presenter.Bind(Layoutable.HorizontalAlignmentProperty, new Binding(nameof(ContentControl.HorizontalContentAlignment)) { Source = button });
        presenter.Bind(Layoutable.VerticalAlignmentProperty, new Binding(nameof(ContentControl.VerticalContentAlignment)) { Source = button });

        var border = new Border { Child = presenter };
        border.Bind(Border.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = button });
        border.Bind(Border.PaddingProperty, new Binding(nameof(TemplatedControl.Padding)) { Source = button });
        return border;
    });

    // Home pane Recent/Pinned tab and search state. The Home pane is rebuilt from scratch whenever a
    // pin/unpin/remove action or a deferred existence probe re-activates it (TryActivateBackstagePane),
    // so the state lives on the window rather than in the pane; it resets each time Backstage opens,
    // like the WPF pane flow's ResetRecentTab.
    private bool _liveBackstageShowingPinnedList;
    private string _liveBackstageRecentFilter = string.Empty;
    private TextBox? _liveBackstageSearchBox;

    private Control BuildBackstageOverlay()
    {
        var entries = LiveBackstageFramePlan.Entries.Select(MapLiveBackstageEntry).ToArray();
        var backstageTheme = AvaloniaSisterBackstageTheme.FromTheme(
            App.ActiveTheme,
            tileWidth: 150,
            tileHeight: 190);
        _backstageOverlay = new AvaloniaBackstageFrame(
            backstageTheme.Accent,
            entries,
            AvaloniaBackstageRibbonChrome.Create(
                Free.Shared.Ribbon.RibbonCommandIconKind.WindowClose))
        {
            Margin = new Thickness(0, 0, 0, ResolveTokenDouble("FreeXStatusBarHeight", 28.0)),
            ZIndex = 1000,
        };
        AutomationProperties.SetAutomationId(_backstageOverlay, "FreeXBackstageOverlay");
        AutomationProperties.SetName(
            _backstageOverlay,
            UiText.CreateAutomationName(UiText.Get("MainWindow_Header_File")));
        _backstageOverlay.Closed += RestoreFocusAfterBackstageDismissal;

        // R140-backstage-shortcuts-leak-to-worksheet: AvaloniaBackstageFrame's own constructor
        // already registers a Tunnel+Bubble KeyDown handler on itself (handledEventsToo: true) that
        // claims Escape and rail navigation (Home/End/Up/Down) and marks those Handled -- but every
        // other key (Ctrl+Z, Delete, arrow-cell-navigation, letters, ...) falls straight through it
        // and used to keep tunnelling/bubbling past this overlay into MainWindow's worksheet
        // keyboard-shortcut dispatch, silently editing the hidden sheet underneath while Backstage
        // was open. Adding our own Tunnel handler here (registered after -- and so run after -- the
        // frame's own handler on this same element) blocks everything the frame didn't already
        // claim, before it can ever reach a focused rail control or bubble any further. Avalonia
        // skips handlers registered without handledEventsToo once Handled is already true, so this
        // is automatically a no-op for the nav keys the frame just handled.
        _backstageOverlay.AddHandler(
            InputElement.KeyDownEvent,
            BlockWorksheetKeyboardShortcutsWhileBackstageVisible,
            RoutingStrategies.Tunnel);

        return _backstageOverlay;
    }

    private void BlockWorksheetKeyboardShortcutsWhileBackstageVisible(object? sender, KeyEventArgs e)
    {
        if (_backstageOverlay.IsVisible)
            e.Handled = true;
    }

    private SisterBackstageEntryPlan<Control> MapLiveBackstageEntry(
        FreeXBackstageFrameEntryPlan entry)
    {
        var navigation = entry.Navigation;
        if (entry.Kind == FreeXBackstageNavigationEntryKind.Divider)
            return SisterBackstageEntryPlan<Control>.Divider(navigation.DockBottom);

        var label = StripDisplayMnemonic(
            FreeXBackstageTextValue.ResolveKey(navigation.LabelKey, UiText.Get));
        var mapped = entry.Kind switch
        {
            FreeXBackstageNavigationEntryKind.Pane => SisterBackstageEntryPlan<Control>.Pane(
                label,
                navigation.Icon!.Value,
                () => BuildLiveBackstagePane(RequireLiveBackstagePaneFlow(entry)),
                navigation.DockBottom,
                navigation.IconCommandName),

            FreeXBackstageNavigationEntryKind.Command => SisterBackstageEntryPlan<Control>.Command(
                label,
                navigation.Icon!.Value,
                BuildLiveBackstageCommandAction(RequireLiveBackstageCommandWorkflow(entry)),
                navigation.DockBottom,
                navigation.IconCommandName),

            _ => throw new InvalidOperationException(
                $"Unsupported Backstage entry kind '{entry.Kind}'."),
        };

        return mapped with
        {
            StableId = entry.StableId,
            KeyTip = navigation.KeyTip,
            AutomationId = navigation.AutomationId,
            AutomationName = FreeXBackstageTextValue.ResolveOptionalKey(
                navigation.AutomationNameKey,
                UiText.Get,
                StripDisplayMnemonic),
            AutomationHelpText = FreeXBackstageTextValue.ResolveOptionalKey(
                navigation.AutomationHelpTextKey,
                UiText.Get,
                StripDisplayMnemonic),
            TooltipTitle = FreeXBackstageTextValue.ResolveOptionalKey(
                navigation.TooltipTitleKey,
                UiText.Get,
                StripDisplayMnemonic),
            TooltipDescription = FreeXBackstageTextValue.ResolveOptionalKey(
                navigation.TooltipDescriptionKey,
                UiText.Get,
                StripDisplayMnemonic),
        };
    }

    private static FreeXBackstagePaneFlowPlan RequireLiveBackstagePaneFlow(
        FreeXBackstageFrameEntryPlan entry) =>
        entry.PaneFlow
        ?? throw new InvalidOperationException(
            $"Backstage pane entry '{entry.Navigation.LabelKey}' is missing a flow plan.");

    private static FreeXBackstageCommandWorkflowPlan RequireLiveBackstageCommandWorkflow(
        FreeXBackstageFrameEntryPlan entry) =>
        entry.CommandWorkflow
        ?? throw new InvalidOperationException(
            $"Backstage command entry '{entry.Navigation.LabelKey}' is missing a workflow plan.");

    private Action BuildLiveBackstageCommandAction(FreeXBackstageCommandWorkflowPlan workflow) =>
        async () =>
        {
            Action<FreeXBackstageCommandId>? activationOverride = null;
            ResolveBackstageCommandActivationOverride(ref activationOverride);
            if (activationOverride is not null)
            {
                activationOverride(workflow.Command);
                return;
            }

            await ExecuteBackstageCommandWorkflowAsync(workflow.Command);
        };

    partial void ResolveBackstageCommandActivationOverride(
        ref Action<FreeXBackstageCommandId>? handler);

    private Control BuildLiveBackstagePane(FreeXBackstagePaneFlowPlan flow) =>
        flow.Pane switch
        {
            FreeXBackstagePaneId.Home => BuildLiveBackstageHomePane(),
            FreeXBackstagePaneId.Info => BuildLiveBackstageInfoPane(),
            FreeXBackstagePaneId.Print => BuildLiveBackstagePrintPane(),
            _ => throw new InvalidOperationException($"Unsupported Backstage pane '{flow.Pane}'."),
        };

    private void ShowBackstageOverlay()
    {
        SetRibbonKeyTipsVisible(false);
        _liveBackstageShowingPinnedList = false;
        _liveBackstageRecentFilter = string.Empty;
        _liveBackstageSearchBox = null;
        _backstageOverlay.Show(FreeXBackstageFramePlanner.GetPaneStableId(
            LiveBackstageFramePlan.Selection.DefaultPane));
    }

    // WPF's Ctrl+P route opens the Backstage Print pane rather than jumping straight to the
    // standalone preview window. Keep the keyboard route on the same live pane so Preview and
    // Print remain available as the next explicit actions.
    private void ShowBackstagePrintPane()
    {
        SetRibbonKeyTipsVisible(false);
        _backstageOverlay.Show(
            FreeXBackstageFramePlanner.GetPaneStableId(FreeXBackstagePaneId.Print));
    }

    private void HideBackstageOverlay() => _backstageOverlay.Hide();

    private void RestoreFocusAfterBackstageDismissal() =>
        (_activeCellBorder as Control ?? _sheetGridHost).Focus();

    private bool TryActivateBackstagePane(FreeXBackstagePaneId pane) =>
        _backstageOverlay.TryActivateEntry(FreeXBackstageFramePlanner.GetPaneStableId(pane));

    private bool TryActivateBackstageCommand(FreeXBackstageCommandId command) =>
        _backstageOverlay.TryActivateEntry(FreeXBackstageFramePlanner.GetCommandStableId(command));

    private Control BuildLiveBackstageHomePane()
    {
        // A rebuild triggered while the user is typing in the search box (a deferred existence probe
        // resolving re-activates the pane) must not drop keyboard focus out of the box.
        var restoreSearchFocus = _liveBackstageSearchBox is { IsFocused: true };

        var content = CreateLiveBackstagePaneStack(LiveBackstageHomeTopInset);
        content.Children.Add(new TextBlock
        {
            Text = BackstageGreetingFormatter.FormatGreeting(DateTime.Now),
            FontFamily = FormulaBarFontFamily,
            FontSize = 28,
            FontWeight = FontWeight.Light,
            Foreground = LiveBackstageInk,
        });
        content.Children.Add(new TextBlock
        {
            Text = UiText.Get("Common_New"),
            FontFamily = FormulaBarFontFamily,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = LiveBackstageInk,
            Margin = new Thickness(0, 18, 0, 12),
        });
        content.Children.Add(BuildLiveBackstageBlankWorkbookCard());

        // ── Recent | Pinned tabs + search ────────────────────────────────────────
        var recentTabText = CreateLiveBackstageRecentTabText(LiveBackstageHomePanePlan.RecentTab);
        var pinnedTabText = CreateLiveBackstageRecentTabText(LiveBackstageHomePanePlan.PinnedTab);
        var recentTabUnderline = CreateLiveBackstageRecentTabUnderline(recentTabText);
        var pinnedTabUnderline = CreateLiveBackstageRecentTabUnderline(pinnedTabText);
        var recentTabButton = CreateLiveBackstageRecentTabButton(
            LiveBackstageHomePanePlan.RecentTab,
            recentTabUnderline,
            "SsRecentTabButton");
        var pinnedTabButton = CreateLiveBackstageRecentTabButton(
            LiveBackstageHomePanePlan.PinnedTab,
            pinnedTabUnderline,
            "SsPinnedTabButton");

        var searchBox = new TextBox
        {
            Text = _liveBackstageRecentFilter,
            Width = 198,
            Height = 26,
            MinHeight = 0,
            Padding = new Thickness(8, 3),
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            Background = Brush(0xF5, 0xF5, 0xF5),
            BorderBrush = Brush(0xD8, 0xD8, 0xD8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(searchBox, "SsSearchBox");
        AutomationProperties.SetName(
            searchBox,
            UiText.Get(LiveBackstageHomePanePlan.Search.AutomationNameKey));
        AutomationProperties.SetHelpText(
            searchBox,
            UiText.Get(LiveBackstageHomePanePlan.Search.AutomationHelpTextKey));
        _liveBackstageSearchBox = searchBox;

        var tabsRow = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            // WPF: the New section's 8 bottom padding plus the Recent grid's 24 top margin.
            Margin = new Thickness(0, 32, 0, 0),
        };
        tabsRow.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { recentTabButton, pinnedTabButton },
        });
        AvaloniaGrid.SetColumn(searchBox, 2);
        tabsRow.Children.Add(searchBox);
        content.Children.Add(tabsRow);

        // ── Name / Date modified column headers ──────────────────────────────────
        // Same column widths as the file rows below so "Date modified" sits over the dates.
        var headerGrid = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(150) },
                new ColumnDefinition { Width = new GridLength(36) },
            },
        };
        foreach (var column in LiveBackstageHomePanePlan.Columns)
        {
            var header = new TextBlock
            {
                Text = UiText.Get(column.LabelKey),
                FontFamily = FormulaBarFontFamily,
                FontSize = 11,
                Foreground = LiveBackstageMutedInk,
            };
            AvaloniaGrid.SetColumn(header, column.Id == FreeXBackstageRecentColumnId.DateModified ? 1 : 0);
            headerGrid.Children.Add(header);
        }

        content.Children.Add(new Border
        {
            BorderBrush = LiveBackstageRuleBrush,
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(0, 5),
            Child = headerGrid,
        });

        var fileList = new StackPanel();
        content.Children.Add(fileList);

        void ApplyTabChrome()
        {
            ApplyLiveBackstageRecentTabChrome(recentTabUnderline, recentTabText, !_liveBackstageShowingPinnedList);
            ApplyLiveBackstageRecentTabChrome(pinnedTabUnderline, pinnedTabText, _liveBackstageShowingPinnedList);
        }

        void PopulateFileList()
        {
            // Filter out moved/deleted recent files the same way the WPF backstage and this app's own
            // native Open-Recent menu already do (see MainWindow.cs's OpenRecentWorkbookMenuPlanner.Create
            // call), rather than silently listing entries that will fail to open. Routed through the
            // cache (not a raw File.Exists) so an unreachable UNC/network entry never blocks the UI
            // thread while the Home pane is built or the search box is typed into.
            // Reload from disk rather than reading the constructor-time _recentFiles field: with
            // multiple windows sharing this process (View > New Window), a sibling window may have
            // registered/pinned/removed an entry since this window loaded, and this window's cached
            // instance would never observe it otherwise (see MainWindow.cs's ReloadRecentFilesStore).
            var plan = BackstageRecentFileListPlanner.Build(
                ReloadRecentFilesStore().Snapshot(),
                _liveBackstageRecentFilter,
                _recentFilePathExistenceCache.Exists);
            var entries = _liveBackstageShowingPinnedList ? plan.PinnedItems : plan.RecentItems;

            fileList.Children.Clear();
            if (entries.Count == 0)
            {
                // WPF leaves an empty Pinned list blank; only the Recent list explains itself.
                if (!_liveBackstageShowingPinnedList)
                {
                    fileList.Children.Add(new TextBlock
                    {
                        Text = UiText.Get("Backstage_Home_NoRecentWorkbooks"),
                        FontFamily = FormulaBarFontFamily,
                        FontSize = 13,
                        Foreground = LiveBackstageMutedInk,
                        Margin = new Thickness(0, 14, 0, 0),
                    });
                }

                return;
            }

            foreach (var entry in entries)
                fileList.Children.Add(BuildLiveBackstageRecentRow(entry));
        }

        void SelectTab(BackstageRecentTab tab)
        {
            var selection = BackstageTabSelectionPlanner.Select(_liveBackstageShowingPinnedList, tab);
            if (!selection.Changed)
                return;

            _liveBackstageShowingPinnedList = selection.PinnedListVisible;
            ApplyTabChrome();
            PopulateFileList();
        }

        recentTabButton.Click += (_, _) => SelectTab(BackstageRecentTab.Recent);
        pinnedTabButton.Click += (_, _) => SelectTab(BackstageRecentTab.Pinned);
        searchBox.TextChanged += (_, _) =>
        {
            var filter = searchBox.Text ?? string.Empty;
            if (string.Equals(filter, _liveBackstageRecentFilter, StringComparison.Ordinal))
                return;

            _liveBackstageRecentFilter = filter;
            PopulateFileList();
        };

        ApplyTabChrome();
        PopulateFileList();

        if (restoreSearchFocus)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(_liveBackstageSearchBox, searchBox))
                    return;

                searchBox.Focus();
                searchBox.CaretIndex = searchBox.Text?.Length ?? 0;
            });
        }

        return content;
    }

    // WPF "Blank workbook" card: a 144x108 mini-worksheet thumbnail (header strips, grid lines, selected
    // A1) with the caption beneath, on a transparent 4px plate that tints on hover.
    private Button BuildLiveBackstageBlankWorkbookCard()
    {
        var thumbnail = new Canvas { Width = 144, Height = 108 };
        var headerFill = Brush(0xF0, 0xF0, 0xF0);
        // WPF draws these as 0.5px #DCDCDC strokes; a 1px line in the equivalent blended tone.
        var gridLine = Brush(0xED, 0xED, 0xED);
        thumbnail.Children.Add(CreateLiveBackstageThumbnailBlock(0, 0, 22, 108, headerFill));
        thumbnail.Children.Add(CreateLiveBackstageThumbnailBlock(0, 0, 144, 17, headerFill));
        foreach (var y in new double[] { 35, 53, 71, 89 })
            thumbnail.Children.Add(CreateLiveBackstageThumbnailBlock(0, y, 144, 1, gridLine));
        foreach (var x in new double[] { 54, 84, 114 })
            thumbnail.Children.Add(CreateLiveBackstageThumbnailBlock(x, 17, 1, 91, gridLine));
        var selectedCell = CreateLiveBackstageThumbnailBlock(22, 17, 32, 18, Brush(0xE6, 0xF6, 0xFA));
        selectedCell.BorderBrush = LiveBackstageAccentBrush;
        selectedCell.BorderThickness = new Thickness(1.5);
        thumbnail.Children.Add(selectedCell);

        var card = new Button
        {
            Template = LiveBackstageFlatButtonTemplate,
            Padding = new Thickness(4),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = new StackPanel
            {
                Width = 144,
                Children =
                {
                    new Border
                    {
                        Width = 144,
                        Height = 108,
                        Background = Brushes.White,
                        BorderBrush = Brush(0xC8, 0xC8, 0xC8),
                        BorderThickness = new Thickness(1),
                        ClipToBounds = true,
                        Child = thumbnail,
                    },
                    new TextBlock
                    {
                        Text = UiText.Get("MainWindow_Text_BlankWorkbook"),
                        FontFamily = FormulaBarFontFamily,
                        FontSize = 12,
                        Foreground = LiveBackstageInk,
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, 7, 0, 2),
                    },
                },
            },
        };
        ApplyLiveBackstageHoverPlate(card, "live-backstage-card", LiveBackstageCardHoverBrush);
        ToolTip.SetTip(card, UiText.Get("MainWindow_TooltipTitle_BlankWorkbook"));
        AutomationProperties.SetAutomationId(card, "BackstageBlankWorkbookButton");
        AutomationProperties.SetName(card, UiText.Get("MainWindow_Text_BlankWorkbook"));
        card.Click += (_, _) => RunGuarded(async () =>
        {
            HideBackstageOverlay();
            await ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.New);
        });
        return card;
    }

    private static Border CreateLiveBackstageThumbnailBlock(
        double left,
        double top,
        double width,
        double height,
        IBrush fill)
    {
        var block = new Border
        {
            Width = width,
            Height = height,
            Background = fill,
        };
        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, top);
        return block;
    }

    // Transparent-at-rest / tinted-on-hover plate, expressed as styles on the control itself so the
    // pointer-over fill is not shadowed by a local Background value.
    private static void ApplyLiveBackstageHoverPlate(StyledElement target, string styleClass, IBrush hoverBrush)
    {
        var backgroundProperty = target is Panel
            ? (AvaloniaProperty)Panel.BackgroundProperty
            : TemplatedControl.BackgroundProperty;
        target.Classes.Add(styleClass);
        target.Styles.Add(new Style(selector => selector.Class(styleClass))
        {
            Setters = { new Setter(backgroundProperty, Brushes.Transparent) },
        });
        target.Styles.Add(new Style(selector => selector.Class(styleClass).Class(":pointerover"))
        {
            Setters = { new Setter(backgroundProperty, hoverBrush) },
        });
    }

    private static TextBlock CreateLiveBackstageRecentTabText(FreeXBackstageRecentTabDescriptor descriptor) =>
        new()
        {
            Text = UiText.Get(descriptor.LabelKey),
            FontFamily = FormulaBarFontFamily,
            FontSize = 13,
        };

    private static Border CreateLiveBackstageRecentTabUnderline(TextBlock label) =>
        new()
        {
            Padding = new Thickness(0, 8, 22, 8),
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush = Brushes.Transparent,
            Child = label,
        };

    private static Button CreateLiveBackstageRecentTabButton(
        FreeXBackstageRecentTabDescriptor descriptor,
        Border underline,
        string automationId)
    {
        var tab = new Button
        {
            Template = LiveBackstageFlatButtonTemplate,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = underline,
        };
        AutomationProperties.SetAutomationId(tab, automationId);
        AutomationProperties.SetName(tab, UiText.Get(descriptor.LabelKey));
        ToolTip.SetTip(tab, UiText.Get(descriptor.TooltipTitleKey));
        return tab;
    }

    private static void ApplyLiveBackstageRecentTabChrome(Border underline, TextBlock label, bool isActive)
    {
        underline.BorderBrush = isActive ? LiveBackstageAccentBrush : Brushes.Transparent;
        label.FontWeight = isActive ? FontWeight.SemiBold : FontWeight.Normal;
        label.Foreground = isActive ? LiveBackstageAccentBrush : LiveBackstageMutedInk;
    }

    private Control BuildLiveBackstageRecentRow(RecentFileViewModel entry)
    {
        var row = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(150) },
                new ColumnDefinition { Width = new GridLength(36) },
            },
            MinHeight = 56,
            Focusable = true,
        };
        ApplyLiveBackstageHoverPlate(row, "live-backstage-recent-row", LiveBackstageRowHoverBrush);

        AvaloniaManagedContextMenu.Attach(
            row,
            () => AvaloniaBackstageRecentFileContextMenu.BuildItems(
                entry.IsPinned,
                entry.FileName,
                UiText.Get,
                action => ApplyBackstageRecentFileAction(entry.Path, action)));
        var rowDescriptor = LiveBackstageHomePanePlan.Rows.Single(descriptor =>
            descriptor.Kind == (entry.IsPinned
                ? FreeXBackstageRecentFileRowKind.Pinned
                : FreeXBackstageRecentFileRowKind.Recent));
        AutomationProperties.SetAutomationId(row, rowDescriptor.AutomationId);

        // File tile + name over directory + date, as one open button spanning the name and date columns.
        var directory = new TextBlock
        {
            Text = entry.DisplayDirectory,
            FontFamily = FormulaBarFontFamily,
            FontSize = 11,
            Foreground = LiveBackstageMutedInk,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 0),
        };
        ToolTip.SetTip(directory, entry.Directory);
        var nameStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = entry.FileName,
                    FontFamily = FormulaBarFontFamily,
                    FontSize = 13,
                    Foreground = LiveBackstageInk,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
                directory,
            },
        };
        AvaloniaGrid.SetColumn(nameStack, 1);

        var date = new TextBlock
        {
            Text = entry.LastOpenedText,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            Foreground = LiveBackstageDateInk,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AvaloniaGrid.SetColumn(date, 2);

        var openContent = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(150) },
            },
        };
        openContent.Children.Add(new Border
        {
            Width = 32,
            Height = 36,
            Background = LiveBackstageAccentBrush,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = LiveBackstageFileTileGlyph,
                FontFamily = FormulaBarFontFamily,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        openContent.Children.Add(nameStack);
        openContent.Children.Add(date);

        var openButton = new Button
        {
            Template = LiveBackstageFlatButtonTemplate,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 10),
            MinHeight = 56,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = openContent,
        };
        AvaloniaGrid.SetColumnSpan(openButton, 2);
        ToolTip.SetTip(openButton, entry.Path);
        AutomationProperties.SetAutomationId(openButton, "BackstageRecentFileButton");
        AutomationProperties.SetName(openButton, entry.OpenAutomationName);
        AutomationProperties.SetHelpText(openButton, entry.OpenAutomationHelpText);
        AvaloniaManagedContextMenu.Attach(
            openButton,
            () => AvaloniaBackstageRecentFileContextMenu.BuildItems(
                entry.IsPinned,
                entry.FileName,
                UiText.Get,
                action => ApplyBackstageRecentFileAction(entry.Path, action)));
        openButton.Click += (_, _) => RunGuarded(async () =>
        {
            HideBackstageOverlay();
            await OpenRecentWorkbookAsync(entry.Path, entry.FileAccessIdentity);
        });
        row.Children.Add(openButton);

        var pin = new Button
        {
            Template = LiveBackstageFlatButtonTemplate,
            Content = AvaloniaRibbonIcons.BuildMonochrome(
                Free.Shared.Ribbon.RibbonCommandIconKind.Pin,
                16,
                entry.IsPinned ? "unpin-from-list" : "pin-to-list",
                entry.IsPinned ? LiveBackstageAccentBrush : LiveBackstageDateInk),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 36,
            Height = 36,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        var pinCommand = LiveBackstageHomePanePlan.RowCommands.Single(command =>
            command.Id == (entry.IsPinned
                ? FreeXBackstageRecentFileCommandId.Unpin
                : FreeXBackstageRecentFileCommandId.Pin));
        ToolTip.SetTip(pin, entry.PinAutomationName);
        AutomationProperties.SetAutomationId(pin, pinCommand.AutomationId);
        AutomationProperties.SetName(pin, entry.PinAutomationName);
        AutomationProperties.SetHelpText(pin, entry.PinAutomationHelpText);
        pin.Click += (_, _) => ApplyBackstageRecentFileAction(
            entry.Path,
            entry.IsPinned ? BackstageRecentFileMenuAction.Unpin : BackstageRecentFileMenuAction.Pin);
        AvaloniaGrid.SetColumn(pin, 2);
        row.Children.Add(pin);

        return row;
    }

    private Control BuildLiveBackstageInfoPane()
    {
        var display = WorkbookInfoDisplayPlanner.Build(
            BuildWorkbookInfoPlan(),
            WorkbookInfoDisplaySurface.AvaloniaBackstageInfoDialog,
            AvaloniaPlannerTextResources.Text);
        var pane = FreeXBackstageInfoPanePlanner.Build(
            FreeXBackstageInfoSurface.AvaloniaLivePane,
            CreateBackstageInfoPaneRequest(display));
        var content = CreateLiveBackstagePaneStack(LiveBackstagePaneTopInset);
        content.Children.Add(CreateLiveBackstageHeading(UiText.Get(pane.TitleKey)));
        content.Children.Add(CreateLiveBackstageSection(UiText.Get(pane.PropertiesHeadingKey)));
        foreach (var detail in pane.Details)
        {
            content.Children.Add(CreateLiveBackstageDetail(
                UiText.Get(detail.LabelKey),
                detail.Value.Resolve(UiText.Get)));
        }

        content.Children.Add(CreateLiveBackstageSection(UiText.Get(pane.ProtectionSectionHeaderKey)));
        content.Children.Add(CreateLiveBackstageDetail(
            UiText.Get("Backstage_LiveInfo_WorkbookLabel"),
            pane.WorkbookProtectionSummary.Resolve(UiText.Get)));
        content.Children.Add(CreateLiveBackstageDetail(
            UiText.Get("Backstage_LiveInfo_ActiveSheetLabel"),
            pane.ActiveSheetProtectionSummary.Resolve(UiText.Get)));
        content.Children.Add(CreateLiveBackstageSection(UiText.Get(pane.StatisticsSectionHeaderKey)));
        content.Children.Add(new TextBlock
        {
            Text = pane.StatisticsSummary.Resolve(UiText.Get),
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            LineHeight = 18,
            Foreground = LiveBackstageBodyInk,
            TextWrapping = TextWrapping.Wrap,
        });
        if (pane.UnsavedChangesNote is { } unsavedChangesNote)
        {
            content.Children.Add(new TextBlock
            {
                Text = unsavedChangesNote.Resolve(UiText.Get),
                FontFamily = FormulaBarFontFamily,
                FontSize = 12,
                Foreground = LiveBackstageBodyInk,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0),
            });
        }

        return content;
    }

    private Control BuildLiveBackstagePrintPane()
    {
        var content = CreateLiveBackstagePaneStack(LiveBackstagePaneTopInset);
        content.Children.Add(CreateLiveBackstageHeading(UiText.Get("MainWindow_Text_Print")));
        content.Children.Add(new TextBlock
        {
            Text = UiText.Get("Backstage_Print_Description"),
            FontFamily = FormulaBarFontFamily,
            FontSize = 13,
            LineHeight = 20,
            Foreground = LiveBackstageInk,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 16, 0, 0),
        };
        actions.Children.Add(CreateLiveBackstageActionButton(
            UiText.Get("ShellLoc_PrintPreviewTitle"),
            "BackstagePrintPreviewButton",
            async () => await ShowPrintPreviewDialogAsync()));
        actions.Children.Add(CreateLiveBackstageActionButton(
            UiText.Get("MainWindow_Text_Print"),
            "BackstagePrintNowButton",
            async () => await ShowPrintDialogAsync()));
        content.Children.Add(actions);
        return content;
    }

    private Button CreateLiveBackstageActionButton(
        string label,
        string automationId,
        Func<Task> action)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 112,
            // WPF's Backstage Print button is MinHeight 34 with the 13px pane body size.
            MinHeight = 34,
            Padding = new Thickness(14, 7),
            FontFamily = FormulaBarFontFamily,
            FontSize = 13,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) => RunGuarded(async () =>
        {
            HideBackstageOverlay();
            await action();
        });
        return button;
    }

    // Panes stretch across the frame's content host like the WPF panes do; the frame already supplies
    // the 40px side / 28px top-bottom inset, so only WPF's extra heading top margin is added here.
    private static StackPanel CreateLiveBackstagePaneStack(double topInset) =>
        new()
        {
            Margin = new Thickness(0, topInset, 0, 0),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };

    // WPF pane heading (SsInfoView/SsPrintView): 28px Light #1F1F1F, 24px above the pane body.
    private static TextBlock CreateLiveBackstageHeading(string text) =>
        new()
        {
            Text = text,
            FontFamily = FormulaBarFontFamily,
            FontSize = 28,
            FontWeight = FontWeight.Light,
            Foreground = LiveBackstageInk,
            Margin = new Thickness(0, 0, 0, 24),
        };

    // WPF Info "Properties" section header: 13px SemiBold #555.
    private static TextBlock CreateLiveBackstageSection(string text) =>
        new()
        {
            Text = text,
            FontFamily = FormulaBarFontFamily,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = LiveBackstageBodyInk,
            Margin = new Thickness(0, 8, 0, 14),
        };

    // WPF Info property: an 11px #888 caption stacked over the 13px #1F1F1F value, 12px apart.
    private static Control CreateLiveBackstageDetail(string label, string value) =>
        new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    FontFamily = FormulaBarFontFamily,
                    FontSize = 11,
                    Foreground = LiveBackstageMutedInk,
                    Margin = new Thickness(0, 0, 0, 2),
                },
                new TextBlock
                {
                    Text = value,
                    FontFamily = FormulaBarFontFamily,
                    FontSize = 13,
                    Foreground = LiveBackstageInk,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };
}
