using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class WorkbookThemeDialog : Window
{
    private readonly WorkbookTheme _initialTheme;

    public WorkbookThemeDialog(WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        _initialTheme = theme;
        InitializeComponent();
        ApplyThemeColorAutomationMetadata();
        PopulateOptions();
        WirePreviewRefresh();
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

    private void WirePreviewRefresh()
    {
        HeadingFontBox.SelectionChanged += (_, _) => UpdatePreview();
        BodyFontBox.SelectionChanged += (_, _) => UpdatePreview();
        HeadingFontBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdatePreview()));
        BodyFontBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdatePreview()));
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

    private void OfficePresetButton_Click(object sender, RoutedEventArgs e) =>
        LoadTheme(WorkbookTheme.Office);

    private void ColorfulPresetButton_Click(object sender, RoutedEventArgs e) =>
        LoadTheme(WorkbookThemeWorkflow.CreateColorfulTheme());

    private void GrayscalePresetButton_Click(object sender, RoutedEventArgs e) =>
        LoadTheme(WorkbookThemeWorkflow.CreateGrayscaleTheme());

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

        PreviewAccentStrip.Children.Clear();
        foreach (var colorBox in ThemeColorFields()
                     .Where(field => field.IsAccent)
                     .Select(field => field.TextBox))
        {
            PreviewAccentStrip.Children.Add(new Border
            {
                Background = new SolidColorBrush(ToMediaColor(ParsePreviewColor(colorBox.Text))),
                Margin = new Thickness(0, 0, 4, 0)
            });
        }

        PreviewBodyText.Foreground = new SolidColorBrush(ToMediaColor(ParsePreviewColor(HyperlinkColorBox.Text)));
    }

    private void UpdateColorPickerSwatches()
    {
        foreach (var field in ThemeColorFields())
        {
            field.Button.Background = new SolidColorBrush(ToMediaColor(ParsePreviewColor(field.TextBox.Text)));
        }
    }

    private static CellColor ParsePreviewColor(string text)
        => WorkbookThemeDialogPlanner.PreviewColorOrBlack(text);

    private static Color ToMediaColor(CellColor color) => Color.FromRgb(color.R, color.G, color.B);

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
        var field = ThemeColorFields().FirstOrDefault(field => field.Slot == error.Slot);
        if (field.TextBox is not null)
            FocusInvalidColorInput(field.TextBox);
    }

    private static void FocusInvalidColorInput(TextBox colorBox)
    {
        colorBox.Focus();
        colorBox.SelectAll();
        Keyboard.Focus(colorBox);
    }

    private void FocusInitialKeyboardTarget()
    {
        ThemeNameBox.Focus();
        ThemeNameBox.SelectAll();
        Keyboard.Focus(ThemeNameBox);
    }

}
