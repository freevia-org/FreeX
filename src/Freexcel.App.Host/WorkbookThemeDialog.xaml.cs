using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class WorkbookThemeDialog : Window
{
    private readonly WorkbookTheme _initialTheme;

    public WorkbookThemeDialog(WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        _initialTheme = theme;
        InitializeComponent();
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

        Dark1ColorBox.Text = WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(WorkbookThemeColorSlot.Dark1));
        Light1ColorBox.Text = WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(WorkbookThemeColorSlot.Light1));
        Dark2ColorBox.Text = WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(WorkbookThemeColorSlot.Dark2));
        Light2ColorBox.Text = WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(WorkbookThemeColorSlot.Light2));
        Accent1ColorBox.Text = WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(WorkbookThemeColorSlot.Accent1));
        Accent2ColorBox.Text = WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(WorkbookThemeColorSlot.Accent2));
        Accent3ColorBox.Text = WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(WorkbookThemeColorSlot.Accent3));
        Accent4ColorBox.Text = WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(WorkbookThemeColorSlot.Accent4));
        Accent5ColorBox.Text = WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(WorkbookThemeColorSlot.Accent5));
        Accent6ColorBox.Text = WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(WorkbookThemeColorSlot.Accent6));
        HyperlinkColorBox.Text = WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(WorkbookThemeColorSlot.Hyperlink));
        FollowedHyperlinkColorBox.Text = WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(WorkbookThemeColorSlot.FollowedHyperlink));
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
        foreach (var colorBox in new[]
                 {
                     Accent1ColorBox,
                     Accent2ColorBox,
                     Accent3ColorBox,
                     Accent4ColorBox,
                     Accent5ColorBox,
                     Accent6ColorBox
                 })
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
        foreach (var (textBox, button) in ThemeColorPickerPairs())
        {
            button.Background = new SolidColorBrush(ToMediaColor(ParsePreviewColor(textBox.Text)));
        }
    }

    private static CellColor ParsePreviewColor(string text)
    {
        try
        {
            return WorkbookThemeDialogColorCodec.ParseColor(text);
        }
        catch (FormatException)
        {
            return new CellColor(0, 0, 0);
        }
    }

    private static Color ToMediaColor(CellColor color) => Color.FromRgb(color.R, color.G, color.B);

    private IEnumerable<TextBox> ThemeColorTextBoxes()
    {
        foreach (var (textBox, _) in ThemeColorPickerPairs())
            yield return textBox;
    }

    private IEnumerable<(TextBox TextBox, Button Button)> ThemeColorPickerPairs()
    {
        yield return (Dark1ColorBox, Dark1ColorPickerButton);
        yield return (Light1ColorBox, Light1ColorPickerButton);
        yield return (Dark2ColorBox, Dark2ColorPickerButton);
        yield return (Light2ColorBox, Light2ColorPickerButton);
        yield return (Accent1ColorBox, Accent1ColorPickerButton);
        yield return (Accent2ColorBox, Accent2ColorPickerButton);
        yield return (Accent3ColorBox, Accent3ColorPickerButton);
        yield return (Accent4ColorBox, Accent4ColorPickerButton);
        yield return (Accent5ColorBox, Accent5ColorPickerButton);
        yield return (Accent6ColorBox, Accent6ColorPickerButton);
        yield return (HyperlinkColorBox, HyperlinkColorPickerButton);
        yield return (FollowedHyperlinkColorBox, FollowedHyperlinkColorPickerButton);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadThemeColor(Dark1ColorBox, out var dark1) ||
            !TryReadThemeColor(Light1ColorBox, out var light1) ||
            !TryReadThemeColor(Dark2ColorBox, out var dark2) ||
            !TryReadThemeColor(Light2ColorBox, out var light2) ||
            !TryReadThemeColor(Accent1ColorBox, out var accent1) ||
            !TryReadThemeColor(Accent2ColorBox, out var accent2) ||
            !TryReadThemeColor(Accent3ColorBox, out var accent3) ||
            !TryReadThemeColor(Accent4ColorBox, out var accent4) ||
            !TryReadThemeColor(Accent5ColorBox, out var accent5) ||
            !TryReadThemeColor(Accent6ColorBox, out var accent6) ||
            !TryReadThemeColor(HyperlinkColorBox, out var hyperlink) ||
            !TryReadThemeColor(FollowedHyperlinkColorBox, out var followedHyperlink))
        {
            return;
        }

        ResultTheme = WorkbookThemeWorkflow.CreateCustomTheme(
                _initialTheme,
                ThemeNameBox.Text,
                HeadingFontBox.Text,
                BodyFontBox.Text,
                EffectsBox.Text)
            .WithColor(WorkbookThemeColorSlot.Dark1, dark1)
            .WithColor(WorkbookThemeColorSlot.Light1, light1)
            .WithColor(WorkbookThemeColorSlot.Dark2, dark2)
            .WithColor(WorkbookThemeColorSlot.Light2, light2)
            .WithColor(WorkbookThemeColorSlot.Accent1, accent1)
            .WithColor(WorkbookThemeColorSlot.Accent2, accent2)
            .WithColor(WorkbookThemeColorSlot.Accent3, accent3)
            .WithColor(WorkbookThemeColorSlot.Accent4, accent4)
            .WithColor(WorkbookThemeColorSlot.Accent5, accent5)
            .WithColor(WorkbookThemeColorSlot.Accent6, accent6)
            .WithColor(WorkbookThemeColorSlot.Hyperlink, hyperlink)
            .WithColor(WorkbookThemeColorSlot.FollowedHyperlink, followedHyperlink);
        DialogResult = true;
    }

    private bool TryReadThemeColor(TextBox colorBox, out CellColor color)
    {
        try
        {
            color = WorkbookThemeDialogColorCodec.ParseColor(colorBox.Text);
            return true;
        }
        catch (FormatException ex)
        {
            color = default;
            MessageBox.Show(this, ex.Message, "Customize Theme", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusInvalidColorInput(colorBox);
            return false;
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
        ThemeNameBox.Focus();
        ThemeNameBox.SelectAll();
        Keyboard.Focus(ThemeNameBox);
    }

}
