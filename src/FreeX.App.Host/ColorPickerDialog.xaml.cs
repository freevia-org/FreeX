using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record ColorPickerSwatch(string Hex, CellColor Color);
public sealed record ColorPickerThemeColumn(string Name, IReadOnlyList<ColorPickerSwatch> Shades);

public partial class ColorPickerDialog : Window
{
    private bool _updatingText;
    private bool _updatingSlider;
    private readonly CellColor? _currentColor;
    private CellColor? _customSpectrumBaseColor;
    private Button? _initialFocusButton;
    private Button? _selectedSwatchButton;

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
        {
            _customSpectrumBaseColor = color;
            SetCustomColorText(color);
        }
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public CellColor? SelectedColor { get; private set; }

    public bool AllowNoColor { get; }

    public static IReadOnlyList<ColorPickerSwatch> BuildDefaultSwatches() =>
        ColorPickerPalettePlanner.BuildDefaultSwatches();

    public static IReadOnlyList<ColorPickerThemeColumn> BuildThemePalette() =>
        ColorPickerPalettePlanner.BuildThemePalette();

    public static IReadOnlyList<ColorPickerSwatch> BuildStandardSwatches() =>
        ColorPickerPalettePlanner.BuildStandardSwatches();

    public static IReadOnlyList<ColorPickerSwatch> BuildCustomSpectrumSwatches() =>
        ColorPickerPalettePlanner.BuildCustomSpectrumSwatches();

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
        SelectColor(ColorPickerPalettePlanner.ScaleColor(baseColor, factor), updateSpectrumBase: false);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseColorText(CustomColorTextBox.Text, out var color))
        {
            DialogMessageHelper.ShowWarning(this, "Enter a color as #RRGGBB or R, G, B.", Title);
            FocusInvalidCustomColorInput();
            return;
        }

        SelectedColor = color;
        SetPreview(NewForegroundPreview, NewBackgroundPreview, NewBackgroundText, color);
        DialogResult = true;
    }

    private void FocusInvalidCustomColorInput()
    {
        ColorTabs.SelectedItem = CustomTab;
        DialogFocus.FocusAndSelect(CustomColorTextBox);
    }

    private void NoColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowNoColor)
            return;

        SelectedColor = null;
        DialogResult = true;
    }

    private void BuildPaletteButtons()
    {
        var themeColumns = BuildThemePalette();
        for (var row = 0; row < themeColumns[0].Shades.Count; row++)
        {
            foreach (var column in themeColumns)
                ThemeColorsPanel.Children.Add(CreateSwatchButton(column.Shades[row], column.Name));
        }

        foreach (var swatch in BuildStandardSwatches())
            StandardColorsPanel.Children.Add(CreateSwatchButton(swatch, "Standard color"));

        foreach (var swatch in BuildCustomSpectrumSwatches())
            CustomSpectrumPanel.Children.Add(CreateSwatchButton(swatch, "Custom spectrum color"));
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
            BorderThickness = new Thickness(1),
            ToolTip = groupName is null ? swatch.Hex : $"{groupName} {swatch.Hex}",
            Tag = swatch.Color
        };
        AutomationProperties.SetName(button, CreateSwatchAutomationName(swatch, groupName));
        AutomationProperties.SetHelpText(button, "Select this color swatch.");
        button.Click += SwatchButton_Click;
        _initialFocusButton ??= button;
        if (SelectedColor == swatch.Color)
            MarkSelectedSwatch(button);
        return button;
    }

    private static string CreateSwatchAutomationName(ColorPickerSwatch swatch, string? groupName) =>
        groupName is null ? $"Color swatch {swatch.Hex}" : $"{groupName} swatch {swatch.Hex}";

    private void FocusInitialKeyboardTarget()
    {
        _initialFocusButton?.Focus();
        if (_initialFocusButton is not null)
            Keyboard.Focus(_initialFocusButton);
    }

    private void SwatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CellColor color } button)
        {
            MarkSelectedSwatch(button);
            SelectColor(
                color,
                updateSpectrumBase: ReferenceEquals(((Button)sender).Parent, CustomSpectrumPanel),
                updateSwatchSelection: false);
        }
    }

    private void MarkSelectedSwatch(Button button)
    {
        if (_selectedSwatchButton is not null)
        {
            _selectedSwatchButton.BorderBrush = Brushes.Gray;
            _selectedSwatchButton.BorderThickness = new Thickness(1);
        }

        button.BorderBrush = Brushes.Black;
        button.BorderThickness = new Thickness(2);
        _selectedSwatchButton = button;
    }

    private void ClearSelectedSwatch()
    {
        if (_selectedSwatchButton is null)
            return;

        _selectedSwatchButton.BorderBrush = Brushes.Gray;
        _selectedSwatchButton.BorderThickness = new Thickness(1);
        _selectedSwatchButton = null;
    }

    private void UpdateSwatchSelection(CellColor color)
    {
        var matchingButton = ThemeColorsPanel.Children
            .OfType<Button>()
            .Concat(StandardColorsPanel.Children.OfType<Button>())
            .Concat(CustomSpectrumPanel.Children.OfType<Button>())
            .FirstOrDefault(button => button.Tag is CellColor swatchColor && swatchColor == color);

        if (matchingButton is null)
        {
            ClearSelectedSwatch();
            return;
        }

        MarkSelectedSwatch(matchingButton);
    }

    private void SelectColor(CellColor color, bool updateSpectrumBase = true, bool updateSwatchSelection = true)
    {
        SelectedColor = color;
        if (updateSwatchSelection)
            UpdateSwatchSelection(color);

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

    private static Brush GetReadableBrush(CellColor color)
    {
        return ColorPickerPalettePlanner.NeedsDarkForeground(color) ? Brushes.Black : Brushes.White;
    }
}
