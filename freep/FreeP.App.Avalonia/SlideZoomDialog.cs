using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class SlideZoomDialog : Window
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
        Height = 160;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ZoomDialogChrome.Apply(this);

        _targetCombo = new ComboBox
        {
            ItemsSource = _session.Options,
            SelectedIndex = _session.InitialSelectedIndex,
            MinWidth = 260,
        };

        var ok = ZoomDialogChrome.MakeButton("OK", true, Apply);
        ok.IsEnabled = _session.CanAccept;
        var cancel = ZoomDialogChrome.MakeButton("Cancel", false, () => Close(false));
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("115, *"),
                    Children =
                    {
                        new TextBlock { Text = "Target slide:", VerticalAlignment = VerticalAlignment.Center },
                        _targetCombo,
                    },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { ok, cancel },
                },
            },
        };
        Grid.SetColumn(_targetCombo, 1);
    }

    private void Apply()
    {
        if (_session.TryAccept(_targetCombo.SelectedIndex))
            Close(true);
    }
}
