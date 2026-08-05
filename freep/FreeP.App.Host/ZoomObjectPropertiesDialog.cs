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
    private readonly CheckBox _frameBorderGradientEnabled;
    private readonly CheckBox _frameBorderPatternEnabled;
    private readonly CheckBox _frameBorderNoFillEnabled;
    private readonly CheckBox _frameBorderThemeEnabled;
    private readonly CheckBox _frameBorderShadowEnabled;
    private readonly CheckBox _frameBorderGlowEnabled;
    private readonly CheckBox _frameBorderSoftEdgeEnabled;
    private readonly ComboBox _imageType;
    private readonly TextBox _transitionDuration;
    private readonly TextBox _frameBorderColor;
    private readonly ComboBox _frameBorderThemeColor;
    private readonly TextBox _frameBorderShadowColor;
    private readonly TextBox _frameBorderShadowAlpha;
    private readonly TextBox _frameBorderShadowBlur;
    private readonly TextBox _frameBorderShadowDistance;
    private readonly TextBox _frameBorderShadowDirection;
    private readonly TextBox _frameBorderGlowColor;
    private readonly TextBox _frameBorderGlowAlpha;
    private readonly TextBox _frameBorderGlowRadius;
    private readonly TextBox _frameBorderSoftEdgeRadius;
    private readonly TextBox _frameBorderWidth;
    private readonly ComboBox _frameBorderDash;
    private readonly TextBox _frameBorderGradientStart;
    private readonly TextBox _frameBorderGradientEnd;
    private readonly TextBox _frameBorderGradientAngle;
    private readonly ComboBox _frameBorderPatternPreset;
    private readonly TextBox _frameBorderPatternForeground;
    private readonly TextBox _frameBorderPatternBackground;
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
        _frameBorderThemeColor = new ComboBox
        {
            ItemsSource = ZoomObjectPropertiesPlanner.FrameBorderThemeColorOptions,
            SelectedItem = current.FrameBorderThemeColor,
            MinWidth = 180,
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
        _frameBorderGradientEnabled = new CheckBox
        {
            Content = "Use gradient border",
            IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderGradientEnabled(current),
        };
        _frameBorderGradientEnabled.Checked += (_, _) => SyncFrameBorderState();
        _frameBorderGradientEnabled.Unchecked += (_, _) => SyncFrameBorderState();
        _frameBorderGradientStart = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderGradientStart(current),
            MinWidth = 180,
            ToolTip = "six-digit RGB value; for example 4472C4",
        };
        _frameBorderGradientEnd = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderGradientEnd(current),
            MinWidth = 180,
            ToolTip = "six-digit RGB value; for example FFFFFF",
        };
        _frameBorderGradientAngle = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderGradientAngle(current),
            MinWidth = 180,
            ToolTip = "linear angle in degrees from 0 to 360",
        };
        _frameBorderPatternEnabled = new CheckBox
        {
            Content = "Use pattern border",
            IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderPatternEnabled(current),
        };
        _frameBorderPatternEnabled.Checked += (_, _) =>
        {
            _frameBorderGradientEnabled.IsChecked = false;
            SyncFrameBorderState();
        };
        _frameBorderPatternEnabled.Unchecked += (_, _) => SyncFrameBorderState();
        _frameBorderGradientEnabled.Checked += (_, _) =>
        {
            _frameBorderPatternEnabled.IsChecked = false;
            SyncFrameBorderState();
        };
        _frameBorderPatternPreset = new ComboBox
        {
            ItemsSource = ZoomObjectPropertiesPlanner.FrameBorderPatternOptions,
            SelectedItem = ZoomObjectPropertiesPlanner.FormatFrameBorderPatternPreset(current),
            MinWidth = 180,
        };
        _frameBorderPatternForeground = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderPatternForeground(current),
            MinWidth = 180,
            ToolTip = "six-digit RGB value; for example 4472C4",
        };
        _frameBorderPatternBackground = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderPatternBackground(current),
            MinWidth = 180,
            ToolTip = "six-digit RGB value; for example FFFFFF",
        };
        _frameBorderNoFillEnabled = new CheckBox
        {
            Content = "Use no-fill border",
            IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderNoFillEnabled(current),
        };
        _frameBorderNoFillEnabled.Checked += (_, _) =>
        {
            _frameBorderGradientEnabled.IsChecked = false;
            _frameBorderPatternEnabled.IsChecked = false;
            SyncFrameBorderState();
        };
        _frameBorderGradientEnabled.Checked += (_, _) =>
        {
            _frameBorderNoFillEnabled.IsChecked = false;
        };
        _frameBorderPatternEnabled.Checked += (_, _) =>
        {
            _frameBorderNoFillEnabled.IsChecked = false;
        };
        _frameBorderThemeEnabled = new CheckBox
        {
            Content = "Use theme border color",
            IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderThemeColorEnabled(current),
        };
        _frameBorderThemeEnabled.Checked += (_, _) =>
        {
            _frameBorderGradientEnabled.IsChecked = false;
            _frameBorderPatternEnabled.IsChecked = false;
            _frameBorderNoFillEnabled.IsChecked = false;
            SyncFrameBorderState();
        };
        _frameBorderThemeEnabled.Unchecked += (_, _) => SyncFrameBorderState();
        _frameBorderShadowEnabled = new CheckBox
        {
            Content = "Use outer border shadow",
            IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderShadowEnabled(current),
        };
        _frameBorderShadowColor = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderShadowColor(current),
            MinWidth = 180,
            ToolTip = "six-digit RGB value; for example 404040",
        };
        _frameBorderShadowAlpha = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderShadowAlpha(current), MinWidth = 180,
        };
        _frameBorderShadowBlur = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderShadowBlur(current), MinWidth = 180,
        };
        _frameBorderShadowDistance = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderShadowDistance(current), MinWidth = 180,
        };
        _frameBorderShadowDirection = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderShadowDirection(current), MinWidth = 180,
        };
        _frameBorderShadowEnabled.Checked += (_, _) => SyncFrameBorderState();
        _frameBorderShadowEnabled.Unchecked += (_, _) => SyncFrameBorderState();
        _frameBorderGlowEnabled = new CheckBox
        {
            Content = "Use border glow",
            IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderGlowEnabled(current),
        };
        _frameBorderGlowColor = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderGlowColor(current),
            MinWidth = 180,
            ToolTip = "six-digit RGB value; for example 4472C4",
        };
        _frameBorderGlowAlpha = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderGlowAlpha(current), MinWidth = 180,
        };
        _frameBorderGlowRadius = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderGlowRadius(current), MinWidth = 180,
        };
        _frameBorderGlowEnabled.Checked += (_, _) => SyncFrameBorderState();
        _frameBorderGlowEnabled.Unchecked += (_, _) => SyncFrameBorderState();
        _frameBorderSoftEdgeEnabled = new CheckBox
        {
            Content = "Use border soft edge",
            IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderSoftEdgeEnabled(current),
        };
        _frameBorderSoftEdgeRadius = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderSoftEdgeRadius(current), MinWidth = 180,
        };
        _frameBorderSoftEdgeEnabled.Checked += (_, _) => SyncFrameBorderState();
        _frameBorderSoftEdgeEnabled.Unchecked += (_, _) => SyncFrameBorderState();
        _frameBorderGradientEnabled.Checked += (_, _) => _frameBorderThemeEnabled.IsChecked = false;
        _frameBorderPatternEnabled.Checked += (_, _) => _frameBorderThemeEnabled.IsChecked = false;
        _frameBorderNoFillEnabled.Checked += (_, _) => _frameBorderThemeEnabled.IsChecked = false;
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
        for (var i = 0; i < 35 + (_summaryTargets.Count > 0 ? 4 : 0); i++)
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
        Grid.SetRow(_frameBorderThemeEnabled, row++);
        Grid.SetColumnSpan(_frameBorderThemeEnabled, 2);
        grid.Children.Add(_frameBorderThemeEnabled);
        AddRow(grid, row++, "Theme color:", _frameBorderThemeColor);
        Grid.SetRow(_frameBorderShadowEnabled, row++);
        Grid.SetColumnSpan(_frameBorderShadowEnabled, 2);
        grid.Children.Add(_frameBorderShadowEnabled);
        AddRow(grid, row++, "Shadow color:", _frameBorderShadowColor);
        AddRow(grid, row++, "Shadow alpha (%):", _frameBorderShadowAlpha);
        AddRow(grid, row++, "Shadow blur (pt):", _frameBorderShadowBlur);
        AddRow(grid, row++, "Shadow distance (pt):", _frameBorderShadowDistance);
        AddRow(grid, row++, "Shadow direction (deg):", _frameBorderShadowDirection);
        Grid.SetRow(_frameBorderGlowEnabled, row++);
        Grid.SetColumnSpan(_frameBorderGlowEnabled, 2);
        grid.Children.Add(_frameBorderGlowEnabled);
        AddRow(grid, row++, "Glow color:", _frameBorderGlowColor);
        AddRow(grid, row++, "Glow alpha (%):", _frameBorderGlowAlpha);
        AddRow(grid, row++, "Glow radius (pt):", _frameBorderGlowRadius);
        Grid.SetRow(_frameBorderSoftEdgeEnabled, row++);
        Grid.SetColumnSpan(_frameBorderSoftEdgeEnabled, 2);
        grid.Children.Add(_frameBorderSoftEdgeEnabled);
        AddRow(grid, row++, "Soft-edge radius (pt):", _frameBorderSoftEdgeRadius);
        AddRow(grid, row++, "Border width (pt):", _frameBorderWidth);
        AddRow(grid, row++, "Border dash:", _frameBorderDash);
        Grid.SetRow(_frameBorderGradientEnabled, row++);
        Grid.SetColumnSpan(_frameBorderGradientEnabled, 2);
        grid.Children.Add(_frameBorderGradientEnabled);
        AddRow(grid, row++, "Gradient start:", _frameBorderGradientStart);
        AddRow(grid, row++, "Gradient end:", _frameBorderGradientEnd);
        AddRow(grid, row++, "Gradient angle (deg):", _frameBorderGradientAngle);
        Grid.SetRow(_frameBorderPatternEnabled, row++);
        Grid.SetColumnSpan(_frameBorderPatternEnabled, 2);
        grid.Children.Add(_frameBorderPatternEnabled);
        AddRow(grid, row++, "Pattern preset:", _frameBorderPatternPreset);
        AddRow(grid, row++, "Pattern foreground:", _frameBorderPatternForeground);
        AddRow(grid, row++, "Pattern background:", _frameBorderPatternBackground);
        Grid.SetRow(_frameBorderNoFillEnabled, row++);
        Grid.SetColumnSpan(_frameBorderNoFillEnabled, 2);
        grid.Children.Add(_frameBorderNoFillEnabled);
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
        var noFillEnabled = _frameBorderEnabled.IsChecked == true
            && _frameBorderNoFillEnabled.IsChecked == true;
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderColor(
                _frameBorderColor.Text,
                _frameBorderEnabled.IsChecked == true
                && _frameBorderGradientEnabled.IsChecked != true
                && _frameBorderPatternEnabled.IsChecked != true
                && _frameBorderThemeEnabled.IsChecked != true
                && !noFillEnabled,
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
        var gradientEnabled = _frameBorderEnabled.IsChecked == true
            && _frameBorderGradientEnabled.IsChecked == true
            && !noFillEnabled;
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderGradient(
                _frameBorderGradientStart.Text,
                _frameBorderGradientEnd.Text,
                _frameBorderGradientAngle.Text,
                gradientEnabled,
                out var frameBorderGradient))
        {
            MessageBox.Show(this,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderGradientMessage,
                ZoomObjectPropertiesPlanner.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        if (gradientEnabled)
            frameBorderColor = null;
        var patternEnabled = _frameBorderEnabled.IsChecked == true
            && _frameBorderPatternEnabled.IsChecked == true
            && !noFillEnabled;
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderPattern(
                _frameBorderPatternPreset.SelectedItem?.ToString(),
                _frameBorderPatternForeground.Text,
                _frameBorderPatternBackground.Text,
                patternEnabled,
                out var frameBorderPattern))
        {
            MessageBox.Show(this,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderPatternMessage,
                ZoomObjectPropertiesPlanner.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        if (patternEnabled)
        {
            frameBorderColor = null;
            frameBorderGradient = null;
        }
        if (noFillEnabled)
        {
            frameBorderColor = null;
            frameBorderGradient = null;
            frameBorderPattern = null;
        }
        var themeColor = _frameBorderEnabled.IsChecked == true
            && _frameBorderThemeEnabled.IsChecked == true
            && !noFillEnabled
            ? _frameBorderThemeColor.SelectedItem is ThemeColorSlot slot ? slot : (ThemeColorSlot?)null
            : null;
        if (themeColor is not null)
        {
            frameBorderColor = null;
            frameBorderGradient = null;
            frameBorderPattern = null;
        }
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderShadow(
                _frameBorderShadowColor.Text,
                _frameBorderShadowAlpha.Text,
                _frameBorderShadowBlur.Text,
                _frameBorderShadowDistance.Text,
                _frameBorderShadowDirection.Text,
                _frameBorderShadowEnabled.IsChecked == true,
                out var frameBorderShadow))
        {
            MessageBox.Show(this,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderShadowMessage,
                ZoomObjectPropertiesPlanner.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderGlow(
                _frameBorderGlowColor.Text,
                _frameBorderGlowAlpha.Text,
                _frameBorderGlowRadius.Text,
                _frameBorderGlowEnabled.IsChecked == true,
                out var frameBorderGlow))
        {
            MessageBox.Show(this,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderGlowMessage,
                ZoomObjectPropertiesPlanner.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderSoftEdge(
                _frameBorderSoftEdgeRadius.Text,
                _frameBorderSoftEdgeEnabled.IsChecked == true,
                out var frameBorderSoftEdge))
        {
            MessageBox.Show(this,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderSoftEdgeMessage,
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
            frameGeometry,
            frameBorderGradient,
            frameBorderPattern,
            noFillEnabled ? true : null,
            themeColor,
            frameBorderShadow,
            _frameBorderShadowEnabled.IsChecked == true ? true : false,
            frameBorderGlow,
            _frameBorderGlowEnabled.IsChecked == true ? true : false,
            frameBorderSoftEdge,
            _frameBorderSoftEdgeEnabled.IsChecked == true ? true : false);
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
        _frameBorderGradientEnabled.IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderGradientEnabled(properties);
        _frameBorderGradientStart.Text = ZoomObjectPropertiesPlanner.FormatFrameBorderGradientStart(properties);
        _frameBorderGradientEnd.Text = ZoomObjectPropertiesPlanner.FormatFrameBorderGradientEnd(properties);
        _frameBorderGradientAngle.Text = ZoomObjectPropertiesPlanner.FormatFrameBorderGradientAngle(properties);
        _frameBorderPatternEnabled.IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderPatternEnabled(properties);
        _frameBorderPatternPreset.SelectedItem = ZoomObjectPropertiesPlanner.FormatFrameBorderPatternPreset(properties);
        _frameBorderPatternForeground.Text = ZoomObjectPropertiesPlanner.FormatFrameBorderPatternForeground(properties);
        _frameBorderPatternBackground.Text = ZoomObjectPropertiesPlanner.FormatFrameBorderPatternBackground(properties);
        _frameBorderNoFillEnabled.IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderNoFillEnabled(properties);
        _frameBorderThemeEnabled.IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderThemeColorEnabled(properties);
        _frameBorderThemeColor.SelectedItem = properties.FrameBorderThemeColor;
        _frameBorderShadowEnabled.IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderShadowEnabled(properties);
        _frameBorderShadowColor.Text = ZoomObjectPropertiesPlanner.FormatFrameBorderShadowColor(properties);
        _frameBorderShadowAlpha.Text = ZoomObjectPropertiesPlanner.FormatFrameBorderShadowAlpha(properties);
        _frameBorderShadowBlur.Text = ZoomObjectPropertiesPlanner.FormatFrameBorderShadowBlur(properties);
        _frameBorderShadowDistance.Text = ZoomObjectPropertiesPlanner.FormatFrameBorderShadowDistance(properties);
        _frameBorderShadowDirection.Text = ZoomObjectPropertiesPlanner.FormatFrameBorderShadowDirection(properties);
        _frameBorderGlowEnabled.IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderGlowEnabled(properties);
        _frameBorderGlowColor.Text = ZoomObjectPropertiesPlanner.FormatFrameBorderGlowColor(properties);
        _frameBorderGlowAlpha.Text = ZoomObjectPropertiesPlanner.FormatFrameBorderGlowAlpha(properties);
        _frameBorderGlowRadius.Text = ZoomObjectPropertiesPlanner.FormatFrameBorderGlowRadius(properties);
        _frameBorderSoftEdgeEnabled.IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderSoftEdgeEnabled(properties);
        _frameBorderSoftEdgeRadius.Text = ZoomObjectPropertiesPlanner.FormatFrameBorderSoftEdgeRadius(properties);
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
        var noFill = enabled && _frameBorderNoFillEnabled.IsChecked == true;
        var gradient = enabled && _frameBorderGradientEnabled.IsChecked == true && !noFill;
        var pattern = enabled && _frameBorderPatternEnabled.IsChecked == true && !noFill;
        var theme = enabled && _frameBorderThemeEnabled.IsChecked == true && !noFill;
        _frameBorderColor.IsEnabled = enabled && !gradient && !pattern && !noFill && !theme;
        _frameBorderWidth.IsEnabled = enabled;
        _frameBorderDash.IsEnabled = enabled;
        _frameBorderGradientEnabled.IsEnabled = enabled;
        _frameBorderGradientStart.IsEnabled = gradient;
        _frameBorderGradientEnd.IsEnabled = gradient;
        _frameBorderGradientAngle.IsEnabled = gradient;
        _frameBorderPatternEnabled.IsEnabled = enabled;
        _frameBorderPatternPreset.IsEnabled = pattern;
        _frameBorderPatternForeground.IsEnabled = pattern;
        _frameBorderPatternBackground.IsEnabled = pattern;
        _frameBorderNoFillEnabled.IsEnabled = enabled;
        _frameBorderThemeEnabled.IsEnabled = enabled;
        _frameBorderThemeColor.IsEnabled = theme;
        _frameBorderShadowEnabled.IsEnabled = enabled;
        _frameBorderShadowColor.IsEnabled = enabled && _frameBorderShadowEnabled.IsChecked == true;
        _frameBorderShadowAlpha.IsEnabled = _frameBorderShadowColor.IsEnabled;
        _frameBorderShadowBlur.IsEnabled = _frameBorderShadowColor.IsEnabled;
        _frameBorderShadowDistance.IsEnabled = _frameBorderShadowColor.IsEnabled;
        _frameBorderShadowDirection.IsEnabled = _frameBorderShadowColor.IsEnabled;
        _frameBorderGlowEnabled.IsEnabled = enabled;
        _frameBorderGlowColor.IsEnabled = enabled && _frameBorderGlowEnabled.IsChecked == true;
        _frameBorderGlowAlpha.IsEnabled = _frameBorderGlowColor.IsEnabled;
        _frameBorderGlowRadius.IsEnabled = _frameBorderGlowColor.IsEnabled;
        _frameBorderSoftEdgeEnabled.IsEnabled = enabled;
        _frameBorderSoftEdgeRadius.IsEnabled = enabled && _frameBorderSoftEdgeEnabled.IsChecked == true;
    }
}
