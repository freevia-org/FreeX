using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class SummaryZoomCoverImageTargetDialog : FreePDialogWindow
{
    private readonly ZoomSingleTargetDialogNativeBinding<ComboBox> _binding;
    private ZoomSingleTargetDialogSession _session => _binding.Session;

    internal string? SelectedTargetSectionId => _binding.SelectedTargetId;

    internal SummaryZoomCoverImageTargetDialog(IReadOnlyList<(string Id, string DisplayName)> options)
    {
        _binding = new(
            ZoomTargetDialogKind.SummaryCoverImage,
            options,
            session => new ComboBox
            {
                ItemsSource = session.Options,
                SelectedIndex = session.InitialSelectedIndex,
                MinWidth = 230,
            },
            static control => control.SelectedIndex,
            () => Close(true));
        var surface = _session.Surface;
        Title = surface.Title;
        Width = 420;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ZoomDialogChrome.Apply(this, surface);

        var target = _binding.Control;
        ZoomDialogChrome.ApplyField(target, surface.Field(ZoomTargetDialogField.Target));
        var ok = ZoomDialogChrome.MakeButton(
            surface.Action(ZoomTargetDialogAction.Accept),
            Apply,
            _binding.Session.CanAccept);
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = surface.Field(ZoomTargetDialogField.Target).Label },
                target,
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
        _binding.TryAccept();
    }
}
