using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class SingleTargetZoomDialog : FreePDialogWindow
{
    private readonly ZoomSingleTargetDialogNativeBinding<ComboBox> _binding;
    private ZoomSingleTargetDialogSession _session => _binding.Session;

    internal ZoomTargetDialogKind TargetKind => _session.Kind;
    internal string? SelectedTargetId => _binding.SelectedTargetId;

    internal SingleTargetZoomDialog(
        ZoomTargetDialogKind kind,
        IReadOnlyList<(string Id, string DisplayName)> options,
        string? title = null,
        string? selectedTargetId = null)
    {
        if (kind is not ZoomTargetDialogKind.Slide and not ZoomTargetDialogKind.Section)
            throw new ArgumentOutOfRangeException(nameof(kind));

        _binding = new(
            kind,
            options,
            session => new ComboBox
            {
                ItemsSource = session.Options,
                SelectedIndex = session.InitialSelectedIndex,
                MinWidth = 260,
            },
            static control => control.SelectedIndex,
            () => Close(true),
            selectedTargetId,
            title);
        var surface = _session.Surface;
        Title = surface.Title;
        Width = 420;
        Height = 160;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ZoomDialogChrome.Apply(this, surface);

        var targetCombo = _binding.Control;
        ZoomDialogChrome.ApplyField(targetCombo, surface.Field(ZoomTargetDialogField.Target));

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
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("115, *"),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = surface.Field(ZoomTargetDialogField.Target).Label,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                        targetCombo,
                    },
                },
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
        Grid.SetColumn(targetCombo, 1);
    }

    private void Apply()
    {
        _binding.TryAccept();
    }
}
