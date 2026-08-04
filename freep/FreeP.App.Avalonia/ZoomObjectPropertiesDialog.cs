using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
    private readonly ComboBox _imageType;
    private readonly TextBox _transitionDuration;
    private readonly TextBox _frameBorderColor;
    private readonly TextBox _cropEdges;
    private readonly TextBlock _validation;
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
        _frameBorderEnabled = new CheckBox
        {
            Content = "Use Zoom border",
            IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderEnabled(current),
        };
        _frameBorderEnabled.IsCheckedChanged += (_, _) => SyncFrameBorderState();
        _cropEdges = new TextBox
        {
            Text = ZoomObjectPropertiesPlanner.FormatCropEdges(current),
            MinWidth = 180,
            PlaceholderText = "left, top, right, bottom",
        };
        _validation = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
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
            Row("Preview crop (%):", _cropEdges),
        };
        if (_summaryTile is not null && _summaryOffset is not null && _summaryScale is not null)
        {
            children.Add(Row("Summary tile:", _summaryTile));
            children.Add(Row("Tile position (%):", _summaryOffset));
            children.Add(Row("Tile scale (%):", _summaryScale));
        }
        children.Add(_returnToParent);
        children.Add(_showBackground);
        children.Add(_validation);
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

    private void Apply()
    {
        if (!ZoomObjectPropertiesPlanner.TryParseTransitionDuration(
                _transitionDuration.Text,
                _transitionEnabled.IsChecked == true,
                out var transitionDuration))
        {
            _validation.Text = "Transition duration must be a positive whole number of milliseconds.";
            return;
        }
        if (!ZoomObjectPropertiesPlanner.TryParseFrameBorderColor(
                _frameBorderColor.Text,
                _frameBorderEnabled.IsChecked == true,
                out var frameBorderColor))
        {
            _validation.Text = "Border color must be a six-digit RGB value.";
            return;
        }
        if (!ZoomObjectPropertiesPlanner.TryParseCropEdges(
                _cropEdges.Text, out var cropLeft, out var cropTop, out var cropRight, out var cropBottom))
        {
            _validation.Text = "Crop edges must be four percentages: left, top, right, bottom.";
            return;
        }
        _validation.Text = string.Empty;
        if (_summaryTile is not null && _summaryOffset is not null && _summaryScale is not null)
        {
            if (!ZoomObjectPropertiesPlanner.TryParseFactorPair(
                    _summaryOffset.Text, allowNegative: true, out var offsetX, out var offsetY)
                || !ZoomObjectPropertiesPlanner.TryParseFactorPair(
                    _summaryScale.Text, allowNegative: false, out var scaleX, out var scaleY))
            {
                _validation.Text = "Summary tile position and scale must each be two percentages.";
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
            frameBorderColor);
        if (_summaryTile is not null && _summaryTile.SelectedIndex >= 0
            && _summaryTile.SelectedIndex < _summaryTargets.Count)
        {
            SummaryTileProperties = new ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit(
                _summaryTargets[_summaryTile.SelectedIndex].SectionId,
                Properties);
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
        _frameBorderEnabled.IsChecked = ZoomObjectPropertiesPlanner.IsFrameBorderEnabled(properties);
        SyncFrameBorderState();
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

    private void SyncFrameBorderState() =>
        _frameBorderColor.IsEnabled = _frameBorderEnabled.IsChecked == true;

    private static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }
}
