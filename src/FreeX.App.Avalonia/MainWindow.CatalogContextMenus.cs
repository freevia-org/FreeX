using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const int AvaloniaQuickAccessHistoryMaxCount = 16;

    private readonly StackPanel _avaloniaQuickAccessToolbar = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 1,
        Margin = new Thickness(4, 2),
    };
    private readonly Dictionary<string, Button> _avaloniaQuickAccessButtons =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _avaloniaQuickAccessKeyTipButtons =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Button, Border> _avaloniaQuickAccessKeyTipBadges = [];
    private AppOptions? _avaloniaQuickAccessOptions;
    private Panel? _avaloniaQuickAccessTitleBarHost;
    private Border? _avaloniaQuickAccessBelowRibbonHost;

    internal StackPanel AvaloniaQuickAccessToolbarForTest => _avaloniaQuickAccessToolbar;
    internal Panel? AvaloniaQuickAccessTitleBarHostForTest => _avaloniaQuickAccessTitleBarHost;
    internal Border? AvaloniaQuickAccessBelowRibbonHostForTest => _avaloniaQuickAccessBelowRibbonHost;

    private Border CreateAvaloniaQuickAccessBelowRibbonHost()
    {
        var host = new Border
        {
            Height = 0,
            IsVisible = false,
            Background = ChromeSurface,
            BorderBrush = ToolbarBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(host, "BelowRibbonQuickAccessToolbarHost");
        AutomationProperties.SetName(host, "Quick Access Toolbar below the Ribbon");
        return host;
    }

    private void PopulateQuickAccessToolbar(Panel titleBarHost, Border belowRibbonHost)
    {
        ArgumentNullException.ThrowIfNull(titleBarHost);
        ArgumentNullException.ThrowIfNull(belowRibbonHost);

        _avaloniaQuickAccessTitleBarHost = titleBarHost;
        _avaloniaQuickAccessBelowRibbonHost = belowRibbonHost;
        titleBarHost.Children.Clear();
        AutomationProperties.SetAutomationId(_avaloniaQuickAccessToolbar, "QuickAccessToolbar");
        AutomationProperties.SetName(_avaloniaQuickAccessToolbar, "Quick Access Toolbar");
        WindowDecorationProperties.SetElementRole(
            _avaloniaQuickAccessToolbar,
            WindowDecorationsElementRole.User);

        _avaloniaQuickAccessOptions = AppOptionsStore.Load();
        RebuildAvaloniaQuickAccessToolbar();
    }

    private void ApplyAvaloniaQuickAccessToolbarPlacement()
    {
        if (_avaloniaQuickAccessTitleBarHost is null || _avaloniaQuickAccessBelowRibbonHost is null)
            return;

        _avaloniaQuickAccessTitleBarHost.Children.Remove(_avaloniaQuickAccessToolbar);
        if (ReferenceEquals(_avaloniaQuickAccessBelowRibbonHost.Child, _avaloniaQuickAccessToolbar))
            _avaloniaQuickAccessBelowRibbonHost.Child = null;

        var showBelowRibbon = _avaloniaQuickAccessOptions?.QuickAccessToolbarBelowRibbon == true;
        if (showBelowRibbon)
        {
            _avaloniaQuickAccessBelowRibbonHost.Child = _avaloniaQuickAccessToolbar;
            _avaloniaQuickAccessBelowRibbonHost.Height = 30;
            _avaloniaQuickAccessBelowRibbonHost.IsVisible = true;
            return;
        }

        _avaloniaQuickAccessBelowRibbonHost.Height = 0;
        _avaloniaQuickAccessBelowRibbonHost.IsVisible = false;
        _avaloniaQuickAccessTitleBarHost.Children.Add(_avaloniaQuickAccessToolbar);
    }

    private void RebuildAvaloniaQuickAccessToolbar()
    {
        if (_avaloniaQuickAccessOptions is null)
            return;

        ApplyAvaloniaQuickAccessToolbarPlacement();
        _avaloniaQuickAccessToolbar.Children.Clear();
        _avaloniaQuickAccessButtons.Clear();
        _avaloniaQuickAccessKeyTipButtons.Clear();
        _avaloniaQuickAccessKeyTipBadges.Clear();
        var commands = QuickAccessToolbarCatalog.Normalize(_avaloniaQuickAccessOptions.QuickAccessToolbarCommands);
        _avaloniaQuickAccessOptions.QuickAccessToolbarCommands = commands.Select(command => command.Id).ToList();
        var showBelowRibbon = _avaloniaQuickAccessOptions.QuickAccessToolbarBelowRibbon;

        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            var keyTip = FormatAvaloniaQuickAccessKeyTip(index + 1);
            var button = CreateAvaloniaQuickAccessButton(command, showBelowRibbon, keyTip);
            _avaloniaQuickAccessToolbar.Children.Add(button);
            _avaloniaQuickAccessButtons[command.Id] = button;
            _avaloniaQuickAccessKeyTipButtons[keyTip] = button;

            if (IsAvaloniaQuickAccessHistoryCommand(command.Id))
                _avaloniaQuickAccessToolbar.Children.Add(CreateAvaloniaQuickAccessHistoryButton(command, showBelowRibbon));
        }

        RefreshAvaloniaQuickAccessToolbarState();
        RefreshAvaloniaQuickAccessKeyTipBadges();
    }

    private Button CreateAvaloniaQuickAccessButton(
        QuickAccessToolbarCommandDefinition command,
        bool showBelowRibbon,
        string keyTip)
    {
        var foreground = showBelowRibbon ? PrimaryInk : StatusBarForeground;
        var keyTipBadge = CreateAvaloniaQuickAccessKeyTipBadge(keyTip);
        var content = new Grid
        {
            IsHitTestVisible = false,
            Children =
            {
                AvaloniaRibbonIcons.BuildMonochrome(command.IconKind, 16, command.Id, foreground),
                keyTipBadge,
            },
        };
        var button = new Button
        {
            Width = IsAvaloniaQuickAccessHistoryCommand(command.Id) ? 24 : 26,
            Height = 24,
            Padding = new Thickness(3),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = foreground,
            Content = content,
            Tag = command.Id,
        };
        _avaloniaQuickAccessKeyTipBadges[button] = keyTipBadge;
        WindowDecorationProperties.SetElementRole(button, WindowDecorationsElementRole.User);
        AutomationProperties.SetAutomationId(button, command.AutomationId);
        AutomationProperties.SetName(button, UiText.Get(command.TitleResourceKey));
        ToolTip.SetTip(button, UiText.Get(command.TitleResourceKey));
        button.Click += (_, args) => ExecuteAvaloniaQuickAccessCommand(command, button, args);
        AttachAvaloniaQuickAccessCustomization(button, command.Id);
        return button;
    }

    private static Border CreateAvaloniaQuickAccessKeyTipBadge(string keyTip) => new()
    {
        Tag = "QuickAccessKeyTipBadge",
        Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xCE)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x76, 0x70, 0x5C)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(2),
        Padding = new Thickness(3, 0),
        MinWidth = 18,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, -7, -5, 0),
        IsVisible = false,
        IsHitTestVisible = false,
        Child = new TextBlock
        {
            Text = keyTip,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
        },
    };

    private Button CreateAvaloniaQuickAccessHistoryButton(
        QuickAccessToolbarCommandDefinition command,
        bool showBelowRibbon)
    {
        var foreground = showBelowRibbon ? PrimaryInk : StatusBarForeground;
        var button = new Button
        {
            Width = 14,
            Height = 24,
            Padding = new Thickness(1),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = foreground,
            Content = AvaloniaRibbonIcons.BuildMonochrome(
                RibbonCommandIconKind.ChevronDown,
                8,
                $"{command.Id}-history",
                foreground),
            Tag = $"{command.Id}.History",
        };
        WindowDecorationProperties.SetElementRole(button, WindowDecorationsElementRole.User);
        AutomationProperties.SetAutomationId(button, $"{command.Id}QatHistoryButton");
        AutomationProperties.SetName(button, $"{command.CommandName} history");
        var menu = AvaloniaManagedContextMenu.Attach(
            button,
            () => AvaloniaQuickAccessToolbarContextMenu.BuildHistoryItems(
                CreateAvaloniaQuickAccessHistoryState(command.Id),
                item => ExecuteAvaloniaQuickAccessHistory(command.Id, item.ActionCount)));
        button.Click += (_, _) => menu.Open(button);
        return button;
    }

    private void AttachAvaloniaQuickAccessCustomization(Control anchor, string commandId) =>
        AvaloniaManagedContextMenu.Attach(
            anchor,
            () => AvaloniaQuickAccessToolbarContextMenu.BuildCustomizationItems(
                CreateAvaloniaQuickAccessCustomizationMenuState(commandId),
                UiText.Get,
                ApplyAvaloniaQuickAccessCustomization));

    private QuickAccessToolbarCustomizationMenuState CreateAvaloniaQuickAccessCustomizationMenuState(
        string commandId)
    {
        _avaloniaQuickAccessOptions = AppOptionsStore.Load();
        return new QuickAccessToolbarCustomizationMenuState(
            commandId,
            _avaloniaQuickAccessOptions.QuickAccessToolbarCommands);
    }

    private void AttachRibbonQuickAccessCustomization(Button button)
    {
        var label = EnumerateControlText(button.Content as Control).FirstOrDefault(text =>
            QuickAccessToolbarCatalog.TryGetByCommandName(text, out _));
        if (label is null || !QuickAccessToolbarCatalog.TryGetByCommandName(label, out var command))
            return;

        AttachAvaloniaQuickAccessCustomization(button, command.Id);
    }

    private static IEnumerable<string> EnumerateControlText(Control? control)
    {
        switch (control)
        {
            case TextBlock text when !string.IsNullOrWhiteSpace(text.Text):
                yield return text.Text.Trim();
                break;
            case Panel panel:
                foreach (var child in panel.Children)
                foreach (var value in EnumerateControlText(child))
                    yield return value;
                break;
            case ContentControl { Content: Control content }:
                foreach (var value in EnumerateControlText(content))
                    yield return value;
                break;
            case Decorator { Child: { } child }:
                foreach (var value in EnumerateControlText(child))
                    yield return value;
                break;
        }
    }

    private void ApplyAvaloniaQuickAccessCustomization(QuickAccessToolbarMenuCommand command)
    {
        var action = command.Action switch
        {
            QuickAccessToolbarMenuAction.Add => QuickAccessToolbarCustomizationAction.Add,
            QuickAccessToolbarMenuAction.Remove => QuickAccessToolbarCustomizationAction.Remove,
            _ => (QuickAccessToolbarCustomizationAction?)null,
        };
        if (action is null)
            return;

        // Options can be changed while this window is open. Reload immediately before mutation so
        // saving QAT customization cannot overwrite unrelated settings with the startup snapshot.
        var latestOptions = AppOptionsStore.Load();
        latestOptions.QuickAccessToolbarCommands =
            QuickAccessToolbarCustomizationPlanner.Apply(
                latestOptions.QuickAccessToolbarCommands,
                command.CommandId,
                action.Value).ToList();
        AppOptionsStore.Save(latestOptions);
        _avaloniaQuickAccessOptions = latestOptions;
        RebuildAvaloniaQuickAccessToolbar();
    }

    internal void SetAvaloniaQuickAccessPlacementForTest(bool belowRibbon)
    {
        if (_avaloniaQuickAccessOptions is null)
            return;

        _avaloniaQuickAccessOptions.QuickAccessToolbarBelowRibbon = belowRibbon;
        RebuildAvaloniaQuickAccessToolbar();
    }

    private QuickAccessToolbarHistoryMenuState CreateAvaloniaQuickAccessHistoryState(string commandId)
    {
        var isRedo = string.Equals(commandId, QuickAccessToolbarCommandIds.Redo, StringComparison.OrdinalIgnoreCase);
        var entries = isRedo
            ? _session.GetRedoHistory(AvaloniaQuickAccessHistoryMaxCount)
            : _session.GetUndoHistory(AvaloniaQuickAccessHistoryMaxCount);
        return new QuickAccessToolbarHistoryMenuState(isRedo, entries.Select(entry => entry.Label).ToArray());
    }

    private void ExecuteAvaloniaQuickAccessHistory(string commandId, int actionCount)
    {
        for (var index = 0; index < actionCount; index++)
        {
            if (string.Equals(commandId, QuickAccessToolbarCommandIds.Undo, StringComparison.OrdinalIgnoreCase))
            {
                if (!_session.CanUndo)
                    break;
                UndoLastEdit();
            }
            else
            {
                if (!_session.CanRedo)
                    break;
                RedoLastEdit();
            }
        }
    }

    /// <summary>
    /// Entry point wired to every Quick Access Toolbar button. Guarded because the dispatch below is
    /// <c>async void</c> at the call site: an exception escaping it terminates the process. The
    /// commands it fans out to include Print, whose printer enumeration routinely fails on Linux
    /// (no CUPS/DBus), so a single toolbar click could kill the app.
    /// </summary>
    private void ExecuteAvaloniaQuickAccessCommand(
        QuickAccessToolbarCommandDefinition command,
        object sender,
        RoutedEventArgs args) =>
        RunGuarded(() => ExecuteAvaloniaQuickAccessCommandAsync(command, sender, args));

    private async Task ExecuteAvaloniaQuickAccessCommandAsync(
        QuickAccessToolbarCommandDefinition command,
        object sender,
        RoutedEventArgs args)
    {
        if (WorkbookApplicationCommandRouter.TryRouteQuickAccess(command.Id, out var route))
        {
            await WorkbookApplicationCommands.TryExecuteAsync(
                route,
                nativeSource: sender,
                nativeEventArgs: args);
            return;
        }

        if (_ribbonControl is null)
            return;

        foreach (var button in EnumerateRibbonControls(_ribbonControl).OfType<Button>())
        {
            if (!EnumerateControlText(button.Content as Control)
                    .Any(label => string.Equals(label, command.CommandName, StringComparison.OrdinalIgnoreCase)))
                continue;

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            return;
        }
    }

    private void RefreshAvaloniaQuickAccessToolbarState()
    {
        var state = new QuickAccessCommandState(
            _session.CanUndo,
            _session.CanRedo,
            HasActiveWorksheet: _session.Workbook.GetSheet(_session.ActiveSheet.Id) is not null,
            HasSelection: _session.SelectedRanges.Count > 0);
        foreach (var (commandId, button) in _avaloniaQuickAccessButtons)
            button.IsEnabled = QuickAccessCommandStateResolver.CanExecute(commandId, state);

        RefreshAvaloniaQuickAccessKeyTipBadges();
    }

    private void RefreshAvaloniaQuickAccessKeyTipBadges()
    {
        foreach (var (button, badge) in _avaloniaQuickAccessKeyTipBadges)
            badge.IsVisible = _ribbonKeyTipsVisible && button.IsEffectivelyEnabled;
    }

    internal string? AvaloniaQuickAccessKeyTipForTest(string commandId) =>
        _avaloniaQuickAccessKeyTipButtons
            .FirstOrDefault(entry => string.Equals(entry.Value.Tag as string, commandId, StringComparison.OrdinalIgnoreCase))
            .Key;

    internal bool AvaloniaQuickAccessKeyTipVisibleForTest(string commandId) =>
        _avaloniaQuickAccessKeyTipButtons
            .FirstOrDefault(entry => string.Equals(entry.Value.Tag as string, commandId, StringComparison.OrdinalIgnoreCase))
            .Value is { } button &&
        _avaloniaQuickAccessKeyTipBadges.TryGetValue(button, out var badge) &&
        badge.IsVisible;

    private static string FormatAvaloniaQuickAccessKeyTip(int visibleIndex)
    {
        if (visibleIndex <= 9)
            return visibleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var offset = visibleIndex - 9;
        const string extraKeyTipCharacters = "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        return offset <= extraKeyTipCharacters.Length
            ? $"0{extraKeyTipCharacters[offset - 1]}"
            : visibleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsAvaloniaQuickAccessHistoryCommand(string commandId) =>
        string.Equals(commandId, QuickAccessToolbarCommandIds.Undo, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(commandId, QuickAccessToolbarCommandIds.Redo, StringComparison.OrdinalIgnoreCase);

    private void ApplyBackstageRecentFileAction(
        Free.Shared.AppServices.RecentFileEntry entry,
        BackstageRecentFileMenuAction action) =>
        ApplyBackstageRecentFileAction(entry.Path, action);

    private void ApplyBackstageRecentFileAction(
        string path,
        BackstageRecentFileMenuAction action)
    {
        switch (action)
        {
            case BackstageRecentFileMenuAction.Pin:
                _recentFiles.Pin(path);
                break;
            case BackstageRecentFileMenuAction.Unpin:
                _recentFiles.Unpin(path);
                break;
            case BackstageRecentFileMenuAction.Remove:
                _recentFiles.Remove(path);
                break;
        }

        NavigateBackstageOverlay(FreeXBackstagePaneId.Home);
    }
}
