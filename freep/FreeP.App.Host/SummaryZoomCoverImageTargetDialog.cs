using System.Windows;
using System.Windows.Controls;
using Free.Shared.Ribbon.Wpf;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal sealed class SummaryZoomCoverImageTargetDialog : DialogWindow
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
                DisplayMemberPath = nameof(ZoomTargetOption.DisplayName),
                SelectedIndex = session.InitialSelectedIndex,
                MinWidth = 230,
            },
            static control => control.SelectedIndex,
            () => DialogResult = true);
        var surface = _session.Surface;
        Title = surface.Title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ZoomDialogChrome.Apply(this, surface);

        var target = _binding.Control;
        ZoomDialogChrome.ApplyField(target, surface.Field(ZoomTargetDialogField.Target));

        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new Label { Content = surface.Field(ZoomTargetDialogField.Target).Label };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);
        Grid.SetRow(target, 1);
        grid.Children.Add(target);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var ok = ZoomDialogChrome.MakeButton(
            surface.Action(ZoomTargetDialogAction.Accept),
            Apply,
            _binding.Session.CanAccept);
        ok.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(ok);
        buttons.Children.Add(ZoomDialogChrome.MakeButton(
            surface.Action(ZoomTargetDialogAction.Cancel),
            () => DialogResult = false));
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;
    }

    private void Apply()
    {
        _binding.TryAccept();
    }
}
