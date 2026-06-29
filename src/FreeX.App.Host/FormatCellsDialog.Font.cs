using System.Windows;
using System.Windows.Media;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class FormatCellsDialog
{
    private void UpdateFontPreview()
    {
        if (DlgFontSamplePreview is null)
            return;

        DlgFontSamplePreview.FontFamily = new FontFamily(DlgFontNameBox.Text);
        DlgFontSamplePreview.FontSize = FormatCellsDialogPlanner.TryParseFontSize(DlgFontSizeBox.Text) ?? 11;
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

    private static FormatCellsDialogFontLabels FontLabels() =>
        new(
            UiText.Get("FormatCells_FontStyleRegular"),
            UiText.Get("FormatCells_FontStyleItalic"),
            UiText.Get("FormatCells_FontStyleBold"),
            UiText.Get("FormatCells_FontStyleBoldItalic"),
            UiText.Get("FormatCells_UnderlineNone"),
            UiText.Get("FormatCells_UnderlineSingle"),
            UiText.Get("FormatCells_UnderlineDouble"),
            UiText.Get("FormatCells_UnderlineSingleAccounting"),
            UiText.Get("FormatCells_UnderlineDoubleAccounting"));

    private static string FontStyleLabel(bool bold, bool italic) =>
        FormatCellsDialogPlanner.FontStyleLabel(bold, italic, FontLabels());

    private bool IsSelectedFontBold()
        => FormatCellsDialogPlanner.IsFontStyleBold(DlgFontStyleList.SelectedItem as string, FontLabels());

    private bool IsSelectedFontItalic()
        => FormatCellsDialogPlanner.IsFontStyleItalic(DlgFontStyleList.SelectedItem as string, FontLabels());

    private bool IsSingleUnderlineSelected()
        => FormatCellsDialogPlanner.IsSingleUnderlineSelected(DlgUnderlineStyleBox.SelectedItem as string, FontLabels());

    private static bool IsDoubleUnderlineSelected(string underline) =>
        FormatCellsDialogPlanner.IsDoubleUnderlineSelected(underline, FontLabels());

    private void DlgNormalFontCheck_Checked(object sender, RoutedEventArgs e)
    {
        var normal = CellStyle.Default;
        EnsureFontNameAvailable(normal.FontName);
        DlgFontNameBox.SelectedItem = FindFontNameItem(normal.FontName);
        DlgFontNameBox.Text = normal.FontName;
        DlgFontSizeBox.Text = normal.FontSize.ToString("0.#");
        DlgFontStyleList.SelectedItem = FontStyleLabel(normal.Bold, normal.Italic);
        DlgUnderlineStyleBox.SelectedItem = FontLabels().UnderlineNone;
        DlgDoubleUnderlineCheck.IsChecked = normal.DoubleUnderline;
        DlgStrikeCheck.IsChecked = normal.Strikethrough;
        DlgSuperscriptCheck.IsChecked = normal.Superscript;
        DlgSubscriptCheck.IsChecked = normal.Subscript;
        DlgFontColorBox.Text = ColorInputParser.FormatRgbColor(normal.FontColor);
        UpdateFontPreview();
    }

    private string? FindFontNameItem(string fontName)
    {
        foreach (var item in DlgFontNameBox.Items)
        {
            if (item is string font && string.Equals(font, fontName, StringComparison.CurrentCultureIgnoreCase))
                return font;
        }

        return null;
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
