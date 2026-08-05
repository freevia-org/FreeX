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
    private readonly CheckBox _returnToParent;
    private readonly CheckBox _showBackground;
    private readonly CheckBox _transitionEnabled;
    private readonly CheckBox _frameBorderEnabled;
    private readonly CheckBox _frameBorderGradientEnabled;
    private readonly CheckBox _frameBorderPatternEnabled;
    private readonly CheckBox _frameBorderNoFillEnabled;
    private readonly CheckBox _frameBorderThemeEnabled;
    private readonly ComboBox _imageType;
    private readonly TextBox _transitionDuration;
    private readonly TextBox _frameBorderColor;
    private readonly ComboBox _frameBorderThemeColor;
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
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ZoomDialogChrome.Apply(this);

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
        _transitionEnabled.IsCheckedChanged += (_, _) => SyncTransitionState();
        _frameBorderColor = new TextBox
        {
            Text = fields.FrameBorderColor,
            MinWidth = 180,
            PlaceholderText = "six-digit RGB value",
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
            PlaceholderText = "positive width in points",
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
        _frameBorderEnabled.IsCheckedChanged += (_, _) => SyncFrameBorderState();
        _frameBorderGradientEnabled = new CheckBox
        {
            Content = "Use gradient border",
            IsChecked = fields.FrameBorderGradientEnabled,
        };
        _frameBorderGradientStart = new TextBox
        {
            Text = fields.FrameBorderGradientStart,
            MinWidth = 180,
            PlaceholderText = "start RGB value",
        };
        _frameBorderGradientEnd = new TextBox
        {
            Text = fields.FrameBorderGradientEnd,
            MinWidth = 180,
            PlaceholderText = "end RGB value",
        };
        _frameBorderGradientAngle = new TextBox
        {
            Text = fields.FrameBorderGradientAngle,
            MinWidth = 180,
            PlaceholderText = "angle 0-360 degrees",
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
            PlaceholderText = "foreground RGB value",
        };
        _frameBorderPatternBackground = new TextBox
        {
            Text = fields.FrameBorderPatternBackground,
            MinWidth = 180,
            PlaceholderText = "background RGB value",
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
        _frameBorderGradientEnabled.IsCheckedChanged += (_, _) =>
            OnBorderModeChanged(_frameBorderGradientEnabled, ZoomObjectPropertiesBorderMode.Gradient);
        _frameBorderPatternEnabled.IsCheckedChanged += (_, _) =>
            OnBorderModeChanged(_frameBorderPatternEnabled, ZoomObjectPropertiesBorderMode.Pattern);
        _frameBorderNoFillEnabled.IsCheckedChanged += (_, _) =>
            OnBorderModeChanged(_frameBorderNoFillEnabled, ZoomObjectPropertiesBorderMode.NoFill);
        _frameBorderThemeEnabled.IsCheckedChanged += (_, _) =>
            OnBorderModeChanged(_frameBorderThemeEnabled, ZoomObjectPropertiesBorderMode.Theme);
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
            PlaceholderText = "left, top, right, bottom",
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

        var ok = ZoomDialogChrome.MakeButton("OK", true, Apply);
        var children = new List<Control>
        {
            Row("Image source:", _imageType),
            _transitionEnabled,
            Row("Transition duration:", _transitionDuration),
            _frameBorderEnabled,
            Row("Border color:", _frameBorderColor),
            _frameBorderThemeEnabled,
            Row("Theme color:", _frameBorderThemeColor),
            Row("Border width (pt):", _frameBorderWidth),
            Row("Border dash:", _frameBorderDash),
            _frameBorderGradientEnabled,
            Row("Gradient start:", _frameBorderGradientStart),
            Row("Gradient end:", _frameBorderGradientEnd),
            Row("Gradient angle (deg):", _frameBorderGradientAngle),
            _frameBorderPatternEnabled,
            Row("Pattern preset:", _frameBorderPatternPreset),
            Row("Pattern foreground:", _frameBorderPatternForeground),
            Row("Pattern background:", _frameBorderPatternBackground),
            _frameBorderNoFillEnabled,
            Row("Frame shape:", _frameGeometry),
            Row("Preview crop (%):", _cropEdges),
        };
        if (_summaryTile is not null && _summaryOffset is not null && _summaryScale is not null)
        {
            children.Add(Row("Summary tile:", _summaryTile));
            children.Add(Row("Tile position (%):", _summaryOffset));
            children.Add(Row("Tile scale (%):", _summaryScale));
            children.Add(_applySummaryPropertiesToAllTiles!);
        }
        children.Add(_returnToParent);
        children.Add(_showBackground);
        children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { ok, ZoomDialogChrome.MakeButton("Cancel", false, () => Close(false)) },
        });
        var content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
        };
        foreach (var child in children)
            content.Children.Add(child);
        Content = content;
        SyncTransitionState();
        SyncFrameBorderState();
        LoadSummaryTileFields();
    }

    private static StackPanel Row(string label, Control control) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            new TextBlock { Text = label, Width = 160, VerticalAlignment = VerticalAlignment.Center },
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
                ZoomObjectPropertiesPlanner.DialogTitle);
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
            _frameBorderThemeEnabled.IsChecked == true);
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
    }
}
