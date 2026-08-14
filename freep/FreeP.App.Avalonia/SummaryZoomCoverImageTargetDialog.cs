using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class SummaryZoomCoverImageTargetDialog : FreePDialogWindow
{
    private readonly ZoomSingleTargetDialogSession _session;
    private readonly ComboBox _target;

    internal string? SelectedTargetSectionId => _session.SelectedTargetId;

    internal SummaryZoomCoverImageTargetDialog(IReadOnlyList<(string Id, string DisplayName)> options)
    {
        _session = new ZoomSingleTargetDialogSession(
            ZoomTargetDialogKind.SummaryCoverImage,
            options);
        var surface = _session.Surface;
        Title = surface.Title;
        Width = 420;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ZoomDialogChrome.Apply(this, surface);

        _target = new ComboBox
        {
            ItemsSource = _session.Options,
            SelectedIndex = _session.InitialSelectedIndex,
            MinWidth = 230,
        };
        ZoomDialogChrome.ApplyField(_target, surface.Field(ZoomTargetDialogField.Target));
        var ok = ZoomDialogChrome.MakeButton(
            surface.Action(ZoomTargetDialogAction.Accept),
            Apply,
            _session.CanAccept);
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = surface.Field(ZoomTargetDialogField.Target).Label },
                _target,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        ok,
                        ZoomDialogChrome.MakeButton(
                            surface.Action(ZoomTargetDialogAction.Cancel),
                            () => Close(false)),
                    },
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
