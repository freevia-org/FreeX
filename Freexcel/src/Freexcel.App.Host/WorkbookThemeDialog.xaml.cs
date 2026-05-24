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
            colorBox.TextChanged += (_, _) => UpdatePreview();
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
        yield return Dark1ColorBox;
        yield return Light1ColorBox;
        yield return Dark2ColorBox;
        yield return Light2ColorBox;
        yield return Accent1ColorBox;
        yield return Accent2ColorBox;
        yield return Accent3ColorBox;
        yield return Accent4ColorBox;
        yield return Accent5ColorBox;
        yield return Accent6ColorBox;
        yield return HyperlinkColorBox;
        yield return FollowedHyperlinkColorBox;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ResultTheme = WorkbookThemeWorkflow.CreateCustomTheme(
                    _initialTheme,
                    ThemeNameBox.Text,
                    HeadingFontBox.Text,
                    BodyFontBox.Text,
                    EffectsBox.Text)
                .WithColor(WorkbookThemeColorSlot.Dark1, WorkbookThemeDialogColorCodec.ParseColor(Dark1ColorBox.Text))
                .WithColor(WorkbookThemeColorSlot.Light1, WorkbookThemeDialogColorCodec.ParseColor(Light1ColorBox.Text))
                .WithColor(WorkbookThemeColorSlot.Dark2, WorkbookThemeDialogColorCodec.ParseColor(Dark2ColorBox.Text))
                .WithColor(WorkbookThemeColorSlot.Light2, WorkbookThemeDialogColorCodec.ParseColor(Light2ColorBox.Text))
                .WithColor(WorkbookThemeColorSlot.Accent1, WorkbookThemeDialogColorCodec.ParseColor(Accent1ColorBox.Text))
                .WithColor(WorkbookThemeColorSlot.Accent2, WorkbookThemeDialogColorCodec.ParseColor(Accent2ColorBox.Text))
                .WithColor(WorkbookThemeColorSlot.Accent3, WorkbookThemeDialogColorCodec.ParseColor(Accent3ColorBox.Text))
                .WithColor(WorkbookThemeColorSlot.Accent4, WorkbookThemeDialogColorCodec.ParseColor(Accent4ColorBox.Text))
                .WithColor(WorkbookThemeColorSlot.Accent5, WorkbookThemeDialogColorCodec.ParseColor(Accent5ColorBox.Text))
                .WithColor(WorkbookThemeColorSlot.Accent6, WorkbookThemeDialogColorCodec.ParseColor(Accent6ColorBox.Text))
                .WithColor(WorkbookThemeColorSlot.Hyperlink, WorkbookThemeDialogColorCodec.ParseColor(HyperlinkColorBox.Text))
                .WithColor(WorkbookThemeColorSlot.FollowedHyperlink, WorkbookThemeDialogColorCodec.ParseColor(FollowedHyperlinkColorBox.Text));
        }
        catch (FormatException ex)
        {
            MessageBox.Show(ex.Message, "Customize Theme", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        ThemeNameBox.Focus();
        ThemeNameBox.SelectAll();
        Keyboard.Focus(ThemeNameBox);
    }

}
