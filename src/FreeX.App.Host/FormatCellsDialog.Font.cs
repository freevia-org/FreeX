using System.Windows;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class FormatCellsDialog
{
    private void UpdateFontPreview()
    {
        if (DlgFontSamplePreview is null)
            return;

        DlgFontSamplePreview.FontFamily = new FontFamily(DlgFontNameBox.Text);
        DlgFontSamplePreview.FontSize = FormatCellsInputParser.TryParseFontSize(DlgFontSizeBox.Text) ?? 11;
        DlgFontSamplePreview.FontWeight = IsSelectedFontBold() ? FontWeights.Bold : FontWeights.Normal;
        DlgFontSamplePreview.FontStyle = IsSelectedFontItalic() ? FontStyles.Italic : FontStyles.Normal;
        DlgFontSamplePreview.Foreground = BrushForColor(TryParseColor(DlgFontColorBox.Text), Brushes.Black);

        var decorations = new TextDecorationCollection();
        if (IsSingleUnderlineSelected() || DlgDoubleUnderlineCheck.IsChecked == true)
        {
            foreach (var decoration in TextDecorations.Underline)
                decorations.Add(decoration);
        }

        if (DlgStrikeCheck.IsChecked == true)
        {
            foreach (var decoration in TextDecorations.Strikethrough)
                decorations.Add(decoration);
        }

        DlgFontSamplePreview.TextDecorations = decorations;
    }

    private static string FontStyleLabel(bool bold, bool italic) => (bold, italic) switch
    {
        (true, true) => UiText.Get("FormatCells_FontStyleBoldItalic"),
        (true, false) => UiText.Get("FormatCells_FontStyleBold"),
        (false, true) => UiText.Get("FormatCells_FontStyleItalic"),
        _ => UiText.Get("FormatCells_FontStyleRegular")
    };

    private bool IsSelectedFontBold()
        => DlgFontStyleList.SelectedItem is string style &&
           (string.Equals(style, UiText.Get("FormatCells_FontStyleBold"), StringComparison.Ordinal) ||
            string.Equals(style, UiText.Get("FormatCells_FontStyleBoldItalic"), StringComparison.Ordinal));

    private bool IsSelectedFontItalic()
        => DlgFontStyleList.SelectedItem is string style &&
           (string.Equals(style, UiText.Get("FormatCells_FontStyleItalic"), StringComparison.Ordinal) ||
            string.Equals(style, UiText.Get("FormatCells_FontStyleBoldItalic"), StringComparison.Ordinal));

    private bool IsSingleUnderlineSelected()
        => DlgUnderlineStyleBox.SelectedItem is string underline &&
           (string.Equals(underline, UiText.Get("FormatCells_UnderlineSingle"), StringComparison.Ordinal) ||
            string.Equals(underline, UiText.Get("FormatCells_UnderlineSingleAccounting"), StringComparison.Ordinal));

    private static bool IsDoubleUnderlineSelected(string underline) =>
        string.Equals(underline, UiText.Get("FormatCells_UnderlineDouble"), StringComparison.Ordinal) ||
        string.Equals(underline, UiText.Get("FormatCells_UnderlineDoubleAccounting"), StringComparison.Ordinal);

    private string? ResolveSelectedFontName()
    {
        var typed = DlgFontNameBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(typed))
            return typed;

        return DlgFontNameBox.SelectedItem as string;
    }

    private void DlgNormalFontCheck_Checked(object sender, RoutedEventArgs e)
    {
        var normal = CellStyle.Default;
        EnsureFontNameAvailable(normal.FontName);
        DlgFontNameBox.SelectedItem = DlgFontNameBox.Items
            .OfType<string>()
            .FirstOrDefault(font => string.Equals(font, normal.FontName, StringComparison.CurrentCultureIgnoreCase));
        DlgFontNameBox.Text = normal.FontName;
        DlgFontSizeBox.Text = normal.FontSize.ToString("0.#");
        DlgFontStyleList.SelectedItem = FontStyleLabel(normal.Bold, normal.Italic);
        DlgUnderlineStyleBox.SelectedItem = UiText.Get("FormatCells_UnderlineNone");
        DlgDoubleUnderlineCheck.IsChecked = normal.DoubleUnderline;
        DlgStrikeCheck.IsChecked = normal.Strikethrough;
        DlgSuperscriptCheck.IsChecked = normal.Superscript;
        DlgSubscriptCheck.IsChecked = normal.Subscript;
        DlgFontColorBox.Text = ColorInputParser.FormatRgbColor(normal.FontColor);
        UpdateFontPreview();
    }

    private void DlgSuperscriptCheck_Checked(object sender, RoutedEventArgs e)
    {
        DlgSubscriptCheck.IsChecked = false;
        UpdateFontPreview();
    }

    private void DlgSubscriptCheck_Checked(object sender, RoutedEventArgs e)
    {
        DlgSuperscriptCheck.IsChecked = false;
        UpdateFontPreview();
    }

    private void EnsureFontNameAvailable(string fontName)
    {
        if (DlgFontNameBox.Items.OfType<string>().Contains(fontName, StringComparer.CurrentCultureIgnoreCase))
            return;

        DlgFontNameBox.ItemsSource = FontNamesWithFallback(fontName);
    }

    private static string[] FontNamesWithFallback(string fontName)
    {
        var fonts = Fonts.SystemFontFamilies
            .Select(font => font.Source)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(font => font, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(fontName)
            && !fonts.Contains(fontName, StringComparer.CurrentCultureIgnoreCase))
        {
            fonts.Insert(0, fontName);
        }

        return fonts.ToArray();
    }
}
