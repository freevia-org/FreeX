using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
        (true, true) => "Bold Italic",
        (true, false) => "Bold",
        (false, true) => "Italic",
        _ => "Regular"
    };

    private bool IsSelectedFontBold()
        => DlgFontStyleList.SelectedItem is string style
            && style.Contains("Bold", StringComparison.OrdinalIgnoreCase);

    private bool IsSelectedFontItalic()
        => DlgFontStyleList.SelectedItem is string style
            && style.Contains("Italic", StringComparison.OrdinalIgnoreCase);

    private bool IsSingleUnderlineSelected()
        => DlgUnderlineStyleBox.SelectedItem is string underline
            && underline == "Single";

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
        DlgUnderlineStyleBox.SelectedItem = "None";
        DlgDoubleUnderlineCheck.IsChecked = normal.DoubleUnderline;
        DlgStrikeCheck.IsChecked = normal.Strikethrough;
        DlgSuperscriptCheck.IsChecked = normal.Superscript;
        DlgSubscriptCheck.IsChecked = normal.Subscript;
        DlgFontColorBox.Text = ColorInputParser.FormatRgbColor(normal.FontColor);
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
