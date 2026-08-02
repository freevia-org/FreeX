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
    private readonly IReadOnlyList<SummaryZoomTarget> _summaryTargets;
    private readonly IReadOnlyList<ZoomObjectProperties> _summaryTileProperties;
    private readonly ComboBox? _summaryTile;
    private readonly TextBox? _summaryOffset;
    private readonly TextBox? _summaryScale;

    internal ZoomObjectProperties Properties { get; private set; }
    internal ZoomObjectPropertiesPlanner.SummaryZoomTileLayoutEdit? SummaryTileLayout { get; private set; }
    internal ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit? SummaryTileProperties { get; private set; }

    internal ZoomObjectPropertiesDialog(
        ZoomObjectProperties current,
        IReadOnlyList<SummaryZoomTarget>? summaryTargets = null,
        IReadOnlyList<ZoomObjectProperties>? summaryTileProperties = null)
    {
        _summaryTargets = summaryTargets ?? Array.Empty<SummaryZoomTarget>();
        _summaryTileProperties = summaryTileProperties is { Count: var count }
            && count == _summaryTargets.Count
            ? summaryTileProperties
            : Enumerable.Repeat(current, _summaryTargets.Count).ToArray();
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

        if (_summaryTargets.Count > 0)
        {
            _summaryTile = new ComboBox
            {
                ItemsSource = _summaryTargets
                    .Select(target => string.IsNullOrWhiteSpace(target.Title)
                        ? target.SectionId
                        : target.Title)
                    .ToArray(),
                SelectedIndex = 0,
                MinWidth = 180,
            };
            _summaryOffset = new TextBox { MinWidth = 180 };
            _summaryScale = new TextBox { MinWidth = 180 };
            _summaryTile.SelectionChanged += (_, _) => LoadSummaryTileFields();
        }

        var grid = new Grid { Margin = new Thickness(14) };
        for (var i = 0; i < 6 + (_summaryTargets.Count > 0 ? 3 : 0); i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;
        AddRow(grid, row++, "Image source:", _imageType);
        AddRow(grid, row++, "Transition duration:", _transitionDuration);
        AddRow(grid, row++, "Preview crop (%):", _cropEdges);
        if (_summaryTile is not null)
        {
            AddRow(grid, row++, "Summary tile:", _summaryTile);
            AddRow(grid, row++, "Tile position (%):", _summaryOffset!);
            AddRow(grid, row++, "Tile scale (%):", _summaryScale!);
        }
        Grid.SetRow(_returnToParent, row++);
        Grid.SetColumnSpan(_returnToParent, 2);
        grid.Children.Add(_returnToParent);
        Grid.SetRow(_showBackground, row++);
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
        Grid.SetRow(buttons, row);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;
        Properties = current;
        LoadSummaryTileFields();
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
        if (_summaryTile is not null && _summaryOffset is not null && _summaryScale is not null)
        {
            if (!ZoomObjectPropertiesPlanner.TryParseFactorPair(
                    _summaryOffset.Text, allowNegative: true, out var offsetX, out var offsetY)
                || !ZoomObjectPropertiesPlanner.TryParseFactorPair(
                    _summaryScale.Text, allowNegative: false, out var scaleX, out var scaleY))
            {
                MessageBox.Show(this,
                    "Summary tile position and scale must each be two percentages.",
                    ZoomObjectPropertiesPlanner.DialogTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var target = _summaryTargets[_summaryTile.SelectedIndex];
            SummaryTileLayout = new ZoomObjectPropertiesPlanner.SummaryZoomTileLayoutEdit(
                target.SectionId, offsetX, offsetY, scaleX, scaleY);
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
        if (_summaryTile is not null && _summaryTile.SelectedIndex >= 0
            && _summaryTile.SelectedIndex < _summaryTargets.Count)
        {
            SummaryTileProperties = new ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit(
                _summaryTargets[_summaryTile.SelectedIndex].SectionId,
                Properties);
        }
        DialogResult = true;
    }

    private void LoadSummaryTileFields()
    {
        if (_summaryTile is null || _summaryOffset is null || _summaryScale is null
            || _summaryTile.SelectedIndex < 0
            || _summaryTile.SelectedIndex >= _summaryTargets.Count)
            return;

        var target = _summaryTargets[_summaryTile.SelectedIndex];
        var properties = _summaryTileProperties[_summaryTile.SelectedIndex];
        _imageType.SelectedItem = ZoomObjectPropertiesPlanner.IsSupportedImageType(properties.ImageType)
            ? properties.ImageType!.ToLowerInvariant()
            : "preview";
        _transitionDuration.Text = properties.TransitionDuration ?? string.Empty;
        _cropEdges.Text = ZoomObjectPropertiesPlanner.FormatCropEdges(properties);
        _returnToParent.IsChecked = properties.ReturnToParent ?? true;
        _showBackground.IsChecked = properties.ShowBackground ?? true;
        _summaryOffset.Text = ZoomObjectPropertiesPlanner.FormatFactorPair(
            target.OffsetFactorX, target.OffsetFactorY);
        _summaryScale.Text = ZoomObjectPropertiesPlanner.FormatFactorPair(
            target.ScaleFactorX, target.ScaleFactorY);
    }
}
