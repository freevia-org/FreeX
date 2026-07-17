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
    private AppOptions? _avaloniaQuickAccessOptions;
    private Panel? _avaloniaQuickAccessTitleBarHost;

    internal StackPanel AvaloniaQuickAccessToolbarForTest => _avaloniaQuickAccessToolbar;
    internal Panel? AvaloniaQuickAccessTitleBarHostForTest => _avaloniaQuickAccessTitleBarHost;

    private void PopulateQuickAccessToolbar(Panel titleBarHost)
    {
        ArgumentNullException.ThrowIfNull(titleBarHost);

        _avaloniaQuickAccessTitleBarHost = titleBarHost;
        titleBarHost.Children.Clear();
        titleBarHost.Children.Add(_avaloniaQuickAccessToolbar);
        AutomationProperties.SetAutomationId(_avaloniaQuickAccessToolbar, "QuickAccessToolbar");
        AutomationProperties.SetName(_avaloniaQuickAccessToolbar, "Quick Access Toolbar");
        WindowDecorationProperties.SetElementRole(
            _avaloniaQuickAccessToolbar,
            WindowDecorationsElementRole.User);

        _avaloniaQuickAccessOptions = AppOptionsStore.Load();
        RebuildAvaloniaQuickAccessToolbar();
    }

    private void RebuildAvaloniaQuickAccessToolbar()
    {
        if (_avaloniaQuickAccessOptions is null)
            return;

        _avaloniaQuickAccessToolbar.Children.Clear();
        _avaloniaQuickAccessButtons.Clear();
        var commands = QuickAccessToolbarCatalog.Normalize(_avaloniaQuickAccessOptions.QuickAccessToolbarCommands);
        _avaloniaQuickAccessOptions.QuickAccessToolbarCommands = commands.Select(command => command.Id).ToList();

        foreach (var command in commands)
        {
            var button = CreateAvaloniaQuickAccessButton(command);
            _avaloniaQuickAccessToolbar.Children.Add(button);
            _avaloniaQuickAccessButtons[command.Id] = button;

            if (IsAvaloniaQuickAccessHistoryCommand(command.Id))
                _avaloniaQuickAccessToolbar.Children.Add(CreateAvaloniaQuickAccessHistoryButton(command));
        }

        RefreshAvaloniaQuickAccessToolbarState();
    }

    private Button CreateAvaloniaQuickAccessButton(QuickAccessToolbarCommandDefinition command)
    {
        var button = new Button
        {
            Width = IsAvaloniaQuickAccessHistoryCommand(command.Id) ? 24 : 26,
            Height = 24,
            Padding = new Thickness(3),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = AvaloniaRibbonIcons.BuildMonochrome(command.IconKind, 16, command.Id, StatusBarForeground),
            Tag = command.Id,
        };
        WindowDecorationProperties.SetElementRole(button, WindowDecorationsElementRole.User);
        AutomationProperties.SetAutomationId(button, command.AutomationId);
        AutomationProperties.SetName(button, UiText.Get(command.TitleResourceKey));
        ToolTip.SetTip(button, UiText.Get(command.TitleResourceKey));
        button.Click += (_, args) => ExecuteAvaloniaQuickAccessCommand(command, button, args);
        AttachAvaloniaQuickAccessCustomization(button, command.Id);
        return button;
    }

    private Button CreateAvaloniaQuickAccessHistoryButton(QuickAccessToolbarCommandDefinition command)
    {
        var button = new Button
        {
            Width = 14,
            Height = 24,
            Padding = new Thickness(1),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = AvaloniaRibbonIcons.BuildMonochrome(
                RibbonCommandIconKind.ChevronDown,
                8,
                $"{command.Id}-history",
                StatusBarForeground),
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

    private void ExecuteAvaloniaQuickAccessCommand(
        QuickAccessToolbarCommandDefinition command,
        object sender,
        RoutedEventArgs args)
    {
        switch (command.Id)
        {
            case QuickAccessToolbarCommandIds.Save:
                SaveButton_Click(sender, args);
                return;
            case QuickAccessToolbarCommandIds.Undo:
                UndoLastEdit();
                return;
            case QuickAccessToolbarCommandIds.Redo:
                RedoLastEdit();
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
            HasActiveWorksheet: true,
            HasSelection: true);
        foreach (var (commandId, button) in _avaloniaQuickAccessButtons)
            button.IsEnabled = QuickAccessCommandStateResolver.CanExecute(commandId, state);
    }

    private static bool IsAvaloniaQuickAccessHistoryCommand(string commandId) =>
        string.Equals(commandId, QuickAccessToolbarCommandIds.Undo, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(commandId, QuickAccessToolbarCommandIds.Redo, StringComparison.OrdinalIgnoreCase);

    private void ApplyBackstageRecentFileAction(
        Free.Shared.AppServices.RecentFileEntry entry,
        BackstageRecentFileMenuAction action)
    {
        switch (action)
        {
            case BackstageRecentFileMenuAction.Pin:
                _recentFiles.Pin(entry.Path);
                break;
            case BackstageRecentFileMenuAction.Unpin:
                _recentFiles.Unpin(entry.Path);
                break;
            case BackstageRecentFileMenuAction.Remove:
                _recentFiles.Remove(entry.Path);
                break;
        }

        NavigateBackstageOverlay(FreeXBackstagePaneId.Home);
    }
}
