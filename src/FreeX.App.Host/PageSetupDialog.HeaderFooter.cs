using System.Windows;
using System.Windows.Controls;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class PageSetupDialog
{
    private sealed record PageSetupPresetComboItem(string Label, string Value);

    private void HeaderPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedPresetValue(HeaderPresetBox.SelectedItem) is not { } preset)
            return;

        Header = Header with { Center = preset };
        UpdateHeaderFooterPreview();
    }

    private void FooterPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedPresetValue(FooterPresetBox.SelectedItem) is not { } preset)
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

    private void PopulateHeaderFooterPresetBoxes()
    {
        PopulatePresetBox(HeaderPresetBox, PageSetupDialogModel.HeaderPresetChoices);
        PopulatePresetBox(FooterPresetBox, PageSetupDialogModel.FooterPresetChoices);
    }

    private static void PopulatePresetBox(
        ComboBox comboBox,
        IReadOnlyList<PageSetupChoice<string>> choices)
    {
        if (comboBox.ItemsSource is not null)
            return;

        comboBox.DisplayMemberPath = nameof(PageSetupPresetComboItem.Label);
        comboBox.ItemsSource = choices
            .Select(choice => new PageSetupPresetComboItem(UiText.Get(choice.LabelResourceKey), choice.Value))
            .ToArray();
    }

    private static void SelectPreset(ComboBox comboBox, string centerText)
    {
        for (var i = 0; i < comboBox.Items.Count; i++)
        {
            if (SelectedPresetValue(comboBox.Items[i]) == centerText)
            {
                comboBox.SelectedIndex = i;
                return;
            }
        }

        comboBox.SelectedIndex = -1;
    }

    private static string? SelectedPresetValue(object? selectedItem) =>
        selectedItem switch
        {
            PageSetupPresetComboItem item => item.Value,
            ComboBoxItem { Tag: string preset } => preset,
            _ => null
        };

    private void UpdateHeaderFooterPreview()
    {
        HeaderPreviewText.Text = PageSetupDialogModel.BuildHeaderFooterPreview(Header, UiText.Get("PageSetup_None"));
        FooterPreviewText.Text = PageSetupDialogModel.BuildHeaderFooterPreview(Footer, UiText.Get("PageSetup_None"));
    }
}
