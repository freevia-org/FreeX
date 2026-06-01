using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shell;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private readonly List<Button> _quickAccessToolbarButtons = [];
    private readonly HashSet<string> _registeredQuickAccessToolbarNames = new(StringComparer.Ordinal);

    private void RebuildQuickAccessToolbar()
    {
        UnregisterQuickAccessToolbarNames();
        TitleBarQatPanel.Children.Clear();
        BelowRibbonQatPanel.Children.Clear();
        _quickAccessToolbarButtons.Clear();

        var commands = QuickAccessToolbarCatalog.Normalize(_options.QuickAccessToolbarCommands);
        _options.QuickAccessToolbarCommands = commands.Select(command => command.Id).ToList();

        var showBelowRibbon = _options.QuickAccessToolbarBelowRibbon;
        var targetPanel = showBelowRibbon ? BelowRibbonQatPanel : TitleBarQatPanel;
        BelowRibbonQatRoot.Visibility = showBelowRibbon ? Visibility.Visible : Visibility.Collapsed;
        TitleBarQatPanel.Visibility = showBelowRibbon ? Visibility.Collapsed : Visibility.Visible;

        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            var button = CreateQuickAccessToolbarButton(command, index + 1, showBelowRibbon);
            targetPanel.Children.Add(button);
            _quickAccessToolbarButtons.Add(button);
            RegisterName(command.AutomationId, button);
            _registeredQuickAccessToolbarNames.Add(command.AutomationId);
        }

        RefreshQuickAccessToolbarCommandStates(force: true);
    }

    private Button CreateQuickAccessToolbarButton(
        QuickAccessToolbarCommandDefinition command,
        int visibleIndex,
        bool showBelowRibbon)
    {
        var iconBrush = (Brush)FindResource(showBelowRibbon
            ? "FreeXTextBrush"
            : "FreeXWhiteBrush");
        var button = new Button
        {
            Name = command.AutomationId,
            Width = 26,
            Height = 22,
            Margin = new Thickness(0, 0, 2, 0),
            Style = (Style)FindResource(showBelowRibbon ? "RibbonBtn" : "TitleBarQatButton"),
            FontSize = 13,
            Content = new RibbonIcon
            {
                Kind = command.IconKind,
                IconSize = 16,
                Foreground = iconBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        WindowChrome.SetIsHitTestVisibleInChrome(button, !showBelowRibbon);
        AutomationProperties.SetAutomationId(button, command.AutomationId);
        AutomationProperties.SetName(button, UiText.Get(command.TitleResourceKey));
        RibbonTooltip.SetTitle(button, UiText.Get(command.TitleResourceKey));
        RibbonTooltip.SetKeyTip(button, FormatQuickAccessToolbarKeyTip(visibleIndex));
        RibbonTooltip.SetDescription(button, UiText.Get(command.DescriptionResourceKey));
        RibbonMetadata.SetCommandName(button, command.CommandName);
        RibbonMetadata.SetCatalogId(button, command.Id);
        button.ContextMenu = CreateQuickAccessToolbarCustomizationContextMenu(command.Id);
        button.Click += (_, args) => ExecuteQuickAccessToolbarCommand(command.Id, button, args);
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

    private ContextMenu CreateQuickAccessToolbarCustomizationContextMenu(string commandId)
    {
        var plan = QuickAccessToolbarCustomizationPlanner.CreatePlan(commandId, _options.QuickAccessToolbarCommands);
        var item = new MenuItem
        {
            Header = UiText.Get(plan.HeaderResourceKey),
            IsEnabled = plan.IsEnabled
        };
        AutomationProperties.SetAutomationId(item, plan.AutomationId);
        item.Click += (_, _) => ApplyQuickAccessToolbarCustomization(plan.CommandId, plan.Action);

        var menu = new ContextMenu();
        menu.Items.Add(item);
        return menu;
    }

    private void ApplyQuickAccessToolbarCustomization(
        string commandId,
        QuickAccessToolbarCustomizationAction action)
    {
        var commandIds = QuickAccessToolbarCustomizationPlanner.Apply(
            _options.QuickAccessToolbarCommands,
            commandId,
            action);
        if (_options.QuickAccessToolbarCommands.SequenceEqual(commandIds, StringComparer.OrdinalIgnoreCase))
            return;

        _options.QuickAccessToolbarCommands = commandIds.ToList();
        if (!_options.Save())
        {
            ShowOwnedMessage(
                _options.LastPersistenceError ?? "Failed to save Quick Access Toolbar customization.",
                UiText.Get("Options_QuickAccessToolbar"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        RebuildQuickAccessToolbar();
    }

    private void UnregisterQuickAccessToolbarNames()
    {
        foreach (var name in _registeredQuickAccessToolbarNames.ToArray())
        {
            UnregisterName(name);
            _registeredQuickAccessToolbarNames.Remove(name);
        }
    }

    private static string FormatQuickAccessToolbarKeyTip(int visibleIndex)
    {
        if (visibleIndex <= 9)
            return visibleIndex.ToString();

        var offset = visibleIndex - 9;
        const string extraKeyTipCharacters = "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        if (offset <= extraKeyTipCharacters.Length)
            return $"0{extraKeyTipCharacters[offset - 1]}";

        return visibleIndex.ToString();
    }

    private IEnumerable<FrameworkElement> EnumerateQuickAccessToolbarButtons() =>
        _quickAccessToolbarButtons;

    private bool IsQuickAccessToolbarButton(FrameworkElement element) =>
        element is Button button && _quickAccessToolbarButtons.Contains(button);

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
            _commandBus.CanUndo(_workbook.Id),
            _commandBus.CanRedo(_workbook.Id),
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

        foreach (var button in _quickAccessToolbarButtons)
        {
            if (!RibbonMetadata.TryGetCatalogId(button, out var commandId))
                continue;

            var isEnabled = QuickAccessCommandStateResolver.CanExecute(commandId, state);
            if (button.IsEnabled != isEnabled)
                button.IsEnabled = isEnabled;
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

        return _quickAccessToolbarButtons
            .FirstOrDefault(button => string.Equals(button.Name, command.AutomationId, StringComparison.Ordinal));
    }

    private async void ExecuteQuickAccessToolbarCommand(string commandId, object sender, RoutedEventArgs args)
    {
        switch (commandId)
        {
            case QuickAccessToolbarCommandIds.Save:
                SaveButton_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.Undo:
                ExecuteUndo();
                break;
            case QuickAccessToolbarCommandIds.Redo:
                ExecuteRedo();
                break;
            case QuickAccessToolbarCommandIds.New:
                await RequestNewWorkbookAsync();
                break;
            case QuickAccessToolbarCommandIds.Open:
                OpenButton_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.SaveAs:
                SaveAsButton_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.Print:
                PrintButton_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.ExportPdfXps:
                ExportPdfButton_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.Cut:
                CutBtn_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.Copy:
                CopyBtn_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.Paste:
                PasteBtn_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.FormatPainter:
                FormatPainterBtn_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.Bold:
                ExecuteToggleButtonQuickAccessCommand(BoldButton, BoldButton_Click);
                break;
            case QuickAccessToolbarCommandIds.Italic:
                ExecuteToggleButtonQuickAccessCommand(ItalicButton, ItalicButton_Click);
                break;
            case QuickAccessToolbarCommandIds.Underline:
                ExecuteToggleButtonQuickAccessCommand(UnderlineButton, UnderlineButton_Click);
                break;
            case QuickAccessToolbarCommandIds.FillColor:
                FillColorBtn_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.FontColor:
                FontColorBtn_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.FormatCells:
                OpenFormatCellsDialog();
                break;
            case QuickAccessToolbarCommandIds.InsertFunction:
                InsertFunctionBtn_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.AutoSum:
                InsertAutoSumFormula("SUM");
                break;
            case QuickAccessToolbarCommandIds.CalculateNow:
                CalcNowBtn_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.CalculateSheet:
                CalcSheetBtn_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.RefreshAll:
                RefreshAllBtn_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.SortAscending:
                SortAscButton_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.SortDescending:
                SortDescButton_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.Filter:
                FilterButton_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.DataValidation:
                ValidationButton_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.NameManager:
                NamedRangesButton_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.Spelling:
                SpellCheckBtn_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.Zoom100:
                Zoom100Btn_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.ZoomSelection:
                ZoomSelectionBtn_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.FreezePanes:
                FreezeAtSelectionMenuItem_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.InsertSheet:
                AddSheetButton_Click(sender, args);
                break;
            case QuickAccessToolbarCommandIds.FindSelect:
                FindButton_Click(sender, args);
                break;
        }
    }

    private static void ExecuteToggleButtonQuickAccessCommand(
        ToggleButton button,
        Action<object, RoutedEventArgs> handler)
    {
        button.IsChecked = button.IsChecked != true;
        handler(button, new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }
}
