using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal sealed class SingleTargetZoomDialog : Free.Shared.Ribbon.Wpf.DialogWindow
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
                DisplayMemberPath = nameof(ZoomTargetOption.DisplayName),
                SelectedIndex = session.InitialSelectedIndex,
                MinWidth = 260,
            },
            static control => control.SelectedIndex,
            () => DialogResult = true,
            selectedTargetId,
            title);
        var surface = _session.Surface;
        Title = surface.Title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ZoomDialogChrome.Apply(this, surface);

        var targetCombo = _binding.Control;
        ZoomDialogChrome.ApplyField(targetCombo, surface.Field(ZoomTargetDialogField.Target));

        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new Label
        {
            Content = surface.Field(ZoomTargetDialogField.Target).Label,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);
        Grid.SetRow(targetCombo, 0);
        Grid.SetColumn(targetCombo, 1);
        grid.Children.Add(targetCombo);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var ok = ZoomDialogChrome.MakeButton(
            surface.Action(ZoomTargetDialogAction.Accept),
            Apply,
            _session.CanAccept);
        ok.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(ok);
        buttons.Children.Add(ZoomDialogChrome.MakeButton(
            surface.Action(ZoomTargetDialogAction.Cancel),
            () => DialogResult = false));
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;
    }

    private void Apply()
    {
        _binding.TryAccept();
    }
}
