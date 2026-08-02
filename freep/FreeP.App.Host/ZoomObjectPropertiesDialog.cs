using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

internal sealed class ZoomObjectPropertiesDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly CheckBox _returnToParent;
    private readonly CheckBox _showBackground;
    private readonly ComboBox _imageType;
    private readonly TextBox _transitionDuration;
    private readonly TextBox _cropEdges;

    internal ZoomObjectProperties Properties { get; private set; }

    internal ZoomObjectPropertiesDialog(ZoomObjectProperties current)
    {
        Title = ZoomObjectPropertiesPlanner.DialogTitle;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _returnToParent = new CheckBox
        {
            Content = "Return to parent after following the Zoom",
            IsChecked = current.ReturnToParent ?? true,
        };
        _showBackground = new CheckBox
        {
            Content = "Show destination slide background",
            IsChecked = current.ShowBackground ?? true,
        };
        _imageType = new ComboBox
        {
            ItemsSource = new[] { "preview", "cover" },
            SelectedItem = ZoomObjectPropertiesPlanner.IsSupportedImageType(current.ImageType)
                ? current.ImageType!.ToLowerInvariant()
                : "preview",
            MinWidth = 180,
        };
        _transitionDuration = new TextBox
        {
            Text = current.TransitionDuration ?? string.Empty,
            MinWidth = 180,
        };
        _cropEdges = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatCropEdges(current),
            MinWidth = 180,
            ToolTip = "left, top, right, bottom as percentages; for example 0, 5, 0, 5",
        };

        var grid = new Grid { Margin = new Thickness(14) };
        for (var i = 0; i < 6; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddRow(grid, 0, "Image source:", _imageType);
        AddRow(grid, 1, "Transition duration:", _transitionDuration);
        AddRow(grid, 2, "Preview crop (%):", _cropEdges);
        Grid.SetRow(_returnToParent, 3);
        Grid.SetColumnSpan(_returnToParent, 2);
        grid.Children.Add(_returnToParent);
        Grid.SetRow(_showBackground, 4);
        Grid.SetColumnSpan(_showBackground, 2);
        grid.Children.Add(_showBackground);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 75, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Apply();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 75 });
        Grid.SetRow(buttons, 5);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;
        Properties = current;
    }

    private static void AddRow(Grid grid, int row, string labelText, Control control)
    {
        var label = new Label { Content = labelText, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(label, row);
        grid.Children.Add(label);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
    }

    private void Apply()
    {
        var imageType = _imageType.SelectedItem as string ?? "preview";
        if (!ZoomObjectPropertiesPlanner.TryParseCropEdges(
                _cropEdges.Text, out var cropLeft, out var cropTop, out var cropRight, out var cropBottom))
        {
            MessageBox.Show(this,
                "Crop edges must be four percentages: left, top, right, bottom.",
                ZoomObjectPropertiesPlanner.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        Properties = new ZoomObjectProperties(
            _returnToParent.IsChecked == true,
            imageType,
            string.IsNullOrWhiteSpace(_transitionDuration.Text) ? null : _transitionDuration.Text.Trim(),
            _showBackground.IsChecked == true,
            cropLeft,
            cropTop,
            cropRight,
            cropBottom);
        DialogResult = true;
    }
}
