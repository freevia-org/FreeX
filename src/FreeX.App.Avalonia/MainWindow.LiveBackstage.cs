using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
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

    private Control BuildBackstageOverlay()
    {
        var entries = LiveBackstageFramePlan.Entries.Select(MapLiveBackstageEntry).ToArray();
        _backstageOverlay = new AvaloniaBackstageFrame(
            new AvaloniaBackstageAccent(
                Color.FromRgb(0x10, 0x25, 0x3A),
                Color.FromRgb(0x1D, 0x3B, 0x54),
                Color.FromRgb(0x24, 0x44, 0x5E),
                Color.FromRgb(0x24, 0x44, 0x5E)),
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

        return _backstageOverlay;
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
        var content = CreateLiveBackstagePaneStack();
        content.Children.Add(new TextBlock
        {
            Text = BackstageGreetingFormatter.FormatGreeting(DateTime.Now),
            FontFamily = FormulaBarFontFamily,
            FontSize = 30,
            Foreground = PrimaryInk,
        });
        content.Children.Add(new TextBlock
        {
            Text = UiText.Get("Common_New"),
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

        var entries = BackstageRecentFileListPlanner.SelectPinnedFirst(
            BackstageRecentFileListPlanner.Build(_recentFiles.Snapshot(), filter: null),
            maximumCount: 12);
        if (entries.Count == 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = UiText.Get("Backstage_Home_NoRecentWorkbooks"),
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

    private Control BuildLiveBackstageRecentRow(RecentFileViewModel entry)
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
                entry.FileName,
                UiText.Get,
                action => ApplyBackstageRecentFileAction(entry.Path, action)));
        var rowDescriptor = LiveBackstageHomePanePlan.Rows.Single(descriptor =>
            descriptor.Kind == (entry.IsPinned
                ? FreeXBackstageRecentFileRowKind.Pinned
                : FreeXBackstageRecentFileRowKind.Recent));
        AutomationProperties.SetAutomationId(row, rowDescriptor.AutomationId);

        var openButton = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2),
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
            Content = new TextBlock
            {
                Text = entry.FileName,
                FontFamily = FormulaBarFontFamily,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
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
        openButton.Click += async (_, _) =>
        {
            HideBackstageOverlay();
            await OpenRecentWorkbookAsync(entry.Path, entry.FileAccessIdentity);
        };
        row.Children.Add(openButton);

        var date = new TextBlock
        {
            Text = entry.LastOpenedText,
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
        var content = CreateLiveBackstagePaneStack();
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
            FontSize = 13,
            Foreground = PrimaryInk,
            TextWrapping = TextWrapping.Wrap,
        });
        if (pane.UnsavedChangesNote is { } unsavedChangesNote)
        {
            content.Children.Add(new TextBlock
            {
                Text = unsavedChangesNote.Resolve(UiText.Get),
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
            Text = UiText.Get("Backstage_Print_Description"),
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
