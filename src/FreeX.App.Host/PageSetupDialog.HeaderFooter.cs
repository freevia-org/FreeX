using System.Windows;
using System.Windows.Controls;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class PageSetupDialog
{
    private void HeaderPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HeaderPresetBox.SelectedItem is not ComboBoxItem { Tag: string preset })
            return;

        Header = Header with { Center = preset };
        UpdateHeaderFooterPreview();
    }

    private void FooterPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FooterPresetBox.SelectedItem is not ComboBoxItem { Tag: string preset })
            return;

        Footer = Footer with { Center = preset };
        UpdateHeaderFooterPreview();
    }

    private void CustomHeaderFooterButton_Click(object sender, RoutedEventArgs e)
    {
        var sheet = new Sheet(_sheetId, "Sheet")
        {
            PageHeader = Header,
            PageFooter = Footer,
            FirstPageHeader = FirstPageHeader,
            FirstPageFooter = FirstPageFooter,
            EvenPageHeader = EvenPageHeader,
            EvenPageFooter = EvenPageFooter,
            PageHeaderPictures = HeaderPictures.DeepClone(),
            PageFooterPictures = FooterPictures.DeepClone(),
            FirstPageHeaderPictures = FirstPageHeaderPictures.DeepClone(),
            FirstPageFooterPictures = FirstPageFooterPictures.DeepClone(),
            EvenPageHeaderPictures = EvenPageHeaderPictures.DeepClone(),
            EvenPageFooterPictures = EvenPageFooterPictures.DeepClone(),
            DifferentFirstPageHeaderFooter = DifferentFirstPageBox.IsChecked == true,
            DifferentOddEvenHeaderFooter = DifferentOddEvenBox.IsChecked == true,
            HeaderFooterScaleWithDocument = ScaleWithDocumentBox.IsChecked == true,
            HeaderFooterAlignWithMargins = AlignWithMarginsBox.IsChecked == true
        };

        var dialog = new HeaderFooterDialog(sheet) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        Header = dialog.Header;
        Footer = dialog.Footer;
        FirstPageHeader = dialog.FirstPageHeader;
        FirstPageFooter = dialog.FirstPageFooter;
        EvenPageHeader = dialog.EvenPageHeader;
        EvenPageFooter = dialog.EvenPageFooter;
        HeaderPictures = dialog.HeaderPictures.DeepClone();
        FooterPictures = dialog.FooterPictures.DeepClone();
        FirstPageHeaderPictures = dialog.FirstPageHeaderPictures.DeepClone();
        FirstPageFooterPictures = dialog.FirstPageFooterPictures.DeepClone();
        EvenPageHeaderPictures = dialog.EvenPageHeaderPictures.DeepClone();
        EvenPageFooterPictures = dialog.EvenPageFooterPictures.DeepClone();
        DifferentFirstPage = dialog.DifferentFirstPage;
        DifferentOddEvenPages = dialog.DifferentOddEvenPages;
        ScaleHeaderFooterWithDocument = dialog.ScaleWithDocument;
        AlignHeaderFooterWithMargins = dialog.AlignWithMargins;
        DifferentFirstPageBox.IsChecked = DifferentFirstPage;
        DifferentOddEvenBox.IsChecked = DifferentOddEvenPages;
        ScaleWithDocumentBox.IsChecked = ScaleHeaderFooterWithDocument;
        AlignWithMarginsBox.IsChecked = AlignHeaderFooterWithMargins;
        SelectPreset(HeaderPresetBox, Header.Center);
        SelectPreset(FooterPresetBox, Footer.Center);
        UpdateHeaderFooterPreview();
    }

    private static void SelectPreset(ComboBox comboBox, string centerText)
    {
        for (var i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is ComboBoxItem { Tag: string preset } && preset == centerText)
            {
                comboBox.SelectedIndex = i;
                return;
            }
        }

        comboBox.SelectedIndex = -1;
    }

    private void UpdateHeaderFooterPreview()
    {
        HeaderPreviewText.Text = FormatHeaderFooterPreview(Header);
        FooterPreviewText.Text = FormatHeaderFooterPreview(Footer);
    }

    private static string FormatHeaderFooterPreview(WorksheetHeaderFooter value)
    {
        var parts = new[] { value.Left, value.Center, value.Right }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        return parts.Length == 0 ? "(none)" : string.Join(" | ", parts);
    }
}
