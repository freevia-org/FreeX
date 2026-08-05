using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

internal sealed class ZoomObjectPropertiesDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ZoomObjectPropertiesDialogSession _session;
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
    private readonly ComboBox? _summaryTile;
    private readonly TextBox? _summaryOffset;
    private readonly TextBox? _summaryScale;
    private readonly CheckBox? _applySummaryPropertiesToAllTiles;

    internal ZoomObjectProperties Properties => _session.Result.Properties;
    internal ZoomObjectPropertiesPlanner.SummaryZoomTileLayoutEdit? SummaryTileLayout =>
        _session.Result.SummaryTileLayout;
    internal ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit? SummaryTileProperties =>
        _session.Result.SummaryTileProperties;
    internal bool ApplySummaryPropertiesToAllTiles =>
        _session.Result.ApplySummaryPropertiesToAllTiles;

    internal ZoomObjectPropertiesDialog(
        ZoomObjectProperties current,
        IReadOnlyList<SummaryZoomTarget>? summaryTargets = null,
        IReadOnlyList<ZoomObjectProperties>? summaryTileProperties = null)
    {
        _session = new ZoomObjectPropertiesDialogSession(current, summaryTargets, summaryTileProperties);
        var fields = _session.InitialFields;
        Title = ZoomObjectPropertiesPlanner.DialogTitle;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _returnToParent = new CheckBox
        {
            Content = "Return to parent after following the Zoom",
            IsChecked = fields.ReturnToParent,
        };
        _showBackground = new CheckBox
        {
            Content = "Show destination slide background",
            IsChecked = fields.ShowBackground,
        };
        _imageType = new ComboBox
        {
            ItemsSource = new[] { "preview", "cover" },
            SelectedItem = fields.ImageType,
            MinWidth = 180,
        };
        _transitionDuration = new TextBox
        {
            Text = fields.TransitionDuration,
            MinWidth = 180,
        };
        _transitionEnabled = new CheckBox
        {
            Content = "Use Zoom transition",
            IsChecked = fields.TransitionEnabled,
        };
        _transitionEnabled.Checked += (_, _) => SyncTransitionState();
        _transitionEnabled.Unchecked += (_, _) => SyncTransitionState();
        _frameBorderColor = new TextBox
        {
            Text = fields.FrameBorderColor,
            MinWidth = 180,
            ToolTip = "six-digit RGB value; for example 4472C4",
        };
        _frameBorderThemeColor = new ComboBox
        {
            ItemsSource = ZoomObjectPropertiesPlanner.FrameBorderThemeColorOptions,
            SelectedItem = fields.FrameBorderThemeColor,
            MinWidth = 180,
        };
        _frameBorderWidth = new TextBox
        {
            Text = fields.FrameBorderWidth,
            MinWidth = 180,
            ToolTip = "positive width in points; for example 1.5",
        };
        _frameBorderDash = new ComboBox
        {
            ItemsSource = ZoomObjectPropertiesPlanner.FrameBorderDashOptions,
            SelectedItem = fields.FrameBorderDash,
            MinWidth = 180,
        };
        _frameBorderEnabled = new CheckBox
        {
            Content = "Use Zoom border",
            IsChecked = fields.FrameBorderEnabled,
        };
        _frameBorderEnabled.Checked += (_, _) => SyncFrameBorderState();
        _frameBorderEnabled.Unchecked += (_, _) => SyncFrameBorderState();
        _frameBorderGradientEnabled = new CheckBox
        {
            Content = "Use gradient border",
            IsChecked = fields.FrameBorderGradientEnabled,
        };
        _frameBorderGradientStart = new TextBox
        {
            Text = fields.FrameBorderGradientStart,
            MinWidth = 180,
            ToolTip = "six-digit RGB value; for example 4472C4",
        };
        _frameBorderGradientEnd = new TextBox
        {
            Text = fields.FrameBorderGradientEnd,
            MinWidth = 180,
            ToolTip = "six-digit RGB value; for example FFFFFF",
        };
        _frameBorderGradientAngle = new TextBox
        {
            Text = fields.FrameBorderGradientAngle,
            MinWidth = 180,
            ToolTip = "linear angle in degrees from 0 to 360",
        };
        _frameBorderPatternEnabled = new CheckBox
        {
            Content = "Use pattern border",
            IsChecked = fields.FrameBorderPatternEnabled,
        };
        _frameBorderPatternPreset = new ComboBox
        {
            ItemsSource = ZoomObjectPropertiesPlanner.FrameBorderPatternOptions,
            SelectedItem = fields.FrameBorderPatternPreset,
            MinWidth = 180,
        };
        _frameBorderPatternForeground = new TextBox
        {
            Text = fields.FrameBorderPatternForeground,
            MinWidth = 180,
            ToolTip = "six-digit RGB value; for example 4472C4",
        };
        _frameBorderPatternBackground = new TextBox
        {
            Text = fields.FrameBorderPatternBackground,
            MinWidth = 180,
            ToolTip = "six-digit RGB value; for example FFFFFF",
        };
        _frameBorderNoFillEnabled = new CheckBox
        {
            Content = "Use no-fill border",
            IsChecked = fields.FrameBorderNoFillEnabled,
        };
        _frameBorderThemeEnabled = new CheckBox
        {
            Content = "Use theme border color",
            IsChecked = fields.FrameBorderThemeEnabled,
        };
        _frameBorderGradientEnabled.Checked += (_, _) =>
            ApplyExclusiveBorderMode(ZoomObjectPropertiesBorderMode.Gradient);
        _frameBorderPatternEnabled.Checked += (_, _) =>
            ApplyExclusiveBorderMode(ZoomObjectPropertiesBorderMode.Pattern);
        _frameBorderNoFillEnabled.Checked += (_, _) =>
            ApplyExclusiveBorderMode(ZoomObjectPropertiesBorderMode.NoFill);
        _frameBorderThemeEnabled.Checked += (_, _) =>
            ApplyExclusiveBorderMode(ZoomObjectPropertiesBorderMode.Theme);
        _frameBorderGradientEnabled.Unchecked += (_, _) => SyncFrameBorderState();
        _frameBorderPatternEnabled.Unchecked += (_, _) => SyncFrameBorderState();
        _frameBorderNoFillEnabled.Unchecked += (_, _) => SyncFrameBorderState();
        _frameBorderThemeEnabled.Unchecked += (_, _) => SyncFrameBorderState();
        _frameBorderShadowEnabled = new CheckBox
        {
            Content = "Use outer border shadow",
            IsChecked = fields.FrameBorderShadowEnabled,
        };
        _frameBorderShadowColor = new TextBox
        {
            Text = fields.FrameBorderShadowColor,
            MinWidth = 180,
            ToolTip = "six-digit RGB value; for example 404040",
        };
        _frameBorderShadowAlpha = new TextBox
        {
            Text = fields.FrameBorderShadowAlpha, MinWidth = 180,
        };
        _frameBorderShadowBlur = new TextBox
        {
            Text = fields.FrameBorderShadowBlur, MinWidth = 180,
        };
        _frameBorderShadowDistance = new TextBox
        {
            Text = fields.FrameBorderShadowDistance, MinWidth = 180,
        };
        _frameBorderShadowDirection = new TextBox
        {
            Text = fields.FrameBorderShadowDirection, MinWidth = 180,
        };
        _frameBorderShadowEnabled.Checked += (_, _) => SyncFrameBorderState();
        _frameBorderShadowEnabled.Unchecked += (_, _) => SyncFrameBorderState();
        _frameBorderGlowEnabled = new CheckBox
        {
            Content = "Use border glow",
            IsChecked = fields.FrameBorderGlowEnabled,
        };
        _frameBorderGlowColor = new TextBox
        {
            Text = fields.FrameBorderGlowColor,
            MinWidth = 180,
            ToolTip = "six-digit RGB value; for example 4472C4",
        };
        _frameBorderGlowAlpha = new TextBox
        {
            Text = fields.FrameBorderGlowAlpha, MinWidth = 180,
        };
        _frameBorderGlowRadius = new TextBox
        {
            Text = fields.FrameBorderGlowRadius, MinWidth = 180,
        };
        _frameBorderGlowEnabled.Checked += (_, _) => SyncFrameBorderState();
        _frameBorderGlowEnabled.Unchecked += (_, _) => SyncFrameBorderState();
        _frameBorderSoftEdgeEnabled = new CheckBox
        {
            Content = "Use border soft edge",
            IsChecked = fields.FrameBorderSoftEdgeEnabled,
        };
        _frameBorderSoftEdgeRadius = new TextBox
        {
            Text = fields.FrameBorderSoftEdgeRadius, MinWidth = 180,
        };
        _frameBorderSoftEdgeEnabled.Checked += (_, _) => SyncFrameBorderState();
        _frameBorderSoftEdgeEnabled.Unchecked += (_, _) => SyncFrameBorderState();
        _frameBorderGradientEnabled.Checked += (_, _) => _frameBorderThemeEnabled.IsChecked = false;
        _frameBorderPatternEnabled.Checked += (_, _) => _frameBorderThemeEnabled.IsChecked = false;
        _frameBorderNoFillEnabled.Checked += (_, _) => _frameBorderThemeEnabled.IsChecked = false;
        _frameGeometry = new ComboBox
        {
            ItemsSource = ZoomObjectPropertiesPlanner.FrameGeometryOptions,
            SelectedItem = fields.FrameGeometry,
            MinWidth = 180,
        };
        _cropEdges = new TextBox
        {
            Text = fields.CropEdges,
            MinWidth = 180,
            ToolTip = "left, top, right, bottom as percentages; for example 0, 5, 0, 5",
        };

        if (_session.HasSummaryTargets)
        {
            _summaryTile = new ComboBox
            {
                ItemsSource = _session.SummaryTargetOptions,
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
        for (var i = 0; i < 35 + (_session.HasSummaryTargets ? 4 : 0); i++)
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
        var input = new ZoomObjectPropertiesDialogInput(
            ReturnToParent: _returnToParent.IsChecked == true,
            ShowBackground: _showBackground.IsChecked == true,
            ImageType: _imageType.SelectedItem as string,
            TransitionEnabled: _transitionEnabled.IsChecked == true,
            TransitionDuration: _transitionDuration.Text,
            FrameBorderEnabled: _frameBorderEnabled.IsChecked == true,
            FrameBorderColor: _frameBorderColor.Text,
            FrameBorderThemeColor: _frameBorderThemeColor.SelectedItem is ThemeColorSlot slot
                ? slot
                : null,
            FrameBorderThemeEnabled: _frameBorderThemeEnabled.IsChecked == true,
            FrameBorderWidth: _frameBorderWidth.Text,
            FrameBorderDash: _frameBorderDash.SelectedItem?.ToString(),
            FrameBorderGradientEnabled: _frameBorderGradientEnabled.IsChecked == true,
            FrameBorderGradientStart: _frameBorderGradientStart.Text,
            FrameBorderGradientEnd: _frameBorderGradientEnd.Text,
            FrameBorderGradientAngle: _frameBorderGradientAngle.Text,
            FrameBorderPatternEnabled: _frameBorderPatternEnabled.IsChecked == true,
            FrameBorderPatternPreset: _frameBorderPatternPreset.SelectedItem?.ToString(),
            FrameBorderPatternForeground: _frameBorderPatternForeground.Text,
            FrameBorderPatternBackground: _frameBorderPatternBackground.Text,
            FrameBorderNoFillEnabled: _frameBorderNoFillEnabled.IsChecked == true,
            FrameBorderShadowEnabled: _frameBorderShadowEnabled.IsChecked == true,
            FrameBorderShadowColor: _frameBorderShadowColor.Text,
            FrameBorderShadowAlpha: _frameBorderShadowAlpha.Text,
            FrameBorderShadowBlur: _frameBorderShadowBlur.Text,
            FrameBorderShadowDistance: _frameBorderShadowDistance.Text,
            FrameBorderShadowDirection: _frameBorderShadowDirection.Text,
            FrameBorderGlowEnabled: _frameBorderGlowEnabled.IsChecked == true,
            FrameBorderGlowColor: _frameBorderGlowColor.Text,
            FrameBorderGlowAlpha: _frameBorderGlowAlpha.Text,
            FrameBorderGlowRadius: _frameBorderGlowRadius.Text,
            FrameBorderSoftEdgeEnabled: _frameBorderSoftEdgeEnabled.IsChecked == true,
            FrameBorderSoftEdgeRadius: _frameBorderSoftEdgeRadius.Text,
            FrameGeometry: _frameGeometry.SelectedItem?.ToString(),
            CropEdges: _cropEdges.Text,
            SummaryTileIndex: _summaryTile?.SelectedIndex ?? -1,
            SummaryOffset: _summaryOffset?.Text,
            SummaryScale: _summaryScale?.Text,
            ApplySummaryPropertiesToAllTiles: _applySummaryPropertiesToAllTiles?.IsChecked == true);
        if (!_session.TryAccept(input, out var validation))
        {
            MessageBox.Show(
                this,
                validation!.Message,
                ZoomObjectPropertiesPlanner.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void LoadSummaryTileFields()
    {
        if (_summaryTile is null || _summaryOffset is null || _summaryScale is null
            || !_session.TryBuildSummaryTileFields(_summaryTile.SelectedIndex, out var fields))
            return;

        _imageType.SelectedItem = fields!.ImageType;
        _transitionDuration.Text = fields.TransitionDuration;
        _transitionEnabled.IsChecked = fields.TransitionEnabled;
        SyncTransitionState();
        _frameBorderColor.Text = fields.FrameBorderColor;
        _frameBorderWidth.Text = fields.FrameBorderWidth;
        _frameBorderDash.SelectedItem = fields.FrameBorderDash;
        _frameBorderEnabled.IsChecked = fields.FrameBorderEnabled;
        _frameBorderGradientEnabled.IsChecked = fields.FrameBorderGradientEnabled;
        _frameBorderGradientStart.Text = fields.FrameBorderGradientStart;
        _frameBorderGradientEnd.Text = fields.FrameBorderGradientEnd;
        _frameBorderGradientAngle.Text = fields.FrameBorderGradientAngle;
        _frameBorderPatternEnabled.IsChecked = fields.FrameBorderPatternEnabled;
        _frameBorderPatternPreset.SelectedItem = fields.FrameBorderPatternPreset;
        _frameBorderPatternForeground.Text = fields.FrameBorderPatternForeground;
        _frameBorderPatternBackground.Text = fields.FrameBorderPatternBackground;
        _frameBorderNoFillEnabled.IsChecked = fields.FrameBorderNoFillEnabled;
        _frameBorderThemeEnabled.IsChecked = fields.FrameBorderThemeEnabled;
        _frameBorderThemeColor.SelectedItem = fields.FrameBorderThemeColor;
        _frameBorderShadowEnabled.IsChecked = fields.FrameBorderShadowEnabled;
        _frameBorderShadowColor.Text = fields.FrameBorderShadowColor;
        _frameBorderShadowAlpha.Text = fields.FrameBorderShadowAlpha;
        _frameBorderShadowBlur.Text = fields.FrameBorderShadowBlur;
        _frameBorderShadowDistance.Text = fields.FrameBorderShadowDistance;
        _frameBorderShadowDirection.Text = fields.FrameBorderShadowDirection;
        _frameBorderGlowEnabled.IsChecked = fields.FrameBorderGlowEnabled;
        _frameBorderGlowColor.Text = fields.FrameBorderGlowColor;
        _frameBorderGlowAlpha.Text = fields.FrameBorderGlowAlpha;
        _frameBorderGlowRadius.Text = fields.FrameBorderGlowRadius;
        _frameBorderSoftEdgeEnabled.IsChecked = fields.FrameBorderSoftEdgeEnabled;
        _frameBorderSoftEdgeRadius.Text = fields.FrameBorderSoftEdgeRadius;
        SyncFrameBorderState();
        _frameGeometry.SelectedItem = fields.FrameGeometry;
        _cropEdges.Text = fields.CropEdges;
        _returnToParent.IsChecked = fields.ReturnToParent;
        _showBackground.IsChecked = fields.ShowBackground;
        _summaryOffset.Text = fields.SummaryOffset;
        _summaryScale.Text = fields.SummaryScale;
    }

    private void ApplyExclusiveBorderMode(ZoomObjectPropertiesBorderMode mode)
    {
        var plan = ZoomObjectPropertiesDialogSession.SelectExclusiveBorderMode(mode);
        _frameBorderGradientEnabled.IsChecked = plan.GradientEnabled;
        _frameBorderPatternEnabled.IsChecked = plan.PatternEnabled;
        _frameBorderNoFillEnabled.IsChecked = plan.NoFillEnabled;
        _frameBorderThemeEnabled.IsChecked = plan.ThemeEnabled;
        SyncFrameBorderState();
    }

    private void SyncTransitionState() => ApplyEnablement();

    private void SyncFrameBorderState() => ApplyEnablement();

    private void ApplyEnablement()
    {
        var enablement = ZoomObjectPropertiesDialogSession.BuildEnablement(
            _transitionEnabled.IsChecked == true,
            _frameBorderEnabled.IsChecked == true,
            _frameBorderGradientEnabled.IsChecked == true,
            _frameBorderPatternEnabled.IsChecked == true,
            _frameBorderNoFillEnabled.IsChecked == true,
            _frameBorderThemeEnabled.IsChecked == true,
            _frameBorderShadowEnabled.IsChecked == true,
            _frameBorderGlowEnabled.IsChecked == true,
            _frameBorderSoftEdgeEnabled.IsChecked == true);
        _transitionDuration.IsEnabled = enablement.TransitionDuration;
        _frameBorderColor.IsEnabled = enablement.FrameBorderColor;
        _frameBorderWidth.IsEnabled = enablement.FrameBorderWidth;
        _frameBorderDash.IsEnabled = enablement.FrameBorderDash;
        _frameBorderGradientEnabled.IsEnabled = enablement.FrameBorderGradientToggle;
        _frameBorderGradientStart.IsEnabled = enablement.FrameBorderGradientFields;
        _frameBorderGradientEnd.IsEnabled = enablement.FrameBorderGradientFields;
        _frameBorderGradientAngle.IsEnabled = enablement.FrameBorderGradientFields;
        _frameBorderPatternEnabled.IsEnabled = enablement.FrameBorderPatternToggle;
        _frameBorderPatternPreset.IsEnabled = enablement.FrameBorderPatternFields;
        _frameBorderPatternForeground.IsEnabled = enablement.FrameBorderPatternFields;
        _frameBorderPatternBackground.IsEnabled = enablement.FrameBorderPatternFields;
        _frameBorderNoFillEnabled.IsEnabled = enablement.FrameBorderNoFillToggle;
        _frameBorderThemeEnabled.IsEnabled = enablement.FrameBorderThemeToggle;
        _frameBorderThemeColor.IsEnabled = enablement.FrameBorderThemeColor;
        _frameBorderShadowEnabled.IsEnabled = enablement.FrameBorderShadowToggle;
        _frameBorderShadowColor.IsEnabled = enablement.FrameBorderShadowFields;
        _frameBorderShadowAlpha.IsEnabled = enablement.FrameBorderShadowFields;
        _frameBorderShadowBlur.IsEnabled = enablement.FrameBorderShadowFields;
        _frameBorderShadowDistance.IsEnabled = enablement.FrameBorderShadowFields;
        _frameBorderShadowDirection.IsEnabled = enablement.FrameBorderShadowFields;
        _frameBorderGlowEnabled.IsEnabled = enablement.FrameBorderGlowToggle;
        _frameBorderGlowColor.IsEnabled = enablement.FrameBorderGlowFields;
        _frameBorderGlowAlpha.IsEnabled = enablement.FrameBorderGlowFields;
        _frameBorderGlowRadius.IsEnabled = enablement.FrameBorderGlowFields;
        _frameBorderSoftEdgeEnabled.IsEnabled = enablement.FrameBorderSoftEdgeToggle;
        _frameBorderSoftEdgeRadius.IsEnabled = enablement.FrameBorderSoftEdgeFields;
    }
}
