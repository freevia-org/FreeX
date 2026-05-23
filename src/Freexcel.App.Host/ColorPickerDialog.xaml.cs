using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record ColorPickerSwatch(string Hex, CellColor Color);
public sealed record ColorPickerThemeColumn(string Name, IReadOnlyList<ColorPickerSwatch> Shades);

public partial class ColorPickerDialog : Window
{
    private bool _updatingText;
    private bool _updatingSlider;
    private readonly CellColor? _currentColor;
    private CellColor? _customSpectrumBaseColor;

    public ColorPickerDialog(CellColor? initialColor = null, bool allowNoColor = false)
    {
        InitializeComponent();

        AllowNoColor = allowNoColor;
        _currentColor = initialColor;
        SelectedColor = initialColor;
        NoColorButton.Visibility = allowNoColor ? Visibility.Visible : Visibility.Collapsed;
        BuildPaletteButtons();

        SetPreview(CurrentForegroundPreview, CurrentBackgroundPreview, CurrentBackgroundText, _currentColor);
        SetPreview(NewForegroundPreview, NewBackgroundPreview, NewBackgroundText, initialColor);
        if (initialColor is { } color)
            SetCustomColorText(color);
    }

    public CellColor? SelectedColor { get; private set; }

    public bool AllowNoColor { get; }

    public static IReadOnlyList<ColorPickerSwatch> BuildDefaultSwatches() =>
        BuildThemePalette().SelectMany(column => column.Shades)
            .Concat(BuildStandardSwatches())
            .DistinctBy(swatch => swatch.Hex)
            .ToList();

    public static IReadOnlyList<ColorPickerThemeColumn> BuildThemePalette() =>
        new[]
        {
            Column("Text/Background Dark 1", "#000000", "#7F7F7F", "#595959", "#3F3F3F", "#262626", "#0D0D0D"),
            Column("Text/Background Light 1", "#FFFFFF", "#F2F2F2", "#D9D9D9", "#BFBFBF", "#A6A6A6", "#808080"),
            Column("Text/Background Dark 2", "#44546A", "#D6DCE4", "#ADB9CA", "#8497B0", "#323E4F", "#222A35"),
            Column("Text/Background Light 2", "#E7E6E6", "#D0CECE", "#AEAAAA", "#757171", "#3A3838", "#171616"),
            Column("Accent 1", "#4472C4", "#D9E2F3", "#B4C6E7", "#8EAADB", "#2F5597", "#1F3864"),
            Column("Accent 2", "#ED7D31", "#FCE4D6", "#F8CBAD", "#F4B183", "#C55A11", "#833C0C"),
            Column("Accent 3", "#A5A5A5", "#EDEDED", "#DBDBDB", "#C9C9C9", "#7B7B7B", "#525252"),
            Column("Accent 4", "#FFC000", "#FFF2CC", "#FFE699", "#FFD966", "#BF9000", "#7F6000"),
            Column("Accent 5", "#5B9BD5", "#DDEBF7", "#BDD7EE", "#9DC3E6", "#2E75B6", "#1F4E79"),
            Column("Accent 6", "#70AD47", "#E2F0D9", "#C6E0B4", "#A9D18E", "#548235", "#375623")
        };

    public static IReadOnlyList<ColorPickerSwatch> BuildStandardSwatches() =>
        new[]
        {
            Swatch("#C00000"),
            Swatch("#FF0000"),
            Swatch("#FFC000"),
            Swatch("#FFFF00"),
            Swatch("#92D050"),
            Swatch("#00B050"),
            Swatch("#00B0F0"),
            Swatch("#0070C0"),
            Swatch("#002060"),
            Swatch("#7030A0")
        };

    public static IReadOnlyList<ColorPickerSwatch> BuildCustomSpectrumSwatches()
    {
        var hues = new[] { 0d, 30d, 60d, 120d, 180d, 210d, 240d, 300d };
        var saturations = new[] { 1d, 0.85d, 0.7d, 0.55d, 0.4d, 0.25d };

        return saturations
            .SelectMany(saturation => hues.Select(hue => SwatchFromHsv(hue, saturation, 1d)))
            .DistinctBy(swatch => swatch.Hex)
            .ToList();
    }

    public static bool TryParseColorText(string text, out CellColor color)
    {
        return ColorInputParser.TryParseColorText(text, out color);
    }

