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
    private readonly ComboBox _imageType;
    private readonly TextBox _transitionDuration;
    private readonly TextBox _cropEdges;
    private readonly TextBlock _validation;

    internal ZoomObjectProperties Properties { get; private set; }

    internal ZoomObjectPropertiesDialog(ZoomObjectProperties current)
    {
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
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                Row("Image source:", _imageType),
                Row("Transition duration:", _transitionDuration),
                Row("Preview crop (%):", _cropEdges),
                _returnToParent,
                _showBackground,
                _validation,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { ok, MakeButton("Cancel", false, () => Close(false)) },
                },
            },
        };
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
        if (!ZoomObjectPropertiesPlanner.TryParseCropEdges(
                _cropEdges.Text, out var cropLeft, out var cropTop, out var cropRight, out var cropBottom))
        {
            _validation.Text = "Crop edges must be four percentages: left, top, right, bottom.";
            return;
        }
        _validation.Text = string.Empty;
        Properties = new ZoomObjectProperties(
            _returnToParent.IsChecked == true,
            _imageType.SelectedItem as string ?? "preview",
            string.IsNullOrWhiteSpace(_transitionDuration.Text) ? null : _transitionDuration.Text.Trim(),
            _showBackground.IsChecked == true,
            cropLeft,
            cropTop,
            cropRight,
            cropBottom);
        Close(true);
    }

    private static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }
}
