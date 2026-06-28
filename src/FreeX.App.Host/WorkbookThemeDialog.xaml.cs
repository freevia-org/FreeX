using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using FreeX.App.Presentation.ThemeUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public enum WorkbookThemeDialogMode
{
    Theme,
    Colors,
    Effects
}

public partial class WorkbookThemeDialog : Window
{
    private readonly WorkbookTheme _initialTheme;
    private readonly WorkbookThemeDialogMode _mode;

    public WorkbookThemeDialog(WorkbookTheme theme)
        : this(theme, WorkbookThemeDialogMode.Theme)
    {
    }

    public WorkbookThemeDialog(WorkbookTheme theme, WorkbookThemeDialogMode mode)
    {
        ArgumentNullException.ThrowIfNull(theme);

        _initialTheme = theme;
        _mode = mode;
        InitializeComponent();
        ApplyThemeColorAutomationMetadata();
        PopulateOptions();
        WirePreviewRefresh();
        ApplyDialogMode();
        LoadTheme(theme);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public WorkbookTheme ResultTheme { get; private set; } = WorkbookTheme.Office;

    private void PopulateOptions()
    {
        var fonts = new[] { "Aptos Display", "Aptos", "Calibri", "Arial", "Times New Roman", "Segoe UI", "Verdana" };
        HeadingFontBox.ItemsSource = fonts;
        BodyFontBox.ItemsSource = fonts;
        EffectsBox.ItemsSource = new[] { "Office", "Subtle", "Refined" };
    }

    private void ApplyDialogMode()
    {
        if (_mode == WorkbookThemeDialogMode.Theme)
            return;

        if (_mode == WorkbookThemeDialogMode.Colors)
        {
            Title = UiText.Get("WorkbookTheme_ThemeColors");
            Height = 600;
            ThemeDialogTitle.Text = UiText.Get("WorkbookTheme_ThemeColors");
            ThemeMetadataPanel.Visibility = Visibility.Collapsed;
            return;
        }

        Title = UiText.Get("WorkbookTheme_Effects2");
        Height = 360;
        ThemeDialogTitle.Text = UiText.Get("WorkbookTheme_Effects2");
        ColorfulPresetButton.Content = UiText.Get("MainWindow_Header_Subtle");
        ColorfulPresetButton.Width = 92;
        GrayscalePresetButton.Content = UiText.Get("MainWindow_Header_Refined");
        GrayscalePresetButton.Width = 92;

        ThemeNameLabel.Visibility = Visibility.Collapsed;
        ThemeNameBox.Visibility = Visibility.Collapsed;
        HeadingFontLabel.Visibility = Visibility.Collapsed;
        HeadingFontBox.Visibility = Visibility.Collapsed;
        BodyFontLabel.Visibility = Visibility.Collapsed;
        BodyFontBox.Visibility = Visibility.Collapsed;
        ThemeColorsTitle.Visibility = Visibility.Collapsed;
        ThemeColorsPanel.Visibility = Visibility.Collapsed;

        Grid.SetColumn(EffectsLabel, 0);
        Grid.SetColumn(EffectsBox, 1);
        Grid.SetColumnSpan(EffectsBox, 3);
        EffectsBox.Margin = new Thickness(0, 0, 0, 8);
    }

    private void WirePreviewRefresh()
    {
        HeadingFontBox.SelectionChanged += (_, _) => UpdatePreview();
        BodyFontBox.SelectionChanged += (_, _) => UpdatePreview();
        EffectsBox.SelectionChanged += (_, _) => UpdatePreview();
        HeadingFontBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdatePreview()));
        BodyFontBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdatePreview()));
        EffectsBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdatePreview()));
        foreach (var colorBox in ThemeColorTextBoxes())
        {
            colorBox.TextChanged += (_, _) =>
            {
                UpdatePreview();
                UpdateColorPickerSwatches();
            };
        }
    }

    private void LoadTheme(WorkbookTheme theme)
    {
        ThemeNameBox.Text = theme.Name;
        HeadingFontBox.Text = theme.MajorFontName;
        BodyFontBox.Text = theme.MinorFontName;
        EffectsBox.Text = theme.EffectsName;

        foreach (var field in ThemeColorFields())
            field.TextBox.Text = WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(field.Slot));

        UpdatePreview();
        UpdateColorPickerSwatches();
    }

    private void OfficePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var theme = _mode switch
        {
            WorkbookThemeDialogMode.Colors => WorkbookThemeWorkflow.ApplyOfficeColors(ReadCurrentDialogThemeOrInitial()),
            WorkbookThemeDialogMode.Effects => ReadCurrentDialogThemeOrInitial().WithEffects(WorkbookTheme.Office.EffectsName),
            _ => WorkbookTheme.Office
        };
        LoadTheme(theme);
    }

    private void ColorfulPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var theme = _mode switch
        {
            WorkbookThemeDialogMode.Colors => WorkbookThemeWorkflow.ApplyColorfulColors(ReadCurrentDialogThemeOrInitial()),
            WorkbookThemeDialogMode.Effects => ReadCurrentDialogThemeOrInitial().WithEffects("Subtle"),
            _ => WorkbookThemeWorkflow.CreateColorfulTheme()
        };
        LoadTheme(theme);
    }

    private void GrayscalePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var theme = _mode switch
        {
            WorkbookThemeDialogMode.Colors => WorkbookThemeWorkflow.ApplyGrayscaleColors(ReadCurrentDialogThemeOrInitial()),
            WorkbookThemeDialogMode.Effects => ReadCurrentDialogThemeOrInitial().WithEffects("Refined"),
            _ => WorkbookThemeWorkflow.CreateGrayscaleTheme()
        };
        LoadTheme(theme);
    }

    private void ThemeColorPickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string colorBoxName } ||
            FindName(colorBoxName) is not TextBox colorBox)
        {
            return;
        }

        CellColor? initialColor = null;
        try
        {
            initialColor = WorkbookThemeDialogColorCodec.ParseColor(colorBox.Text);
        }
        catch (FormatException)
        {
        }

        var dialog = new ColorPickerDialog(initialColor) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedColor.HasValue)
        {
            colorBox.Text = WorkbookThemeDialogColorCodec.FormatColor(dialog.SelectedColor.Value);
            UpdatePreview();
            UpdateColorPickerSwatches();
        }
    }

    private void UpdatePreview()
    {
        PreviewHeadingText.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(HeadingFontBox.Text) ? "Aptos Display" : HeadingFontBox.Text);
        PreviewBodyText.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(BodyFontBox.Text) ? "Aptos" : BodyFontBox.Text);

        var dark1 = ParsePreviewColor(Dark1ColorBox.Text);
        var light1 = ParsePreviewColor(Light1ColorBox.Text);
        var dark2 = ParsePreviewColor(Dark2ColorBox.Text);
        var light2 = ParsePreviewColor(Light2ColorBox.Text);
        var accent1 = ParsePreviewColor(Accent1ColorBox.Text);
        var accent2 = ParsePreviewColor(Accent2ColorBox.Text);
        var accent3 = ParsePreviewColor(Accent3ColorBox.Text);
        var accent4 = ParsePreviewColor(Accent4ColorBox.Text);
        var accent5 = ParsePreviewColor(Accent5ColorBox.Text);
        var accent6 = ParsePreviewColor(Accent6ColorBox.Text);
        var hyperlink = ParsePreviewColor(HyperlinkColorBox.Text);

        ThemePreviewPane.Background = ToBrush(light1);
        ThemePreviewPane.BorderBrush = ToBrush(dark2);
        PreviewHeadingText.Foreground = ToBrush(accent1);
        PreviewBodyText.Foreground = ToBrush(hyperlink);

        PreviewAccentStrip.Children.Clear();
        foreach (var color in new[] { accent1, accent2, accent3, accent4, accent5, accent6 })
        {
            PreviewAccentStrip.Children.Add(new Border
            {
                Background = ToBrush(color),
                Margin = new Thickness(0, 0, 4, 0)
            });
        }

        ApplyPreviewBorder(PreviewSheetHeader, light2, accent1);
        ApplyPreviewBorder(PreviewTableHeader, accent1, accent1);
        ApplyPreviewBorder(PreviewTableBand, Blend(light1, accent1, 0.78), accent1);
        ApplyPreviewBorder(PreviewShapeSample, accent2, accent3);
        ApplyPreviewBorder(PreviewChartBar1, accent4, accent4);
        ApplyPreviewBorder(PreviewChartBar2, accent5, accent5);
        ApplyPreviewBorder(PreviewChartBar3, accent6, accent6);
        PreviewShapeSample.Effect = CreatePreviewEffect(dark1);
    }

    private void UpdateColorPickerSwatches()
    {
        foreach (var field in ThemeColorFields())
        {
            field.Button.Background = ToBrush(ParsePreviewColor(field.TextBox.Text));
        }
    }

    private WorkbookTheme ReadCurrentDialogThemeOrInitial()
    {
        var colorTextBySlot = ThemeColorFields()
            .ToDictionary(field => field.Slot, field => field.TextBox.Text);

        return WorkbookThemeDialogPlanner.TryCreateTheme(
            _initialTheme,
            ThemeNameBox.Text,
            HeadingFontBox.Text,
            BodyFontBox.Text,
            EffectsBox.Text,
            colorTextBySlot,
            out var theme,
            out _)
            ? theme
            : _initialTheme;
    }

    private static void ApplyPreviewBorder(Border border, CellColor fill, CellColor stroke)
    {
        border.Background = ToBrush(fill);
        border.BorderBrush = ToBrush(stroke);
        border.BorderThickness = new Thickness(1);
    }

    private DropShadowEffect? CreatePreviewEffect(CellColor shadowColor)
    {
        var effectsName = (EffectsBox.Text ?? string.Empty).Trim();
        var (opacity, depth, blur) = effectsName.ToUpperInvariant() switch
        {
            "SUBTLE" => (0.24, 2.0, 5.0),
            "REFINED" => (0.36, 4.0, 8.0),
            _ => (0.0, 0.0, 0.0)
        };

        if (opacity <= 0)
            return null;

        return new DropShadowEffect
        {
            BlurRadius = blur,
            Direction = 315,
            Opacity = opacity,
            ShadowDepth = depth,
            Color = ToMediaColor(shadowColor)
        };
    }

    private static CellColor Blend(CellColor baseColor, CellColor overlayColor, double overlayWeight)
    {
        var baseWeight = 1 - overlayWeight;
        return new CellColor(
            (byte)Math.Round(baseColor.R * baseWeight + overlayColor.R * overlayWeight),
            (byte)Math.Round(baseColor.G * baseWeight + overlayColor.G * overlayWeight),
            (byte)Math.Round(baseColor.B * baseWeight + overlayColor.B * overlayWeight));
    }

    private static CellColor ParsePreviewColor(string text)
        => WorkbookThemeDialogPlanner.PreviewColorOrBlack(text);

    private static Color ToMediaColor(CellColor color) => Color.FromRgb(color.R, color.G, color.B);

    private static SolidColorBrush ToBrush(CellColor color) => new(ToMediaColor(color));

    private IEnumerable<TextBox> ThemeColorTextBoxes()
    {
        foreach (var field in ThemeColorFields())
            yield return field.TextBox;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var colorTextBySlot = ThemeColorFields()
            .ToDictionary(field => field.Slot, field => field.TextBox.Text);

        if (!WorkbookThemeDialogPlanner.TryCreateTheme(
            _initialTheme,
            ThemeNameBox.Text,
            HeadingFontBox.Text,
            BodyFontBox.Text,
            EffectsBox.Text,
            colorTextBySlot,
            out var theme,
            out var error))
        {
            if (error is not null)
                ShowInvalidThemeColor(error);
            return;
        }

        ResultTheme = theme;
        DialogResult = true;
    }

    private void ShowInvalidThemeColor(WorkbookThemeDialogValidationError error)
    {
        DialogMessageHelper.ShowWarning(this, error.Message, UiText.Get("WorkbookTheme_CustomizeThemeTitle"));
        foreach (var field in ThemeColorFields())
        {
            if (field.Slot != error.Slot)
                continue;

            if (field.TextBox is not null)
                FocusInvalidColorInput(field.TextBox);
            break;
        }
    }

    private static void FocusInvalidColorInput(TextBox colorBox)
    {
        colorBox.Focus();
        colorBox.SelectAll();
        Keyboard.Focus(colorBox);
    }

    private void FocusInitialKeyboardTarget()
    {
        if (_mode == WorkbookThemeDialogMode.Colors)
        {
            Accent1ColorBox.Focus();
            Accent1ColorBox.SelectAll();
            Keyboard.Focus(Accent1ColorBox);
            return;
        }

        if (_mode == WorkbookThemeDialogMode.Effects)
        {
            EffectsBox.Focus();
            EffectsBox.IsDropDownOpen = true;
            Keyboard.Focus(EffectsBox);
            return;
        }

        ThemeNameBox.Focus();
        ThemeNameBox.SelectAll();
        Keyboard.Focus(ThemeNameBox);
    }

}
