using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal sealed class SummaryZoomDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly SummaryZoomDialogSession _session;
    private readonly ListBox _targetList;
    private readonly ObservableCollection<ZoomTargetOption> _items;

    internal IReadOnlyList<string> SelectedTargetSectionIds => _session.SelectedTargetIds;

    internal SummaryZoomDialog(
        IReadOnlyList<(string Id, string DisplayName)> options,
        string? title = null,
        IReadOnlyCollection<string>? selectedTargetIds = null)
    {
        _session = new SummaryZoomDialogSession(options, selectedTargetIds);
        Title = title ?? SummaryZoomInsertionPlanner.DialogTitle;
        Width = 460;
        Height = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _items = new ObservableCollection<ZoomTargetOption>(_session.Options);
        _targetList = new ListBox
        {
            ItemsSource = _items,
            SelectionMode = SelectionMode.Extended,
            MinHeight = 180,
        };
        foreach (var item in _items)
            if (_session.InitialSelectedTargetIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
                _targetList.SelectedItems.Add(item);

        var moveUp = new Button { Content = "Move Up", MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        moveUp.Click += (_, _) => MoveSelected(_items, -1);
        var moveDown = new Button { Content = "Move Down", MinWidth = 80 };
        moveDown.Click += (_, _) => MoveSelected(_items, 1);

        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new Label { Content = "Target sections (select at least two):" };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);
        Grid.SetRow(_targetList, 1);
        grid.Children.Add(_targetList);

        var reorder = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
        };
        reorder.Children.Add(moveUp);
        reorder.Children.Add(moveDown);
        Grid.SetRow(reorder, 2);
        grid.Children.Add(reorder);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var ok = new Button { Content = "OK", IsDefault = true, IsEnabled = _session.CanAccept, MinWidth = 75, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Apply();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 75 });
        Grid.SetRow(buttons, 3);
        grid.Children.Add(buttons);
        Content = grid;
    }

    private void Apply()
    {
        var selectedIds = _targetList.SelectedItems
            .OfType<ZoomTargetOption>()
            .Select(option => option.Id)
            .ToArray();
        if (_session.TryAccept(selectedIds))
            DialogResult = true;
    }

    private void MoveSelected(ObservableCollection<ZoomTargetOption> items, int delta)
    {
        var selectedIds = _targetList.SelectedItems
            .OfType<ZoomTargetOption>()
            .Select(option => option.Id)
            .ToArray();
        if (!_session.TryMoveSelected(selectedIds, delta, out var plan))
            return;

        items.Move(plan!.FromIndex, plan.ToIndex);
        _targetList.SelectedItem = items[plan.ToIndex];
    }
}
