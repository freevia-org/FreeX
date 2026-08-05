using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ZoomObjectPropertiesDialog : Window
{
    private readonly ZoomObjectPropertiesDialogSession _session;
    private readonly ZoomObjectPropertiesDialogSurfacePlan _surface;
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
        _surface = ZoomObjectPropertiesDialogSurfacePlanner.BuildSurfacePlan();
        var fields = _session.InitialFields;
        var layout = _surface.Layout;
        var text = _surface.Text;
        Title = _surface.Chrome.Title;
        Width = _surface.Chrome.Width;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ZoomDialogChrome.Apply(this);

        _imageType = new ComboBox
        {
            ItemsSource = _surface.ImageTypeOptions,
            SelectedItem = fields.ImageType,
            MinWidth = layout.InputMinWidth,
        };
        _transitionDuration = new TextBox
        {
            Text = fields.TransitionDuration,
            MinWidth = layout.InputMinWidth,
        };
        _transitionEnabled = new CheckBox
        {
            Content = text.UseZoomTransitionLabel,
            IsChecked = fields.TransitionEnabled,
        };
        _transitionEnabled.IsCheckedChanged += (_, _) => SyncTransitionState();
        _frameBorderColor = new TextBox
        {
            Text = fields.FrameBorderColor,
            MinWidth = layout.InputMinWidth,
            PlaceholderText = "six-digit RGB value",
        };
        _frameBorderThemeColor = new ComboBox
        {
            ItemsSource = ZoomObjectPropertiesPlanner.FrameBorderThemeColorOptions,
            SelectedItem = fields.FrameBorderThemeColor,
            MinWidth = layout.InputMinWidth,
        };
        _frameBorderWidth = new TextBox
        {
            Text = fields.FrameBorderWidth,
            MinWidth = layout.InputMinWidth,
            PlaceholderText = "positive width in points",
        };
        _frameBorderDash = new ComboBox
        {
            ItemsSource = ZoomObjectPropertiesPlanner.FrameBorderDashOptions,
            SelectedItem = fields.FrameBorderDash,
            MinWidth = layout.InputMinWidth,
        };
        _frameBorderEnabled = new CheckBox
        {
            Content = text.UseZoomBorderLabel,
            IsChecked = fields.FrameBorderEnabled,
        };
        _frameBorderEnabled.IsCheckedChanged += (_, _) => SyncFrameBorderState();
        _frameBorderGradientEnabled = new CheckBox
        {
            Content = text.UseGradientBorderLabel,
            IsChecked = fields.FrameBorderGradientEnabled,
        };
        _frameBorderGradientStart = new TextBox
        {
            Text = fields.FrameBorderGradientStart,
            MinWidth = layout.InputMinWidth,
            PlaceholderText = "start RGB value",
        };
        _frameBorderGradientEnd = new TextBox
        {
            Text = fields.FrameBorderGradientEnd,
            MinWidth = layout.InputMinWidth,
            PlaceholderText = "end RGB value",
        };
        _frameBorderGradientAngle = new TextBox
        {
            Text = fields.FrameBorderGradientAngle,
            MinWidth = layout.InputMinWidth,
            PlaceholderText = "angle 0-360 degrees",
        };
        _frameBorderPatternEnabled = new CheckBox
        {
            Content = text.UsePatternBorderLabel,
            IsChecked = fields.FrameBorderPatternEnabled,
        };
        _frameBorderPatternPreset = new ComboBox
        {
            ItemsSource = ZoomObjectPropertiesPlanner.FrameBorderPatternOptions,
            SelectedItem = fields.FrameBorderPatternPreset,
            MinWidth = layout.InputMinWidth,
        };
        _frameBorderPatternForeground = new TextBox
        {
            Text = fields.FrameBorderPatternForeground,
            MinWidth = layout.InputMinWidth,
            PlaceholderText = "foreground RGB value",
        };
        _frameBorderPatternBackground = new TextBox
        {
            Text = fields.FrameBorderPatternBackground,
            MinWidth = layout.InputMinWidth,
            PlaceholderText = "background RGB value",
        };
        _frameBorderNoFillEnabled = new CheckBox
        {
            Content = text.UseNoFillBorderLabel,
            IsChecked = fields.FrameBorderNoFillEnabled,
        };
        _frameBorderThemeEnabled = new CheckBox
        {
            Content = text.UseThemeBorderColorLabel,
            IsChecked = fields.FrameBorderThemeEnabled,
        };
        _frameBorderGradientEnabled.IsCheckedChanged += (_, _) =>
            OnBorderModeChanged(_frameBorderGradientEnabled, ZoomObjectPropertiesBorderMode.Gradient);
        _frameBorderPatternEnabled.IsCheckedChanged += (_, _) =>
            OnBorderModeChanged(_frameBorderPatternEnabled, ZoomObjectPropertiesBorderMode.Pattern);
        _frameBorderNoFillEnabled.IsCheckedChanged += (_, _) =>
            OnBorderModeChanged(_frameBorderNoFillEnabled, ZoomObjectPropertiesBorderMode.NoFill);
        _frameBorderThemeEnabled.IsCheckedChanged += (_, _) =>
            OnBorderModeChanged(_frameBorderThemeEnabled, ZoomObjectPropertiesBorderMode.Theme);
        _frameBorderShadowEnabled = new CheckBox
        {
            Content = text.UseOuterBorderShadowLabel,
            IsChecked = fields.FrameBorderShadowEnabled,
        };
        _frameBorderShadowColor = new TextBox
        {
            Text = fields.FrameBorderShadowColor,
            MinWidth = layout.InputMinWidth,
            PlaceholderText = "six-digit RGB value",
        };
        _frameBorderShadowAlpha = new TextBox
        {
            Text = fields.FrameBorderShadowAlpha, MinWidth = layout.InputMinWidth,
        };
        _frameBorderShadowBlur = new TextBox
        {
            Text = fields.FrameBorderShadowBlur, MinWidth = layout.InputMinWidth,
        };
        _frameBorderShadowDistance = new TextBox
        {
            Text = fields.FrameBorderShadowDistance, MinWidth = layout.InputMinWidth,
        };
        _frameBorderShadowDirection = new TextBox
        {
            Text = fields.FrameBorderShadowDirection, MinWidth = layout.InputMinWidth,
        };
        _frameBorderShadowEnabled.IsCheckedChanged += (_, _) => SyncFrameBorderState();
        _frameBorderGlowEnabled = new CheckBox
        {
            Content = text.UseBorderGlowLabel,
            IsChecked = fields.FrameBorderGlowEnabled,
        };
        _frameBorderGlowColor = new TextBox
        {
            Text = fields.FrameBorderGlowColor,
            MinWidth = layout.InputMinWidth,
            PlaceholderText = "six-digit RGB value",
        };
        _frameBorderGlowAlpha = new TextBox
        {
            Text = fields.FrameBorderGlowAlpha, MinWidth = layout.InputMinWidth,
        };
        _frameBorderGlowRadius = new TextBox
        {
            Text = fields.FrameBorderGlowRadius, MinWidth = layout.InputMinWidth,
        };
        _frameBorderGlowEnabled.IsCheckedChanged += (_, _) => SyncFrameBorderState();
        _frameBorderSoftEdgeEnabled = new CheckBox
        {
            Content = text.UseBorderSoftEdgeLabel,
            IsChecked = fields.FrameBorderSoftEdgeEnabled,
        };
        _frameBorderSoftEdgeRadius = new TextBox
        {
            Text = fields.FrameBorderSoftEdgeRadius, MinWidth = layout.InputMinWidth,
        };
        _frameBorderSoftEdgeEnabled.IsCheckedChanged += (_, _) => SyncFrameBorderState();
        _frameGeometry = new ComboBox
        {
            ItemsSource = ZoomObjectPropertiesPlanner.FrameGeometryOptions,
            SelectedItem = fields.FrameGeometry,
            MinWidth = layout.InputMinWidth,
        };
        _cropEdges = new TextBox
        {
            Text = fields.CropEdges,
            MinWidth = layout.InputMinWidth,
            PlaceholderText = "left, top, right, bottom",
        };
        if (_session.HasSummaryTargets)
        {
            _summaryTile = new ComboBox
            {
                ItemsSource = _session.SummaryTargetOptions,
                SelectedIndex = 0,
                MinWidth = layout.InputMinWidth,
            };
            _summaryOffset = new TextBox { MinWidth = layout.InputMinWidth };
            _summaryScale = new TextBox { MinWidth = layout.InputMinWidth };
            _applySummaryPropertiesToAllTiles = new CheckBox
            {
                Content = text.ApplyToAllSummaryTilesLabel,
                Margin = new Thickness(0, 4, 0, 0),
            };
            _summaryTile.SelectionChanged += (_, _) => LoadSummaryTileFields();
        }
        _returnToParent = new CheckBox
        {
            Content = text.ReturnToParentLabel,
            IsChecked = fields.ReturnToParent,
        };
        _showBackground = new CheckBox
        {
            Content = text.ShowBackgroundLabel,
            IsChecked = fields.ShowBackground,
        };

        var ok = ZoomDialogChrome.MakeButton(_surface.Chrome.AcceptLabel, true, Apply);
        var children = new List<Control>
        {
            Row(text.ImageSourceLabel, _imageType, layout.LabelWidth),
            _transitionEnabled,
            Row(text.TransitionDurationLabel, _transitionDuration, layout.LabelWidth),
            _frameBorderEnabled,
            Row(text.BorderColorLabel, _frameBorderColor, layout.LabelWidth),
            _frameBorderThemeEnabled,
            Row(text.ThemeColorLabel, _frameBorderThemeColor, layout.LabelWidth),
            _frameBorderShadowEnabled,
            Row(text.ShadowColorLabel, _frameBorderShadowColor, layout.LabelWidth),
            Row(text.ShadowAlphaLabel, _frameBorderShadowAlpha, layout.LabelWidth),
            Row(text.ShadowBlurLabel, _frameBorderShadowBlur, layout.LabelWidth),
            Row(text.ShadowDistanceLabel, _frameBorderShadowDistance, layout.LabelWidth),
            Row(text.ShadowDirectionLabel, _frameBorderShadowDirection, layout.LabelWidth),
            _frameBorderGlowEnabled,
            Row(text.GlowColorLabel, _frameBorderGlowColor, layout.LabelWidth),
            Row(text.GlowAlphaLabel, _frameBorderGlowAlpha, layout.LabelWidth),
            Row(text.GlowRadiusLabel, _frameBorderGlowRadius, layout.LabelWidth),
            _frameBorderSoftEdgeEnabled,
            Row(text.SoftEdgeRadiusLabel, _frameBorderSoftEdgeRadius, layout.LabelWidth),
            Row(text.BorderWidthLabel, _frameBorderWidth, layout.LabelWidth),
            Row(text.BorderDashLabel, _frameBorderDash, layout.LabelWidth),
            _frameBorderGradientEnabled,
            Row(text.GradientStartLabel, _frameBorderGradientStart, layout.LabelWidth),
            Row(text.GradientEndLabel, _frameBorderGradientEnd, layout.LabelWidth),
            Row(text.GradientAngleLabel, _frameBorderGradientAngle, layout.LabelWidth),
            _frameBorderPatternEnabled,
            Row(text.PatternPresetLabel, _frameBorderPatternPreset, layout.LabelWidth),
            Row(text.PatternForegroundLabel, _frameBorderPatternForeground, layout.LabelWidth),
            Row(text.PatternBackgroundLabel, _frameBorderPatternBackground, layout.LabelWidth),
            _frameBorderNoFillEnabled,
            Row(text.FrameShapeLabel, _frameGeometry, layout.LabelWidth),
            Row(text.PreviewCropLabel, _cropEdges, layout.LabelWidth),
        };
        if (_summaryTile is not null && _summaryOffset is not null && _summaryScale is not null)
        {
            children.Add(Row(text.SummaryTileLabel, _summaryTile, layout.LabelWidth));
            children.Add(Row(text.TilePositionLabel, _summaryOffset, layout.LabelWidth));
            children.Add(Row(text.TileScaleLabel, _summaryScale, layout.LabelWidth));
            children.Add(_applySummaryPropertiesToAllTiles!);
        }
        children.Add(_returnToParent);
        children.Add(_showBackground);
        children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { ok, ZoomDialogChrome.MakeButton(_surface.Chrome.CancelLabel, false, () => Close(false)) },
        });
        var content = new StackPanel
        {
            Margin = new Thickness(layout.ContentMargin),
            Spacing = 8,
        };
        foreach (var child in children)
            content.Children.Add(child);
        Content = content;
        SyncTransitionState();
        SyncFrameBorderState();
        LoadSummaryTileFields();
    }

    private static StackPanel Row(string label, Control control, double labelWidth) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            new TextBlock { Text = label, Width = labelWidth, VerticalAlignment = VerticalAlignment.Center },
            control,
        },
    };

    private async void Apply()
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
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                validation!.Message,
                _surface.Chrome.Title);
            return;
        }

        Close(true);
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

    private void OnBorderModeChanged(CheckBox source, ZoomObjectPropertiesBorderMode mode)
    {
        if (source.IsChecked == true)
            ApplyExclusiveBorderMode(mode);
        else
            SyncFrameBorderState();
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
