using System.Windows;
using System.Windows.Controls;
using Free.Shared.Ribbon.Wpf;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal sealed class SummaryZoomCoverImageTargetDialog : DialogWindow
{
    private readonly ComboBox _target;

    internal string? SelectedTargetSectionId { get; private set; }

    internal SummaryZoomCoverImageTargetDialog(IReadOnlyList<(string Id, string DisplayName)> options)
    {
        Title = ZoomCoverImagePlanner.DialogTitle;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var items = options.Select(option => new TargetOption(option.Id, option.DisplayName)).ToArray();
        _target = new ComboBox { ItemsSource = items, SelectedIndex = items.Length == 0 ? -1 : 0, MinWidth = 230 };

        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new Label { Content = "Summary Zoom tile:" };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);
        Grid.SetRow(_target, 1);
        grid.Children.Add(_target);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var ok = new Button { Content = "OK", IsDefault = true, IsEnabled = items.Length > 0, MinWidth = 75, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Apply();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 75 });
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;
    }

    private void Apply()
    {
        if (_target.SelectedItem is TargetOption option)
        {
            SelectedTargetSectionId = option.Id;
            DialogResult = true;
        }
    }

    private sealed record TargetOption(string Id, string DisplayName);
}
