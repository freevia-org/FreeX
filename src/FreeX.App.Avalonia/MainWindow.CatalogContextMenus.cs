using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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

    internal StackPanel AvaloniaQuickAccessToolbarForTest => _avaloniaQuickAccessToolbar;

    private Control BuildQuickAccessToolbar()
    {
        _avaloniaQuickAccessOptions = AppOptionsStore.Load();
        RebuildAvaloniaQuickAccessToolbar();

        var host = new Border
        {
            Background = Brushes.White,
            BorderBrush = ToolbarBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            MinHeight = 28,
            Child = _avaloniaQuickAccessToolbar,
        };
        AutomationProperties.SetAutomationId(host, "QuickAccessToolbar");
        AutomationProperties.SetName(host, "Quick Access Toolbar");
        return host;
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
            Content = AvaloniaRibbonIcons.BuildMonochrome(command.IconKind, 16, command.Id, PrimaryInk),
            Tag = command.Id,
        };
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
                PrimaryInk),
            Tag = $"{command.Id}.History",
        };
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
                new QuickAccessToolbarCustomizationMenuState(
                    commandId,
                    _avaloniaQuickAccessOptions?.QuickAccessToolbarCommands ?? QuickAccessToolbarCatalog.DefaultCommandIds),
                UiText.Get,
                ApplyAvaloniaQuickAccessCustomization));

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
        if (_avaloniaQuickAccessOptions is null)
            return;

        var action = command.Action switch
        {
            QuickAccessToolbarMenuAction.Add => QuickAccessToolbarCustomizationAction.Add,
            QuickAccessToolbarMenuAction.Remove => QuickAccessToolbarCustomizationAction.Remove,
            _ => (QuickAccessToolbarCustomizationAction?)null,
        };
        if (action is null)
            return;

        _avaloniaQuickAccessOptions.QuickAccessToolbarCommands =
            QuickAccessToolbarCustomizationPlanner.Apply(
                _avaloniaQuickAccessOptions.QuickAccessToolbarCommands,
                command.CommandId,
                action.Value).ToList();
        AppOptionsStore.Save(_avaloniaQuickAccessOptions);
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