    private void CustomColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingText || !TryParseColorText(CustomColorTextBox.Text, out var color))
            return;

        SelectColor(color);
    }

    private void CustomRgbTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingText
            || !byte.TryParse(CustomRedTextBox.Text, out var red)
            || !byte.TryParse(CustomGreenTextBox.Text, out var green)
            || !byte.TryParse(CustomBlueTextBox.Text, out var blue))
        {
            return;
        }

        SelectColor(new CellColor(red, green, blue));
    }

    private void CustomLuminositySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSlider || _customSpectrumBaseColor is not { } baseColor)
            return;

        var factor = CustomLuminositySlider.Value / 100d;
        SelectColor(new CellColor(
            ScaleColorComponent(baseColor.R, factor),
            ScaleColorComponent(baseColor.G, factor),
            ScaleColorComponent(baseColor.B, factor)),
            updateSpectrumBase: false);
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
        SetPreview(NewForegroundPreview, NewBackgroundPreview, NewBackgroundText, color);
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

    private static ColorPickerThemeColumn Column(string name, params string[] shades) =>
        new(name, shades.Select(Swatch).ToList());

    private void BuildPaletteButtons()
    {
        var themeColumns = BuildThemePalette();
        for (var row = 0; row < themeColumns[0].Shades.Count; row++)
        {
            foreach (var column in themeColumns)
                ThemeColorsPanel.Children.Add(CreateSwatchButton(column.Shades[row], column.Name));
        }

        foreach (var swatch in BuildStandardSwatches())
            StandardColorsPanel.Children.Add(CreateSwatchButton(swatch));

        foreach (var swatch in BuildCustomSpectrumSwatches())
            CustomSpectrumPanel.Children.Add(CreateSwatchButton(swatch));
    }

    private Button CreateSwatchButton(ColorPickerSwatch swatch, string? groupName = null)
    {
        var button = new Button
        {
            Width = 30,
            Height = 24,
            Margin = new Thickness(2),
            Padding = new Thickness(0),
            Background = ToBrush(swatch.Color),
            BorderBrush = Brushes.Gray,
            ToolTip = groupName is null ? swatch.Hex : $"{groupName} {swatch.Hex}",
            Tag = swatch.Color
        };
        button.Click += SwatchButton_Click;
        return button;
    }

    private void SwatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CellColor color })
            SelectColor(color, updateSpectrumBase: ReferenceEquals(((Button)sender).Parent, CustomSpectrumPanel));
    }

    private void SelectColor(CellColor color, bool updateSpectrumBase = true)
    {
        SelectedColor = color;
        if (updateSpectrumBase)
        {
            _customSpectrumBaseColor = color;
            _updatingSlider = true;
            CustomLuminositySlider.Value = 100;
            _updatingSlider = false;
        }

        SetPreview(NewForegroundPreview, NewBackgroundPreview, NewBackgroundText, color);
        SetCustomColorText(color);
    }

    private void SetCustomColorText(CellColor color)
    {
        _updatingText = true;
        CustomColorTextBox.Text = ColorInputParser.FormatHexColor(color);
        CustomRedTextBox.Text = color.R.ToString();
        CustomGreenTextBox.Text = color.G.ToString();
        CustomBlueTextBox.Text = color.B.ToString();
        _updatingText = false;
    }

    private static void SetPreview(TextBlock foregroundPreview, Border backgroundPreview, TextBlock backgroundText, CellColor? color)
    {
        if (color is not { } selected)
        {
            foregroundPreview.Foreground = SystemColors.GrayTextBrush;
            backgroundPreview.Background = Brushes.Transparent;
            backgroundText.Foreground = SystemColors.ControlTextBrush;
            return;
        }

        foregroundPreview.Foreground = ToBrush(selected);
        backgroundPreview.Background = ToBrush(selected);
        backgroundText.Foreground = GetReadableBrush(selected);
    }

    private static SolidColorBrush ToBrush(CellColor color) =>
        new(Color.FromRgb(color.R, color.G, color.B));

    private static ColorPickerSwatch SwatchFromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var huePrime = hue / 60d;
        var x = chroma * (1d - Math.Abs((huePrime % 2d) - 1d));
        var match = value - chroma;

        var (red, green, blue) = huePrime switch
        {
            >= 0 and < 1 => (chroma, x, 0d),
            >= 1 and < 2 => (x, chroma, 0d),
            >= 2 and < 3 => (0d, chroma, x),
            >= 3 and < 4 => (0d, x, chroma),
            >= 4 and < 5 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        var color = new CellColor(
            ToByte(red + match),
            ToByte(green + match),
            ToByte(blue + match));

        return new ColorPickerSwatch(ColorInputParser.FormatHexColor(color), color);
    }

    private static byte ScaleColorComponent(byte component, double factor) =>
        (byte)Math.Clamp((int)Math.Round(component * factor), 0, 255);

    private static byte ToByte(double value) =>
        (byte)Math.Clamp((int)Math.Round(value * 255d), 0, 255);

    private static Brush GetReadableBrush(CellColor color)
    {
        var luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
        return luminance > 140 ? Brushes.Black : Brushes.White;
    }
}
