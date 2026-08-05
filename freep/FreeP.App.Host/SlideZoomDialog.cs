using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal sealed class SlideZoomDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ZoomSingleTargetDialogSession _session;
    private readonly ComboBox _targetCombo;

    internal string? SelectedTargetSlideId => _session.SelectedTargetId;

    internal SlideZoomDialog(
        IReadOnlyList<(string Id, string DisplayName)> options,
        string? title = null,
        string? selectedTargetId = null)
    {
        _session = new ZoomSingleTargetDialogSession(options, selectedTargetId);
        Title = title ?? SlideZoomInsertionPlanner.DialogTitle;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _targetCombo = new ComboBox
        {
            ItemsSource = _session.Options,
            DisplayMemberPath = nameof(ZoomTargetOption.DisplayName),
            SelectedIndex = _session.InitialSelectedIndex,
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
        var ok = new Button { Content = "OK", IsDefault = true, IsEnabled = _session.CanAccept, MinWidth = 75, Margin = new Thickness(0, 0, 8, 0) };
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
        if (_session.TryAccept(_targetCombo.SelectedIndex))
            DialogResult = true;
    }
}
