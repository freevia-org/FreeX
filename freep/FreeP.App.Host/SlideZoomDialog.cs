using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal sealed class SlideZoomDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ComboBox _targetCombo;

    internal string? SelectedTargetSlideId { get; private set; }

    internal SlideZoomDialog(
        IReadOnlyList<(string Id, string DisplayName)> options,
        string? title = null,
        string? selectedTargetId = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        Title = title ?? SlideZoomInsertionPlanner.DialogTitle;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _targetCombo = new ComboBox
        {
            ItemsSource = options.Select(option => new TargetOption(option.Id, option.DisplayName)).ToArray(),
            DisplayMemberPath = nameof(TargetOption.DisplayName),
            SelectedIndex = FindSelectedIndex(options, selectedTargetId),
            MinWidth = 260,
        };

        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new Label { Content = "Target slide:", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);
        Grid.SetRow(_targetCombo, 0);
        Grid.SetColumn(_targetCombo, 1);
        grid.Children.Add(_targetCombo);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var ok = new Button { Content = "OK", IsDefault = true, IsEnabled = options.Count > 0, MinWidth = 75, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Apply();
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 75 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);

        Content = grid;
    }

    private void Apply()
    {
        SelectedTargetSlideId = (_targetCombo.SelectedItem as TargetOption)?.Id;
        if (!string.IsNullOrWhiteSpace(SelectedTargetSlideId))
            DialogResult = true;
    }

    private static int FindSelectedIndex(
        IReadOnlyList<(string Id, string DisplayName)> options,
        string? selectedTargetId)
    {
        if (options.Count == 0)
            return -1;
        for (var index = 0; index < options.Count; index++)
            if (string.Equals(options[index].Id, selectedTargetId, StringComparison.OrdinalIgnoreCase))
                return index;
        return 0;
    }

    private sealed record TargetOption(string Id, string DisplayName);
}
