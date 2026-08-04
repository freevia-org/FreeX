using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal sealed class SummaryZoomDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ListBox _targetList;
    private readonly ObservableCollection<TargetOption> _items;

    internal IReadOnlyList<string> SelectedTargetSectionIds { get; private set; } = Array.Empty<string>();

    internal SummaryZoomDialog(
        IReadOnlyList<(string Id, string DisplayName)> options,
        string? title = null,
        IReadOnlyCollection<string>? selectedTargetIds = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        Title = title ?? SummaryZoomInsertionPlanner.DialogTitle;
        Width = 460;
        Height = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _items = new ObservableCollection<TargetOption>(
            options.Select(option => new TargetOption(option.Id, option.DisplayName)));
        _targetList = new ListBox
        {
            ItemsSource = _items,
            SelectionMode = SelectionMode.Extended,
            MinHeight = 180,
        };
        foreach (var item in _items)
            if (selectedTargetIds?.Contains(item.Id, StringComparer.OrdinalIgnoreCase) == true)
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
        var ok = new Button { Content = "OK", IsDefault = true, IsEnabled = options.Count >= 2, MinWidth = 75, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Apply();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 75 });
        Grid.SetRow(buttons, 3);
        grid.Children.Add(buttons);
        Content = grid;
    }

    private void Apply()
    {
        var selectedIds = _targetList.SelectedItems.OfType<TargetOption>().Select(option => option.Id).ToArray();
        var selected = SummaryZoomTargetPlanner.SelectOrderedTargets(
            _items.Select(item => item.Id), selectedIds);
        if (selected.Count >= 2)
        {
            SelectedTargetSectionIds = selected;
            DialogResult = true;
        }
    }

    private void MoveSelected(ObservableCollection<TargetOption> items, int delta)
    {
        var selected = _targetList.SelectedItems.OfType<TargetOption>().ToArray();
        if (selected.Length != 1)
            return;

        var index = items.IndexOf(selected[0]);
        var targetIndex = index + delta;
        if (index < 0 || targetIndex < 0 || targetIndex >= items.Count)
            return;

        items.Move(index, targetIndex);
        _targetList.SelectedItem = selected[0];
    }

    private sealed record TargetOption(string Id, string DisplayName);
}
