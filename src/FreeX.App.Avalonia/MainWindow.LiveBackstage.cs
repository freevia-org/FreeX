using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using AvaloniaGrid = global::Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static readonly IBrush LiveBackstageRail = Brush(0x10, 0x25, 0x3A);
    private static readonly IBrush LiveBackstageRailHover = Brush(0x1D, 0x3B, 0x54);
    private static readonly IBrush LiveBackstageRailSelected = Brush(0x24, 0x44, 0x5E);
    private static readonly IBrush LiveBackstageSurface = Brush(0xFA, 0xFA, 0xFA);
    private readonly AvaloniaGrid _backstageOverlay = new();
    private readonly ContentControl _backstageContentHost = new();
    private readonly Dictionary<FreeXBackstagePaneId, Button> _backstagePaneButtons = [];
    private readonly Dictionary<FreeXBackstageCommandId, Button> _backstageCommandButtons = [];
    private FreeXBackstagePaneId _activeBackstagePane = FreeXBackstagePaneId.Home;

    internal bool IsBackstageOverlayVisibleForTest => _backstageOverlay.IsVisible;
    internal FreeXBackstagePaneId ActiveBackstagePaneForTest => _activeBackstagePane;
    internal Action<FreeXBackstageCommandId>? BackstageCommandActivationOverrideForTest { get; set; }
    internal Button? BackstagePaneButtonForTest(FreeXBackstagePaneId pane) =>
        _backstagePaneButtons.GetValueOrDefault(pane);
    internal Button? BackstageCommandButtonForTest(FreeXBackstageCommandId command) =>
        _backstageCommandButtons.GetValueOrDefault(command);

    private Control BuildBackstageOverlay()
    {
        _backstageOverlay.Background = LiveBackstageSurface;
        _backstageOverlay.IsVisible = false;
        _backstageOverlay.Focusable = true;
        _backstageOverlay.Margin = new Thickness(0, 0, 0, ResolveTokenDouble("FreeXStatusBarHeight", 28.0));
        _backstageOverlay.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
        _backstageOverlay.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _backstageOverlay.ZIndex = 1000;
        AutomationProperties.SetAutomationId(_backstageOverlay, "FreeXBackstageOverlay");
        AutomationProperties.SetName(_backstageOverlay, "File");

        var rail = BuildLiveBackstageRail();
        AvaloniaGrid.SetColumn(rail, 0);
        _backstageOverlay.Children.Add(rail);

        var contentScroll = new ScrollViewer
        {
            Background = LiveBackstageSurface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _backstageContentHost,
        };
        AvaloniaGrid.SetColumn(contentScroll, 1);
        _backstageOverlay.Children.Add(contentScroll);

        _backstageOverlay.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;

            HideBackstageOverlay();
            args.Handled = true;
        };

        return _backstageOverlay;
    }

    private Control BuildLiveBackstageRail()
    {
        var root = new DockPanel
        {
            LastChildFill = true,
            Background = LiveBackstageRail,
        };

        var bottom = new StackPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        var top = new StackPanel();
        root.Children.Add(top);

        var backButton = CreateLiveBackstageRailButton(
            label: string.Empty,
            icon: BackstageIconKind.Previous,
            iconCommandName: "Back",
            automationId: "BackstageBackButton");
        backButton.Height = 50;
        backButton.Click += (_, _) => HideBackstageOverlay();
        top.Children.Add(backButton);

        foreach (var entry in FreeXBackstageFramePlanner.Build().Entries)
        {
            var target = entry.Navigation.DockBottom ? bottom : top;
            if (entry.Kind == FreeXBackstageNavigationEntryKind.Divider)
            {
                target.Children.Add(new Border
                {
                    Height = 1,
                    Background = LiveBackstageRailSelected,
                    Margin = new Thickness(12, 6),
                });
                continue;
            }

            var navigation = entry.Navigation;
            var button = CreateLiveBackstageRailButton(
                StripDisplayMnemonic(UiText.Get(navigation.LabelKey!)),
                navigation.Icon!.Value,
                navigation.IconCommandName,
                navigation.AutomationId!);

            if (navigation.Pane is { } pane)
            {
                button.Tag = pane;
                button.Click += (_, _) => NavigateBackstageOverlay(pane);
                _backstagePaneButtons[pane] = button;
            }
            else if (navigation.Command is { } command)
            {
                button.Tag = command;
                button.Click += async (_, _) =>
                {
                    HideBackstageOverlay();
                    if (BackstageCommandActivationOverrideForTest is { } testOverride)
                    {
                        testOverride(command);
                        return;
                    }

                    await ExecuteBackstageCommandWorkflowAsync(command);
                };
                _backstageCommandButtons[command] = button;
            }

            target.Children.Add(button);
        }

        return root;
    }

    private Button CreateLiveBackstageRailButton(
        string label,
        BackstageIconKind icon,
        string? iconCommandName,
        string automationId)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(AvaloniaRibbonIcons.BuildMonochrome(
            MapBackstageIcon(icon),
            18,
            iconCommandName,
            Brushes.White));
        if (!string.IsNullOrEmpty(label))
        {
            content.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontFamily = FormulaBarFontFamily,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 9),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
        };
        button.PointerEntered += (_, _) =>
        {
            if (button.Tag is not FreeXBackstagePaneId pane || pane != _activeBackstagePane)
                button.Background = LiveBackstageRailHover;
        };
        button.PointerExited += (_, _) =>
        {
            if (button.Tag is not FreeXBackstagePaneId pane || pane != _activeBackstagePane)
                button.Background = Brushes.Transparent;
        };
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, label);
        return button;
    }

    private void ShowBackstageOverlay()
    {
        SetRibbonKeyTipsVisible(false);
        NavigateBackstageOverlay(FreeXBackstagePaneId.Home);
        _backstageOverlay.IsVisible = true;
        _backstageOverlay.Focus();
    }

    // WPF's Ctrl+P route opens the Backstage Print pane rather than jumping straight to the
    // standalone preview window. Keep the keyboard route on the same live pane so Preview and
    // Print remain available as the next explicit actions.
    private void ShowBackstagePrintPane()
    {
        ShowBackstageOverlay();
        NavigateBackstageOverlay(FreeXBackstagePaneId.Print);
    }

    private void HideBackstageOverlay()
    {
        _backstageOverlay.IsVisible = false;
        (_activeCellBorder as Control ?? _sheetGridHost).Focus();
    }

    private void NavigateBackstageOverlay(FreeXBackstagePaneId pane)
    {
        _activeBackstagePane = pane;
        foreach (var (candidate, button) in _backstagePaneButtons)
            button.Background = candidate == pane ? LiveBackstageRailSelected : Brushes.Transparent;

        _backstageContentHost.Content = pane switch
        {
            FreeXBackstagePaneId.Home => BuildLiveBackstageHomePane(),
            FreeXBackstagePaneId.Info => BuildLiveBackstageInfoPane(),
            FreeXBackstagePaneId.Print => BuildLiveBackstagePrintPane(),
            _ => BuildLiveBackstageHomePane(),
        };
    }

    private bool TryActivateBackstagePane(FreeXBackstagePaneId pane)
    {
        if (!_backstagePaneButtons.TryGetValue(pane, out var button) ||
            !button.IsVisible ||
            !button.IsEffectivelyEnabled)
            return false;

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        return true;
    }

    private bool TryActivateBackstageCommand(FreeXBackstageCommandId command)
    {
        if (!_backstageCommandButtons.TryGetValue(command, out var button) ||
            !button.IsVisible ||
            !button.IsEffectivelyEnabled)
            return false;

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        return true;
    }

    private Control BuildLiveBackstageHomePane()
    {
        var content = CreateLiveBackstagePaneStack();
        content.Children.Add(new TextBlock
        {
            Text = GetLiveBackstageGreeting(DateTime.Now),
            FontFamily = FormulaBarFontFamily,
            FontSize = 30,
            Foreground = PrimaryInk,
        });
        content.Children.Add(new TextBlock
        {
            Text = UiText.Get("MainWindow_Text_New"),
            FontFamily = FormulaBarFontFamily,
            FontSize = 17,
            Foreground = PrimaryInk,
            Margin = new Thickness(0, 24, 0, 10),
        });

        var blankWorkbook = new Button
        {
            Width = 145,
            Height = 112,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            Background = Brushes.White,
            BorderBrush = FormulaBarControlBorder,
            BorderThickness = new Thickness(1),
            Content = new StackPanel
            {
                Spacing = 9,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                Children =
                {
                    AvaloniaRibbonIcons.Build(
                        Free.Shared.Ribbon.RibbonCommandIconKind.Grid,
                        44,
                        "new"),
                    new TextBlock
                    {
                        Text = UiText.Get("MainWindow_Text_BlankWorkbook"),
                        FontFamily = FormulaBarFontFamily,
                        FontSize = 13,
                        HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                    },
                },
            },
        };
        AutomationProperties.SetAutomationId(blankWorkbook, "BackstageBlankWorkbookButton");
        blankWorkbook.Click += async (_, _) =>
        {
            HideBackstageOverlay();
            await ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.New);
        };
        content.Children.Add(blankWorkbook);

        content.Children.Add(new TextBlock
        {
            Text = UiText.Get("MainWindow_Text_Recent"),
            FontFamily = FormulaBarFontFamily,
            FontSize = 17,
            Foreground = PrimaryInk,
            Margin = new Thickness(0, 28, 0, 8),
        });

        var entries = _recentFiles.Snapshot()
            .OrderByDescending(entry => entry.IsPinned)
            .ThenByDescending(entry => entry.LastOpened)
            .Take(12)
            .ToArray();
        if (entries.Length == 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = "(No recent workbooks)",
                FontFamily = FormulaBarFontFamily,
                Foreground = SecondaryInk,
            });
        }
        else
        {
            foreach (var entry in entries)
                content.Children.Add(BuildLiveBackstageRecentRow(entry));
        }

        return content;
    }

    private Control BuildLiveBackstageRecentRow(Free.Shared.AppServices.RecentFileEntry entry)
    {
        var row = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(145) },
                new ColumnDefinition { Width = new GridLength(38) },
            },
            Height = 40,
            Focusable = true,
        };

        AvaloniaManagedContextMenu.Attach(
            row,
            () => AvaloniaBackstageRecentFileContextMenu.BuildItems(
                entry.IsPinned,
                Path.GetFileName(entry.Path),
                UiText.Get,
                action => ApplyBackstageRecentFileAction(entry, action)));

        var openButton = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2),
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
            Content = new TextBlock
            {
                Text = Path.GetFileName(entry.Path),
                FontFamily = FormulaBarFontFamily,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
        ToolTip.SetTip(openButton, entry.Path);
        AutomationProperties.SetAutomationId(openButton, "BackstageRecentFileButton");
        AutomationProperties.SetName(openButton, Path.GetFileName(entry.Path));
        AvaloniaManagedContextMenu.Attach(
            openButton,
            () => AvaloniaBackstageRecentFileContextMenu.BuildItems(
                entry.IsPinned,
                Path.GetFileName(entry.Path),
                UiText.Get,
                action => ApplyBackstageRecentFileAction(entry, action)));
        openButton.Click += async (_, _) =>
        {
            HideBackstageOverlay();
            await OpenRecentWorkbookAsync(entry.Path, entry.FileAccessIdentity);
        };
        row.Children.Add(openButton);

        var date = new TextBlock
        {
            Text = entry.LastOpened.LocalDateTime.ToString("g"),
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            Foreground = SecondaryInk,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AvaloniaGrid.SetColumn(date, 1);
        row.Children.Add(date);

        var pin = new Button
        {
            Content = AvaloniaRibbonIcons.BuildMonochrome(
                Free.Shared.Ribbon.RibbonCommandIconKind.Pin,
                14,
                entry.IsPinned ? "unpin-from-list" : "pin-to-list",
                SecondaryInk),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
        };
        ToolTip.SetTip(pin, entry.IsPinned ? "Unpin from list" : "Pin to list");
        pin.Click += (_, _) => ApplyBackstageRecentFileAction(
            entry,
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
            CreateWorkbookInfoDisplayStrings());
        var content = CreateLiveBackstagePaneStack();
        content.Children.Add(CreateLiveBackstageHeading(UiText.Get("Backstage_Info_Title")));
        content.Children.Add(CreateLiveBackstageSection("Properties"));
        content.Children.Add(CreateLiveBackstageDetail("Workbook", display.WorkbookName));
        content.Children.Add(CreateLiveBackstageDetail("Location", display.FilePath));
        content.Children.Add(CreateLiveBackstageDetail("Format", display.Format));
        content.Children.Add(CreateLiveBackstageDetail("Size", display.FileSize));
        content.Children.Add(CreateLiveBackstageDetail("Last modified", display.LastModified));
        content.Children.Add(CreateLiveBackstageDetail("Sheets", display.SheetCount));
        content.Children.Add(CreateLiveBackstageSection("Protection"));
        content.Children.Add(CreateLiveBackstageDetail("Workbook", display.WorkbookProtectionSummary));
        content.Children.Add(CreateLiveBackstageDetail("Active sheet", display.ActiveSheetProtectionSummary));
        content.Children.Add(CreateLiveBackstageSection("Statistics"));
        content.Children.Add(new TextBlock
        {
            Text = display.StatisticsSummary,
            FontFamily = FormulaBarFontFamily,
            FontSize = 13,
            Foreground = PrimaryInk,
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(display.UnsavedChangesNote))
        {
            content.Children.Add(new TextBlock
            {
                Text = display.UnsavedChangesNote,
                FontFamily = FormulaBarFontFamily,
                FontSize = 12,
                Foreground = SecondaryInk,
                Margin = new Thickness(0, 12, 0, 0),
            });
        }

        return content;
    }

    private Control BuildLiveBackstagePrintPane()
    {
        var content = CreateLiveBackstagePaneStack();
        content.Children.Add(CreateLiveBackstageHeading(UiText.Get("MainWindow_Text_Print")));
        content.Children.Add(new TextBlock
        {
            Text = "Preview the active worksheet or send it to an available printer.",
            FontFamily = FormulaBarFontFamily,
            FontSize = 13,
            Foreground = SecondaryInk,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 22, 0, 0),
        };
        actions.Children.Add(CreateLiveBackstageActionButton(
            "Print Preview",
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
            Padding = new Thickness(14, 7),
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += async (_, _) =>
        {
            HideBackstageOverlay();
            await action();
        };
        return button;
    }

    private static StackPanel CreateLiveBackstagePaneStack() =>
        new()
        {
            Margin = new Thickness(38, 30, 34, 36),
            Spacing = 6,
            MaxWidth = 760,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };

    private static string GetLiveBackstageGreeting(DateTime now) =>
        UiText.Get(now.Hour switch
        {
            < 12 => "Backstage_GreetingMorning",
            < 17 => "Backstage_GreetingAfternoon",
            _ => "Backstage_GreetingEvening",
        });

    private static TextBlock CreateLiveBackstageHeading(string text) =>
        new()
        {
            Text = text,
            FontFamily = FormulaBarFontFamily,
            FontSize = 28,
            Foreground = PrimaryInk,
            Margin = new Thickness(0, 0, 0, 12),
        };

    private static TextBlock CreateLiveBackstageSection(string text) =>
        new()
        {
            Text = text,
            FontFamily = FormulaBarFontFamily,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = PrimaryInk,
            Margin = new Thickness(0, 14, 0, 4),
        };

    private static Control CreateLiveBackstageDetail(string label, string value)
    {
        var row = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(150) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            MinHeight = 27,
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = FormulaBarFontFamily,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = PrimaryInk,
        });
        var valueBlock = new TextBlock
        {
            Text = value,
            FontFamily = FormulaBarFontFamily,
            FontSize = 13,
            Foreground = SecondaryInk,
            TextWrapping = TextWrapping.Wrap,
        };
        AvaloniaGrid.SetColumn(valueBlock, 1);
        row.Children.Add(valueBlock);
        return row;
    }
}
