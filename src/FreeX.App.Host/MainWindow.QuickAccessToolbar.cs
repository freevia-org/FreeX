using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shell;
using FreeX.App.Services.Ribbon;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Commands;
using SharedQat = Free.Shared.Ribbon.Wpf.QuickAccessToolbarRenderer;
using SharedQatItem = Free.Shared.Ribbon.Wpf.QuickAccessToolbarItem;
using SharedQatOptions = Free.Shared.Ribbon.Wpf.QuickAccessToolbarRenderOptions;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const int QuickAccessHistoryMaxCount = 16;

    private readonly List<Button> _quickAccessToolbarButtons = [];
    private readonly HashSet<ButtonBase> _quickAccessToolbarChromeButtons = [];
    private readonly List<QuickAccessToolbarStateTarget> _quickAccessToolbarStateTargets = [];
    private readonly HashSet<string> _registeredQuickAccessToolbarNames = new(StringComparer.Ordinal);

    private void RebuildQuickAccessToolbar()
    {
        UnregisterQuickAccessToolbarNames();
        TitleBarQatPanel.Children.Clear();
        BelowRibbonQatPanel.Children.Clear();
        _quickAccessToolbarButtons.Clear();
        _quickAccessToolbarChromeButtons.Clear();
        _quickAccessToolbarStateTargets.Clear();

        var commands = QuickAccessToolbarCatalog.Normalize(_options.QuickAccessToolbarCommands);
        _options.QuickAccessToolbarCommands = commands.Select(command => command.Id).ToList();

        var showBelowRibbon = _options.QuickAccessToolbarBelowRibbon;
        var targetPanel = showBelowRibbon ? BelowRibbonQatPanel : TitleBarQatPanel;
        BelowRibbonQatRoot.Visibility = showBelowRibbon ? Visibility.Visible : Visibility.Collapsed;
        TitleBarQatPanel.Visibility = showBelowRibbon ? Visibility.Collapsed : Visibility.Visible;

        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            var hasHistoryFlyout = IsQuickAccessHistoryCommand(command.Id);
            var button = CreateQuickAccessToolbarButton(command, index + 1, showBelowRibbon, hasHistoryFlyout);
            var availability = QuickAccessCommandStateResolver.GetAvailability(command.Id);
            targetPanel.Children.Add(button);
            _quickAccessToolbarButtons.Add(button);
            _quickAccessToolbarChromeButtons.Add(button);
            _quickAccessToolbarStateTargets.Add(new(button, availability));
            RegisterQuickAccessToolbarName(command.AutomationId, button);

            if (hasHistoryFlyout)
            {
                var historyButton = CreateQuickAccessToolbarHistoryButton(command, showBelowRibbon);
                targetPanel.Children.Add(historyButton);
                _quickAccessToolbarChromeButtons.Add(historyButton);
                _quickAccessToolbarStateTargets.Add(new(historyButton, availability));
                RegisterQuickAccessToolbarName(historyButton.Name, historyButton);
            }
        }

        RefreshQuickAccessToolbarCommandStates(force: true);
    }

    // Builds a QAT button from a neutral descriptor through the shared Free.Shared.Ribbon.Wpf QAT renderer
    // (style, glyph, size, hit-test-in-chrome, automation id/name — the construction FreeX and FreeW shared),
    // then layers FreeX-only decorations the renderer leaves to the host: localized tooltip/key-tip metadata,
    // command metadata, the per-command customization context menu, and the sender/args click that
    // forwards to ExecuteQuickAccessToolbarCommand. The neutral QuickAccessToolbarCatalog and command state
    // live in FreeX.App.Services.Ribbon; this WPF layer only renders and dispatches them.
    private Button CreateQuickAccessToolbarButton(
        QuickAccessToolbarCommandDefinition command,
        int visibleIndex,
        bool showBelowRibbon,
        bool hasHistoryFlyout)
    {
        var title = UiText.Get(command.TitleResourceKey);
        var item = new SharedQatItem(command.Id, title, command.IconKind)
        {
            AutomationId = command.AutomationId
        };

        var button = SharedQat.BuildButton(
            this,
            item,
            onClick: null,
            QuickAccessToolbarRenderOptions(
                showBelowRibbon,
                width: hasHistoryFlyout ? 24 : 26,
                margin: hasHistoryFlyout ? new Thickness(0) : new Thickness(0, 0, 2, 0)));

        RibbonTooltip.SetTitle(button, title);
        RibbonTooltip.SetKeyTip(button, QuickAccessToolbarCatalog.FormatKeyTip(visibleIndex));
        RibbonTooltip.SetDescription(button, UiText.Get(command.DescriptionResourceKey));
        RibbonMetadata.SetCommandName(button, command.CommandName);
        RibbonMetadata.SetCatalogId(button, command.Id);
        button.ContextMenu = CreateQuickAccessToolbarCustomizationContextMenu(command.Id);
        button.Click += (_, args) => ExecuteQuickAccessToolbarCommand(command.Id, button, args);
        return button;
    }

    // FreeX-side QAT render options: the shared renderer draws the button (TitleBarQatButton on the navy
    // caption, or RibbonBtn when shown below the ribbon) with FreeX's own RibbonIcon glyph factory so the
    // icons match the rest of the app. FreeX keeps localized RibbonTooltip metadata, name
    // registration (tracked for rebuild) and click (sender/args), so those shared hooks are turned off.
    private SharedQatOptions QuickAccessToolbarRenderOptions(
        bool showBelowRibbon,
        double width,
        Thickness margin)
    {
        var iconBrushKey = showBelowRibbon ? "FreeXTextBrush" : "FreeXWhiteBrush";
        var iconBrushFallback = showBelowRibbon
            ? new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F))
            : (Brush)Brushes.White;
        var iconBrush = TryFindResource(iconBrushKey) as Brush ?? iconBrushFallback;
        return new SharedQatOptions
        {
            ButtonStyleKey = showBelowRibbon ? "RibbonBtn" : "TitleBarQatButton",
            Foreground = iconBrush,
            ButtonWidth = width,
            ButtonHeight = 22,
            IconSize = 16,
            ButtonMargin = margin,
            FontSize = 13,
            HitTestVisibleInChrome = !showBelowRibbon,
            SetWpfToolTip = false,
            WireClick = false,
            SetElementName = false,
            IconFactory = (kind, size, brush) => new RibbonIcon
            {
                Kind = kind,
                IconSize = size,
                Foreground = brush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private Button CreateQuickAccessToolbarHistoryButton(
        QuickAccessToolbarCommandDefinition command,
        bool showBelowRibbon)
    {
        var title = UiText.Get(command.TitleResourceKey);
        var historyTitle = UiText.Format("QuickAccessToolbar_HistoryAutomationNameFormat", title);
        var buttonName = GetQuickAccessHistoryButtonName(command.Id);
        var item = new SharedQatItem(command.Id, historyTitle, RibbonCommandIconKind.ChevronDown)
        {
            AutomationId = buttonName
        };

        var options = QuickAccessToolbarRenderOptions(showBelowRibbon, width: 12, margin: new Thickness(0, 0, 2, 0));
        var button = SharedQat.BuildButton(this, item, onClick: null, options with { IconSize = 9 });

        RibbonTooltip.SetTitle(button, historyTitle);
        RibbonTooltip.SetDescription(
            button,
            UiText.Format("QuickAccessToolbar_HistoryDescriptionFormat", title.ToLowerInvariant()));
        RibbonMetadata.SetCommandName(button, $"{command.CommandName} History");
        RibbonMetadata.SetCatalogId(button, command.Id);
        button.ContextMenu = CreateQuickAccessToolbarCustomizationContextMenu(command.Id);
        button.Click += (_, _) => OpenQuickAccessHistoryMenu(command.Id, button);
        return button;
    }

    private void InitializeQuickAccessToolbarCustomizationContextMenus()
    {
        RibbonTabs.AddHandler(
            FrameworkElement.ContextMenuOpeningEvent,
            new ContextMenuEventHandler(RibbonCommand_ContextMenuOpening),
            handledEventsToo: true);
    }

    private void RibbonCommand_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            !TryFindQuickAccessToolbarCatalogCommand(source, out var placementTarget, out var command))
        {
            return;
        }

        e.Handled = true;
        var menu = CreateQuickAccessToolbarCustomizationContextMenu(command.Id);
        menu.PlacementTarget = placementTarget;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private bool TryFindQuickAccessToolbarCatalogCommand(
        DependencyObject source,
        out ButtonBase placementTarget,
        out QuickAccessToolbarCommandDefinition command)
    {
        for (DependencyObject? current = source; current is not null; current = GetRibbonTreeParent(current))
        {
            if (current is not ButtonBase button ||
                IsQuickAccessToolbarButton(button) ||
                RibbonMetadata.IsCollapsedGroupButton(button) ||
                !RibbonMetadata.TryGetCommandName(button, out var commandName) ||
                !QuickAccessToolbarCatalog.TryGetByCommandName(commandName, out command))
            {
                continue;
            }

            placementTarget = button;
            return true;
        }

        placementTarget = null!;
        command = null!;
        return false;
    }

    // Builds the per-command QAT customization menu from the neutral QuickAccessToolbarContextMenuPlanner so
    // its single Add/Remove item (header, enablement, automation id) is single-sourced with a future Avalonia
    // port instead of hand-built here. Dispatch still routes through ApplyQuickAccessToolbarCustomization,
    // preserving the persisted-options + rebuild behavior verbatim.
    private ContextMenu CreateQuickAccessToolbarCustomizationContextMenu(string commandId)
    {
        var menu = new ContextMenu();
        var state = new QuickAccessToolbarCustomizationMenuState(
            commandId,
            QuickAccessToolbarCatalog.NormalizeCommandIds(_options.QuickAccessToolbarCommands));
        foreach (var command in QuickAccessToolbarContextMenuPlanner.BuildCustomizationCommands(state))
            AddQuickAccessToolbarCustomizationMenuItem(menu.Items, command);

        return menu;
    }

    private void AddQuickAccessToolbarCustomizationMenuItem(
        ItemCollection target,
        QuickAccessToolbarMenuCommand command)
    {
        var menuItem = new MenuItem
        {
            Header = UiText.Get(command.ResourceKey),
            IsEnabled = command.IsEnabled
        };
        AutomationProperties.SetAutomationId(menuItem, command.AutomationId);

        var action = command.Action == QuickAccessToolbarMenuAction.Remove
            ? QuickAccessToolbarCustomizationAction.Remove
            : QuickAccessToolbarCustomizationAction.Add;
        menuItem.Click += (_, _) => ApplyQuickAccessToolbarCustomization(command.CommandId, action);

        target.Add(menuItem);
    }

    private void ApplyQuickAccessToolbarCustomization(
        string commandId,
        QuickAccessToolbarCustomizationAction action)
    {
        var saveResult = MutateRuntimeOptions(options =>
            options.QuickAccessToolbarCommands =
                QuickAccessToolbarCustomizationPlanner.Apply(
                    options.QuickAccessToolbarCommands,
                    commandId,
                    action).ToList());
        if (!saveResult.IsPersisted)
        {
            ShowOwnedMessage(
                saveResult.PersistenceError ?? UiText.Get("QuickAccessToolbar_CustomizationSaveFailed"),
                UiText.Get("Options_QuickAccessToolbar"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        RebuildQuickAccessToolbar();
    }

    private void RegisterQuickAccessToolbarName(string name, FrameworkElement element)
    {
        RegisterName(name, element);
        _registeredQuickAccessToolbarNames.Add(name);
    }

    private void UnregisterQuickAccessToolbarNames()
    {
        foreach (var name in _registeredQuickAccessToolbarNames.ToArray())
        {
            UnregisterName(name);
            _registeredQuickAccessToolbarNames.Remove(name);
        }
    }

    private IEnumerable<FrameworkElement> EnumerateQuickAccessToolbarButtons() =>
        _quickAccessToolbarButtons;

    private bool IsQuickAccessToolbarButton(FrameworkElement element) =>
        element is ButtonBase button && _quickAccessToolbarChromeButtons.Contains(button);

    private void RefreshQuickAccessToolbarCommandStates(bool force = false)
    {
        ApplyQuickAccessToolbarCommandState(CreateQuickAccessCommandState(), force);
    }

    private void RefreshQuickAccessToolbarCommandStatesAfterSelectionChange()
    {
        if (_lastQuickAccessCommandState is not { } state ||
            _lastQuickAccessCommandStateWorkbookId != _workbook.Id)
        {
            RefreshQuickAccessToolbarCommandStates();
            return;
        }

        ApplyQuickAccessToolbarCommandState(
            state.WithSelectionContext(
                HasActiveWorksheetForQuickAccessCommandState(),
                HasSelectionForQuickAccessCommandState()),
            force: false);
    }

    private QuickAccessCommandState CreateQuickAccessCommandState() =>
        new(
            _session.CanUndo,
            _session.CanRedo,
            HasActiveWorksheetForQuickAccessCommandState(),
            HasSelectionForQuickAccessCommandState());

    private bool HasActiveWorksheetForQuickAccessCommandState() =>
        _workbook.GetSheet(_currentSheetId) is not null;

    private bool HasSelectionForQuickAccessCommandState() =>
        SheetGrid.SelectedRange is not null;

    private void ApplyQuickAccessToolbarCommandState(QuickAccessCommandState state, bool force)
    {
        if (!force &&
            _lastQuickAccessCommandState == state &&
            _lastQuickAccessCommandStateWorkbookId == _workbook.Id)
        {
            return;
        }

        foreach (var target in _quickAccessToolbarStateTargets)
        {
            var isEnabled = QuickAccessCommandStateResolver.CanExecute(target.Availability, state);
            if (target.Button.IsEnabled != isEnabled)
                target.Button.IsEnabled = isEnabled;
        }

        _lastQuickAccessCommandState = state;
        _lastQuickAccessCommandStateWorkbookId = _workbook.Id;
    }

    private bool IsQuickAccessToolbarCommandStateStableForSelectionDrag()
    {
        if (_lastQuickAccessCommandState is not { } state ||
            _lastQuickAccessCommandStateWorkbookId != _workbook.Id)
            return false;

        return state.HasActiveWorksheet == HasActiveWorksheetForQuickAccessCommandState() &&
            state.HasSelection == HasSelectionForQuickAccessCommandState();
    }

    private Button? GetQuickAccessToolbarButton(string commandId)
    {
        if (!QuickAccessToolbarCatalog.TryGet(commandId, out var command))
            return null;

        foreach (var button in _quickAccessToolbarButtons)
        {
            if (string.Equals(button.Name, command.AutomationId, StringComparison.Ordinal))
                return button;
        }

        return null;
    }

    private static bool IsQuickAccessHistoryCommand(string commandId) =>
        string.Equals(commandId, QuickAccessToolbarCommandIds.Undo, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(commandId, QuickAccessToolbarCommandIds.Redo, StringComparison.OrdinalIgnoreCase);

    private static string GetQuickAccessHistoryButtonName(string commandId) =>
        string.Equals(commandId, QuickAccessToolbarCommandIds.Undo, StringComparison.OrdinalIgnoreCase)
            ? "UndoQatHistoryBtn"
            : "RedoQatHistoryBtn";

    private IReadOnlyList<CommandHistoryEntry> GetQuickAccessHistoryEntries(string commandId)
    {
        return commandId switch
        {
            QuickAccessToolbarCommandIds.Undo => _session.GetUndoHistory(QuickAccessHistoryMaxCount),
            QuickAccessToolbarCommandIds.Redo => _session.GetRedoHistory(QuickAccessHistoryMaxCount),
            _ => []
        };
    }

    private void OpenQuickAccessHistoryMenu(string commandId, ButtonBase placementTarget)
    {
        var menu = CreateQuickAccessHistoryMenu(commandId, placementTarget);
        menu.IsOpen = true;
    }

    // Builds the Undo/Redo history dropdown from the neutral QuickAccessToolbarContextMenuPlanner so its
    // structure (per-span entries vs. the disabled "No actions to …" placeholder, per-item automation ids) is
    // single-sourced with a future Avalonia port. Dispatch still calls ExecuteQuickAccessHistory with the
    // span's 1-based action count, preserving the existing behavior verbatim.
    private ContextMenu CreateQuickAccessHistoryMenu(string commandId, ButtonBase placementTarget)
    {
        var entries = GetQuickAccessHistoryEntries(commandId);
        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = PlacementMode.Bottom
        };

        var isRedo = string.Equals(commandId, QuickAccessToolbarCommandIds.Redo, StringComparison.OrdinalIgnoreCase);
        var state = new QuickAccessToolbarHistoryMenuState(
            isRedo,
            entries.Select(entry => entry.Label).ToList());
        foreach (var command in QuickAccessToolbarContextMenuPlanner.BuildHistoryCommands(state))
            AddQuickAccessHistoryMenuItem(menu.Items, commandId, command);

        return menu;
    }

    private void AddQuickAccessHistoryMenuItem(
        ItemCollection target,
        string commandId,
        QuickAccessToolbarMenuCommand command)
    {
        var menuItem = new MenuItem
        {
            Header = command.Header,
            IsEnabled = command.IsEnabled
        };

        if (command.Action == QuickAccessToolbarMenuAction.ExecuteHistory)
        {
            AutomationProperties.SetAutomationId(menuItem, command.AutomationId);
            var actionCount = command.ActionCount;
            menuItem.Click += (_, _) => ExecuteQuickAccessHistory(commandId, actionCount);
        }

        target.Add(menuItem);
    }

    private void ExecuteQuickAccessHistory(string commandId, int actionCount)
    {
        for (var index = 0; index < actionCount; index++)
        {
            var success = commandId switch
            {
                QuickAccessToolbarCommandIds.Undo => ExecuteUndo(),
                QuickAccessToolbarCommandIds.Redo => ExecuteRedo(),
                _ => false
            };

            if (!success)
                break;
        }
    }

    private async void ExecuteQuickAccessToolbarCommand(string commandId, object sender, RoutedEventArgs args)
    {
        if (!WorkbookApplicationCommandRouter.TryRouteQuickAccess(commandId, out var route))
            return;

        await WorkbookApplicationCommands.TryExecuteAsync(
            route,
            nativeSource: sender,
            nativeEventArgs: args);
    }

    // The QAT runs a ribbon toggle command without the toggle's own Click: flip the command's checked
    // state in the neutral store (as a real click would before raising Click), then invoke the handler,
    // which reads the new state from the store.
    private void ExecuteToggleQuickAccessCommand(
        string commandId,
        Action<object, RoutedEventArgs> handler)
    {
        _ribbonState.SetChecked(commandId, !IsRibbonCommandChecked(commandId));
        handler(this, new RoutedEventArgs(ButtonBase.ClickEvent, this));
    }

    private readonly record struct QuickAccessToolbarStateTarget(
        ButtonBase Button,
        QuickAccessCommandAvailability Availability);
}
