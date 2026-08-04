using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

internal sealed class ZoomObjectPropertiesDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly CheckBox _returnToParent;
    private readonly CheckBox _showBackground;
    private readonly CheckBox _transitionEnabled;
    private readonly CheckBox _frameBorderEnabled;
    private readonly ComboBox _imageType;
    private readonly TextBox _transitionDuration;
    private readonly TextBox _frameBorderColor;
    private readonly TextBox _frameBorderWidth;
    private readonly ComboBox _frameBorderDash;
    private readonly ComboBox _frameGeometry;
    private readonly TextBox _cropEdges;
    private readonly IReadOnlyList<SummaryZoomTarget> _summaryTargets;
    private readonly IReadOnlyList<ZoomObjectProperties> _summaryTileProperties;
    private readonly ComboBox? _summaryTile;
    private readonly TextBox? _summaryOffset;
    private readonly TextBox? _summaryScale;
    private readonly CheckBox? _applySummaryPropertiesToAllTiles;

    internal ZoomObjectProperties Properties { get; private set; }
    internal ZoomObjectPropertiesPlanner.SummaryZoomTileLayoutEdit? SummaryTileLayout { get; private set; }
    internal ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit? SummaryTileProperties { get; private set; }
    internal bool ApplySummaryPropertiesToAllTiles => _applySummaryPropertiesToAllTiles?.IsChecked == true;

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
        _transitionEnabled = new CheckBox
        {
            Content = "Use Zoom transition",
            IsChecked = ZoomObjectPropertiesPlanner.IsTransitionEnabled(current),
        };
        _transitionEnabled.Checked += (_, _) => SyncTransitionState();
        _transitionEnabled.Unchecked += (_, _) => SyncTransitionState();
        _frameBorderColor = new TextBox
        {
            Text = current.FrameBorderColor ?? string.Empty,
            MinWidth = 180,
            ToolTip = "six-digit RGB value; for example 4472C4",
        };
        _frameBorderWidth = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderWidth(current),
            MinWidth = 180,
            ToolTip = "positive width in points; for example 1.5",
        };
        _frameBorderDash = new ComboBox
        {
            ItemsSource = ZoomObjectPropertiesPlanner.FrameBorderDashOptions,
            SelectedItem = current.FrameBorderDash ?? OutlineDash.Solid,
            MinWidth = 180,
        };
        _frameBorderEnabled = new CheckBox
        {
            Content = "Use Zoom border",
            IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderEnabled(current),
        };
        _frameBorderEnabled.Checked += (_, _) => SyncFrameBorderState();
        _frameBorderEnabled.Unchecked += (_, _) => SyncFrameBorderState();
        _frameGeometry = new ComboBox
        {
            ItemsSource = ZoomObjectPropertiesPlanner.FrameGeometryOptions,
            SelectedItem = ZoomObjectPropertiesPlanner.FrameGeometryOptions.FirstOrDefault(
                geometry => string.Equals(geometry, current.FrameGeometry, StringComparison.OrdinalIgnoreCase))
                ?? "rect",
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
            _applySummaryPropertiesToAllTiles = new CheckBox
            {
                Content = "Apply format to all Summary Zoom tiles",
                Margin = new Thickness(0, 4, 0, 0),
            };
            _summaryTile.SelectionChanged += (_, _) => LoadSummaryTileFields();
        }

        var grid = new Grid { Margin = new Thickness(14) };
        for (var i = 0; i < 12 + (_summaryTargets.Count > 0 ? 4 : 0); i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;
        AddRow(grid, row++, "Image source:", _imageType);
        Grid.SetRow(_transitionEnabled, row++);
        Grid.SetColumnSpan(_transitionEnabled, 2);
        grid.Children.Add(_transitionEnabled);
        AddRow(grid, row++, "Transition duration:", _transitionDuration);
        Grid.SetRow(_frameBorderEnabled, row++);
        Grid.SetColumnSpan(_frameBorderEnabled, 2);
        grid.Children.Add(_frameBorderEnabled);
        AddRow(grid, row++, "Border color:", _frameBorderColor);
        AddRow(grid, row++, "Border width (pt):", _frameBorderWidth);
        AddRow(grid, row++, "Border dash:", _frameBorderDash);
        AddRow(grid, row++, "Frame shape:", _frameGeometry);
        AddRow(grid, row++, "Preview crop (%):", _cropEdges);
        if (_summaryTile is not null)
        {
            AddRow(grid, row++, "Summary tile:", _summaryTile);
            AddRow(grid, row++, "Tile position (%):", _summaryOffset!);
            AddRow(grid, row++, "Tile scale (%):", _summaryScale!);
            Grid.SetRow(_applySummaryPropertiesToAllTiles!, row++);
            Grid.SetColumnSpan(_applySummaryPropertiesToAllTiles!, 2);
            grid.Children.Add(_applySummaryPropertiesToAllTiles);
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
        SyncTransitionState();
        SyncFrameBorderState();
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
        if (!ZoomObjectPropertiesPlanner.TryParseTransitionDuration(
                _transitionDuration.Text,
                _transitionEnabled.IsChecked == true,
                out var transitionDuration))
        {
            MessageBox.Show(this,
                ZoomObjectPropertiesPlanner.InvalidTransitionDurationMessage,
                ZoomObjectPropertiesPlanner.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderColor(
                _frameBorderColor.Text,
                _frameBorderEnabled.IsChecked == true,
                out var frameBorderColor))
        {
            MessageBox.Show(this,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderColorMessage,
                ZoomObjectPropertiesPlanner.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderWidth(
                _frameBorderWidth.Text,
                _frameBorderEnabled.IsChecked == true,
                out var frameBorderWidth))
        {
            MessageBox.Show(this,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderWidthMessage,
                ZoomObjectPropertiesPlanner.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        var frameBorderDashText = _frameBorderEnabled.IsChecked == true
            ? _frameBorderDash.SelectedItem?.ToString()
            : null;
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderDash(
                frameBorderDashText,
                out var frameBorderDash))
        {
            MessageBox.Show(this,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderDashMessage,
                ZoomObjectPropertiesPlanner.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        if (!ZoomObjectPropertiesPlanner.TryParseFrameGeometry(
                _frameGeometry.SelectedItem?.ToString(), out var frameGeometry))
        {
            MessageBox.Show(this,
                ZoomObjectPropertiesPlanner.InvalidFrameGeometryMessage,
                ZoomObjectPropertiesPlanner.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        if (!ZoomObjectPropertiesPlanner.TryParseCropEdges(
                _cropEdges.Text, out var cropLeft, out var cropTop, out var cropRight, out var cropBottom))
        {
            MessageBox.Show(this,
                ZoomObjectPropertiesPlanner.InvalidCropEdgesMessage,
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
                    ZoomObjectPropertiesPlanner.InvalidSummaryTileLayoutMessage,
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
            transitionDuration,
            _showBackground.IsChecked == true,
            cropLeft,
            cropTop,
            cropRight,
            cropBottom,
            frameBorderColor,
            frameBorderWidth,
            frameBorderDash,
            frameGeometry);
        if (_summaryTile is not null && _summaryTile.SelectedIndex >= 0
            && _summaryTile.SelectedIndex < _summaryTargets.Count)
        {
            if (!ApplySummaryPropertiesToAllTiles)
            {
                SummaryTileProperties = new ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit(
                    _summaryTargets[_summaryTile.SelectedIndex].SectionId,
                    Properties);
            }
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
        _transitionEnabled.IsChecked = ZoomObjectPropertiesPlanner.IsTransitionEnabled(properties);
        SyncTransitionState();
        _frameBorderColor.Text = properties.FrameBorderColor ?? string.Empty;
        _frameBorderWidth.Text = ZoomObjectPropertiesPlanner.FormatFrameBorderWidth(properties);
        _frameBorderDash.SelectedItem = properties.FrameBorderDash ?? OutlineDash.Solid;
        _frameBorderEnabled.IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderEnabled(properties);
        SyncFrameBorderState();
        _frameGeometry.SelectedItem = ZoomObjectPropertiesPlanner.FrameGeometryOptions.FirstOrDefault(
            geometry => string.Equals(geometry, properties.FrameGeometry, StringComparison.OrdinalIgnoreCase))
            ?? "rect";
        _cropEdges.Text = ZoomObjectPropertiesPlanner.FormatCropEdges(properties);
        _returnToParent.IsChecked = properties.ReturnToParent ?? true;
        _showBackground.IsChecked = properties.ShowBackground ?? true;
        _summaryOffset.Text = ZoomObjectPropertiesPlanner.FormatFactorPair(
            target.OffsetFactorX, target.OffsetFactorY);
        _summaryScale.Text = ZoomObjectPropertiesPlanner.FormatFactorPair(
            target.ScaleFactorX, target.ScaleFactorY);
    }

    private void SyncTransitionState() =>
        _transitionDuration.IsEnabled = _transitionEnabled.IsChecked == true;

    private void SyncFrameBorderState()
    {
        var enabled = _frameBorderEnabled.IsChecked == true;
        _frameBorderColor.IsEnabled = enabled;
        _frameBorderWidth.IsEnabled = enabled;
        _frameBorderDash.IsEnabled = enabled;
    }
}
