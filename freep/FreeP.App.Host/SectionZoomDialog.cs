using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal sealed class SectionZoomDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ZoomSingleTargetDialogSession _session;
    private readonly ComboBox _targetCombo;

    internal string? SelectedTargetSectionId => _session.SelectedTargetId;

    internal SectionZoomDialog(
        IReadOnlyList<(string Id, string DisplayName)> options,
        string? title = null,
        string? selectedTargetId = null)
    {
        _session = new ZoomSingleTargetDialogSession(
            ZoomTargetDialogKind.Section,
            options,
            selectedTargetId,
            title);
        var surface = _session.Surface;
        Title = surface.Title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ZoomDialogChrome.Apply(this, surface);

        _targetCombo = new ComboBox
        {
            ItemsSource = _session.Options,
            DisplayMemberPath = nameof(ZoomTargetOption.DisplayName),
            SelectedIndex = _session.InitialSelectedIndex,
            MinWidth = 260,
        };
        ZoomDialogChrome.ApplyField(_targetCombo, surface.Field(ZoomTargetDialogField.Target));

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
        Grid.SetRow(_targetCombo, 0);
        Grid.SetColumn(_targetCombo, 1);
        grid.Children.Add(_targetCombo);

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
        if (_session.TryAccept(_targetCombo.SelectedIndex))
            DialogResult = true;
    }
}
