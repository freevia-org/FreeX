using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class SummaryZoomCoverImageTargetDialog : Window
{
    private readonly ZoomSingleTargetDialogSession _session;
    private readonly ComboBox _target;

    internal string? SelectedTargetSectionId => _session.SelectedTargetId;

    internal SummaryZoomCoverImageTargetDialog(IReadOnlyList<(string Id, string DisplayName)> options)
    {
        _session = new ZoomSingleTargetDialogSession(options);
        Title = ZoomCoverImagePlanner.DialogTitle;
        Width = 420;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ZoomDialogChrome.Apply(this);

        _target = new ComboBox
        {
            ItemsSource = _session.Options,
            SelectedIndex = _session.InitialSelectedIndex,
            MinWidth = 230,
        };
        var ok = ZoomDialogChrome.MakeButton("OK", true, Apply);
        ok.IsEnabled = _session.CanAccept;
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Summary Zoom tile:" },
                _target,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { ok, ZoomDialogChrome.MakeButton("Cancel", false, () => Close(false)) },
                },
            },
        };
    }

    private void Apply()
    {
        if (_session.TryAccept(_target.SelectedIndex))
            Close(true);
    }
}
