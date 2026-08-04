using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal sealed class SummaryZoomDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ListBox _targetList;

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

        var items = options.Select(option => new TargetOption(option.Id, option.DisplayName)).ToArray();
        _targetList = new ListBox
        {
            ItemsSource = items,
            SelectionMode = SelectionMode.Extended,
            MinHeight = 180,
        };
        foreach (var item in items)
            if (selectedTargetIds?.Contains(item.Id, StringComparer.OrdinalIgnoreCase) == true)
                _targetList.SelectedItems.Add(item);

        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new Label { Content = "Target sections (select at least two):" };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);
        Grid.SetRow(_targetList, 1);
        grid.Children.Add(_targetList);

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
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;
    }

    private void Apply()
    {
        var selected = _targetList.SelectedItems.OfType<TargetOption>().Select(option => option.Id).ToArray();
        if (selected.Length >= 2)
        {
            SelectedTargetSectionIds = selected;
            DialogResult = true;
        }
    }

    private sealed record TargetOption(string Id, string DisplayName);
}
