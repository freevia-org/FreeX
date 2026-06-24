using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FreeW.App.Host;

/// <summary>
/// Dialog for Picture Format > Adjust: brightness (-100..100), contrast (-100..100),
/// saturation (0..400, 100=normal), and transparency (0..100). Returns null on cancel.
/// </summary>
internal sealed class ImageAdjustDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _brightnessBox, _contrastBox, _saturationBox, _transparencyBox;
    private (double Brightness, double Contrast, double Saturation, double Transparency)? _result;

    private ImageAdjustDialog(Window? owner,
        double brightnessPct, double contrastPct, double saturationPct, double transparencyPct)
    {
        Owner = owner;
        Title = "Picture Corrections and Color";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _brightnessBox   = Box(brightnessPct);
        _contrastBox     = Box(contrastPct);
        _saturationBox   = Box(saturationPct);
        _transparencyBox = Box(transparencyPct);

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 7; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        static void Place(Grid g, UIElement el, int row, int col)
        { Grid.SetRow(el, row); Grid.SetColumn(el, col); g.Children.Add(el); }

        Place(grid, Label("Corrections"), 0, 0);
        Place(grid, new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) }, 0, 1);

        Place(grid, Label("Brightness (-100 to +100):"), 1, 0); Place(grid, _brightnessBox,   1, 1);
        Place(grid, Label("Contrast (-100 to +100):"),   2, 0); Place(grid, _contrastBox,     2, 1);

        var sep = new Separator { Margin = new Thickness(0, 6, 0, 4) };
        Grid.SetRow(sep, 3); Grid.SetColumnSpan(sep, 2); grid.Children.Add(sep);

        Place(grid, Label("Color"), 3, 0);

        Place(grid, Label("Saturation (0–400, 100=normal):"), 4, 0); Place(grid, _saturationBox,   4, 1);
        Place(grid, Label("Transparency (0–100):"),           5, 0); Place(grid, _transparencyBox, 5, 1);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Place(grid, buttons, 6, 1);

        Content = grid;
        DialogFocus.FocusAndSelect(_brightnessBox);
    }

    private static TextBox Box(double value) =>
        new() { Text = value.ToString("0.##", CultureInfo.CurrentCulture), MinWidth = 80 };

    private static TextBlock Label(string text) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 0) };

    private void Accept()
    {
        if (!TryParseDouble(_brightnessBox.Text, -100, 100, out var brightness, "Brightness"))   return;
        if (!TryParseDouble(_contrastBox.Text,   -100, 100, out var contrast,   "Contrast"))     return;
        if (!TryParseDouble(_saturationBox.Text,    0, 400, out var saturation, "Saturation"))   return;
        if (!TryParseDouble(_transparencyBox.Text,  0, 100, out var transparency,"Transparency")) return;

        _result = (brightness, contrast, saturation, transparency);
        Close();
    }

    private bool TryParseDouble(string text, double min, double max, out double value, string label)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || value < min || value > max)
        {
            DialogMessageHelper.ShowWarning(this, $"{label} must be a number between {min} and {max}.");
            value = 0;
            return false;
        }
        return true;
    }

    /// <summary>Show the dialog. Returns (brightness, contrast, saturation, transparency), or null if cancelled.</summary>
    public static (double Brightness, double Contrast, double Saturation, double Transparency)? Prompt(
        Window? owner, double brightnessPct, double contrastPct, double saturationPct, double transparencyPct)
    {
        var dialog = new ImageAdjustDialog(owner, brightnessPct, contrastPct, saturationPct, transparencyPct);
        dialog.ShowDialog();
        return dialog._result;
    }
}
