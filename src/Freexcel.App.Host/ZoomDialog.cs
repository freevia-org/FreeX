using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public sealed record ZoomDialogResult(int ZoomPercent, bool FitSelection = false);

public sealed class ZoomDialog : Window
{
    private static readonly int[] ZoomPresets = [400, 200, 100, 75, 50, 25];
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
        Height = 240;
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
        var stack = new StackPanel { Margin = new Thickness(12) };
        var group = new GroupBox
        {
            Header = "Magnification",
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var choices = new Grid();
        choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var presets = new StackPanel();
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
            presets.Children.Add(button);
        }

        choices.Children.Add(presets);
        var customChoices = new StackPanel();
        _customZoomButton.IsChecked = !ZoomPresets.Contains(currentZoomPercent);
        _fitSelectionButton.Margin = new Thickness(0, 0, 0, 10);
        customChoices.Children.Add(_fitSelectionButton);
        var customRow = new StackPanel { Orientation = Orientation.Horizontal };
        customRow.Children.Add(_customZoomButton);
        _zoomBox.Width = 72;
        _zoomBox.Height = 24;
        customRow.Children.Add(_zoomBox);
        customRow.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
        customChoices.Children.Add(customRow);
        Grid.SetColumn(customChoices, 1);
        choices.Children.Add(customChoices);
        group.Content = choices;
        stack.Children.Add(group);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
    }
}
