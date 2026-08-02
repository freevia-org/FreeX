using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal sealed class SectionZoomDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ComboBox _targetCombo;

    internal string? SelectedTargetSectionId { get; private set; }

    internal SectionZoomDialog(IReadOnlyList<(string Id, string DisplayName)> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Title = SectionZoomInsertionPlanner.DialogTitle;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _targetCombo = new ComboBox
        {
            ItemsSource = options.Select(option => new TargetOption(option.Id, option.DisplayName)).ToArray(),
            DisplayMemberPath = nameof(TargetOption.DisplayName),
            SelectedIndex = options.Count == 0 ? -1 : 0,
            MinWidth = 260,
        };

        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new Label { Content = "Target section:", VerticalAlignment = VerticalAlignment.Center };
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
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 75 });
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;
    }

    private void Apply()
    {
        SelectedTargetSectionId = (_targetCombo.SelectedItem as TargetOption)?.Id;
        if (!string.IsNullOrWhiteSpace(SelectedTargetSectionId))
            DialogResult = true;
    }

    private sealed record TargetOption(string Id, string DisplayName);
}
