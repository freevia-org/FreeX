using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ZoomObjectPropertiesDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(global::Avalonia.Media.FontFamily.Default);
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
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AvaloniaCompactDialogChrome.ApplyWindow(this, DialogChromeStyle);

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
        _transitionEnabled.IsCheckedChanged += (_, _) => SyncTransitionState();
        _frameBorderColor = new TextBox
        {
            Text = current.FrameBorderColor ?? string.Empty,
            MinWidth = 180,
            PlaceholderText = "six-digit RGB value",
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
            PlaceholderText = "positive width in points",
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
        _frameBorderEnabled.IsCheckedChanged += (_, _) => SyncFrameBorderState();
        _frameBorderGradientEnabled = new CheckBox
        {
            Content = "Use gradient border",
            IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderGradientEnabled(current),
        };
        _frameBorderGradientEnabled.IsCheckedChanged += (_, _) => SyncFrameBorderState();
        _frameBorderGradientStart = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderGradientStart(current),
            MinWidth = 180,
            PlaceholderText = "start RGB value",
        };
        _frameBorderGradientEnd = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderGradientEnd(current),
            MinWidth = 180,
            PlaceholderText = "end RGB value",
        };
        _frameBorderGradientAngle = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderGradientAngle(current),
            MinWidth = 180,
            PlaceholderText = "angle 0-360 degrees",
        };
        _frameBorderPatternEnabled = new CheckBox
        {
            Content = "Use pattern border",
            IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderPatternEnabled(current),
        };
        _frameBorderPatternEnabled.IsCheckedChanged += (_, _) =>
        {
            if (_frameBorderPatternEnabled.IsChecked == true)
                _frameBorderGradientEnabled.IsChecked = false;
            SyncFrameBorderState();
        };
        _frameBorderGradientEnabled.IsCheckedChanged += (_, _) =>
        {
            if (_frameBorderGradientEnabled.IsChecked == true)
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
            PlaceholderText = "foreground RGB value",
        };
        _frameBorderPatternBackground = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatFrameBorderPatternBackground(current),
            MinWidth = 180,
            PlaceholderText = "background RGB value",
        };
        _frameBorderNoFillEnabled = new CheckBox
        {
            Content = "Use no-fill border",
            IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderNoFillEnabled(current),
        };
        _frameBorderNoFillEnabled.IsCheckedChanged += (_, _) =>
        {
            if (_frameBorderNoFillEnabled.IsChecked == true)
            {
                _frameBorderGradientEnabled.IsChecked = false;
                _frameBorderPatternEnabled.IsChecked = false;
            }
            SyncFrameBorderState();
        };
        _frameBorderGradientEnabled.IsCheckedChanged += (_, _) =>
        {
            if (_frameBorderGradientEnabled.IsChecked == true)
            {
                _frameBorderNoFillEnabled.IsChecked = false;
            }
        };
        _frameBorderPatternEnabled.IsCheckedChanged += (_, _) =>
        {
            if (_frameBorderPatternEnabled.IsChecked == true)
            {
                _frameBorderNoFillEnabled.IsChecked = false;
            }
        };
        _frameBorderThemeEnabled = new CheckBox
        {
            Content = "Use theme border color",
            IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderThemeColorEnabled(current),
        };
        _frameBorderThemeEnabled.IsCheckedChanged += (_, _) =>
        {
            if (_frameBorderThemeEnabled.IsChecked == true)
            {
                _frameBorderGradientEnabled.IsChecked = false;
                _frameBorderPatternEnabled.IsChecked = false;
                _frameBorderNoFillEnabled.IsChecked = false;
            }
            SyncFrameBorderState();
        };
        _frameBorderGradientEnabled.IsCheckedChanged += (_, _) =>
        {
            if (_frameBorderGradientEnabled.IsChecked == true)
                _frameBorderThemeEnabled.IsChecked = false;
        };
        _frameBorderPatternEnabled.IsCheckedChanged += (_, _) =>
        {
            if (_frameBorderPatternEnabled.IsChecked == true)
                _frameBorderThemeEnabled.IsChecked = false;
        };
        _frameBorderNoFillEnabled.IsCheckedChanged += (_, _) =>
        {
            if (_frameBorderNoFillEnabled.IsChecked == true)
                _frameBorderThemeEnabled.IsChecked = false;
        };
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
            PlaceholderText = "left, top, right, bottom",
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

        Properties = current;
        var ok = MakeButton("OK", true, Apply);
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
            Children = { ok, MakeButton("Cancel", false, () => Close(false)) },
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
        if (!ZoomObjectPropertiesPlanner.TryParseTransitionDuration(
                _transitionDuration.Text,
                _transitionEnabled.IsChecked == true,
                out var transitionDuration))
        {
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                ZoomObjectPropertiesPlanner.InvalidTransitionDurationMessage,
                ZoomObjectPropertiesPlanner.DialogTitle);
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
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderColorMessage,
                ZoomObjectPropertiesPlanner.DialogTitle);
            return;
        }
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderWidth(
                _frameBorderWidth.Text,
                _frameBorderEnabled.IsChecked == true,
                out var frameBorderWidth))
        {
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderWidthMessage,
                ZoomObjectPropertiesPlanner.DialogTitle);
            return;
        }
        var frameBorderDashText = _frameBorderEnabled.IsChecked == true
            ? _frameBorderDash.SelectedItem?.ToString()
            : null;
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderDash(
                frameBorderDashText,
                out var frameBorderDash))
        {
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderDashMessage,
                ZoomObjectPropertiesPlanner.DialogTitle);
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
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderGradientMessage,
                ZoomObjectPropertiesPlanner.DialogTitle);
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
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                ZoomObjectPropertiesPlanner.InvalidFrameBorderPatternMessage,
                ZoomObjectPropertiesPlanner.DialogTitle);
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
        if (!ZoomObjectPropertiesPlanner.TryParseFrameGeometry(
                _frameGeometry.SelectedItem?.ToString(), out var frameGeometry))
        {
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                ZoomObjectPropertiesPlanner.InvalidFrameGeometryMessage,
                ZoomObjectPropertiesPlanner.DialogTitle);
            return;
        }
        if (!ZoomObjectPropertiesPlanner.TryParseCropEdges(
                _cropEdges.Text, out var cropLeft, out var cropTop, out var cropRight, out var cropBottom))
        {
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                ZoomObjectPropertiesPlanner.InvalidCropEdgesMessage,
                ZoomObjectPropertiesPlanner.DialogTitle);
            return;
        }
        if (_summaryTile is not null && _summaryOffset is not null && _summaryScale is not null)
        {
            if (!ZoomObjectPropertiesPlanner.TryParseFactorPair(
                    _summaryOffset.Text, allowNegative: true, out var offsetX, out var offsetY)
                || !ZoomObjectPropertiesPlanner.TryParseFactorPair(
                    _summaryScale.Text, allowNegative: false, out var scaleX, out var scaleY))
            {
                await AvaloniaUserMessageDialog.ShowWarningAsync(
                    this,
                    ZoomObjectPropertiesPlanner.InvalidSummaryTileLayoutMessage,
                    ZoomObjectPropertiesPlanner.DialogTitle);
                return;
            }

            var target = _summaryTargets[_summaryTile.SelectedIndex];
            SummaryTileLayout = new ZoomObjectPropertiesPlanner.SummaryZoomTileLayoutEdit(
                target.SectionId, offsetX, offsetY, scaleX, scaleY);
        }

        Properties = new ZoomObjectProperties(
            _returnToParent.IsChecked == true,
            _imageType.SelectedItem as string ?? "preview",
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
            themeColor);
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
        Close(true);
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
    }

    private static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }
}
