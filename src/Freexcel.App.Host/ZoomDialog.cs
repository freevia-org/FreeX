using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public sealed record ZoomDialogResult(int ZoomPercent, bool FitSelection = false);

public sealed class ZoomDialog : Window
{
    private static readonly int[] ZoomPresets = [200, 100, 75, 50, 25];
    private readonly TextBox _zoomBox = new();
    private readonly RadioButton _customZoomButton = new() { Content = "_Custom:", GroupName = "Zoom", IsChecked = true };
    private readonly RadioButton _fitSelectionButton = new() { Content = "Fit _selection", GroupName = "Zoom" };
    private readonly List<RadioButton> _presetButtons = [];

    public ZoomDialogResult Result { get; private set; }

    public ZoomDialog(int currentZoomPercent)
    {
        Result = new ZoomDialogResult(currentZoomPercent);
        Title = "Zoom";
        Width = 300;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _zoomBox.Text = currentZoomPercent.ToString(CultureInfo.InvariantCulture);
        Content = CreateZoomContent(currentZoomPercent);
    }

    public static bool TryCreateResult(string? input, out ZoomDialogResult result, out string? error)
    {
        result = new ZoomDialogResult(100);
        error = null;
        if (!Freexcel.App.UI.ZoomLevelMapper.TryParseZoomPercent(input, out var zoomPercent))
        {
            error = "Zoom must be between 10% and 400%.";
            return false;
        }

        result = new ZoomDialogResult((int)Math.Round(zoomPercent));
        return true;
    }

    public static ZoomDialogResult CreateFitSelectionResult(int currentZoomPercent) =>
        new(currentZoomPercent, FitSelection: true);

    private void Accept()
    {
        if (_fitSelectionButton.IsChecked == true)
        {
            Result = CreateFitSelectionResult(Result.ZoomPercent);
            DialogResult = true;
            return;
        }

        var selectedPreset = _presetButtons
            .Where(button => button.IsChecked == true)
            .Select(button => button.Tag?.ToString())
            .FirstOrDefault();
        var input = selectedPreset ?? _zoomBox.Text;
        if (!TryCreateResult(input, out var result, out _))
            return;
        Result = result;
        DialogResult = true;
    }

    private UIElement CreateZoomContent(int currentZoomPercent)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Magnification", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var preset in ZoomPresets)
        {
            var button = new RadioButton
            {
                Content = $"{preset}%",
                GroupName = "Zoom",
                Tag = preset,
                IsChecked = preset == currentZoomPercent,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _presetButtons.Add(button);
            stack.Children.Add(button);
        }

        _customZoomButton.IsChecked = !ZoomPresets.Contains(currentZoomPercent);
        _fitSelectionButton.Margin = new Thickness(0, 2, 0, 4);
        stack.Children.Add(_fitSelectionButton);
        var customRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 16) };
        customRow.Children.Add(_customZoomButton);
        _zoomBox.Width = 72;
        _zoomBox.Height = 24;
        customRow.Children.Add(_zoomBox);
        customRow.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
        stack.Children.Add(customRow);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
    }
}
