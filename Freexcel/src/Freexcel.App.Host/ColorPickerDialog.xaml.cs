using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record ColorPickerSwatch(string Hex, CellColor Color);

public partial class ColorPickerDialog : Window
{
    public ColorPickerDialog(CellColor? initialColor = null, bool allowNoColor = false)
    {
        InitializeComponent();

        AllowNoColor = allowNoColor;
        SelectedColor = initialColor;
        NoColorButton.Visibility = allowNoColor ? Visibility.Visible : Visibility.Collapsed;
        SwatchList.ItemsSource = BuildDefaultSwatches();

        if (initialColor is { } color)
            CustomColorTextBox.Text = ColorInputParser.FormatHexColor(color);
    }

    public CellColor? SelectedColor { get; private set; }

    public bool AllowNoColor { get; }

    public static IReadOnlyList<ColorPickerSwatch> BuildDefaultSwatches() =>
        new[]
        {
            Swatch("#000000"),
            Swatch("#FFFFFF"),
            Swatch("#C00000"),
            Swatch("#FF0000"),
            Swatch("#FFC000"),
            Swatch("#FFFF00"),
            Swatch("#92D050"),
            Swatch("#00B050"),
            Swatch("#00B0F0"),
            Swatch("#0070C0"),
            Swatch("#002060"),
            Swatch("#7030A0"),
            Swatch("#217346"),
            Swatch("#D9EAD3"),
            Swatch("#FCE4D6"),
            Swatch("#D9EAF7")
        };

    public static bool TryParseColorText(string text, out CellColor color)
    {
        return ColorInputParser.TryParseColorText(text, out color);
    }

    private void SwatchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SwatchList.SelectedItem is not ColorPickerSwatch swatch)
            return;

        SelectedColor = swatch.Color;
        CustomColorTextBox.Text = swatch.Hex;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseColorText(CustomColorTextBox.Text, out var color))
        {
            MessageBox.Show(
                this,
                "Enter a color as #RRGGBB or R, G, B.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SelectedColor = color;
        DialogResult = true;
    }

    private void NoColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowNoColor)
            return;

        SelectedColor = null;
        DialogResult = true;
    }

    private static ColorPickerSwatch Swatch(string hex)
    {
        if (!TryParseColorText(hex, out var color))
            throw new InvalidOperationException($"Invalid swatch color '{hex}'.");

        return new ColorPickerSwatch(hex, color);
    }

}
