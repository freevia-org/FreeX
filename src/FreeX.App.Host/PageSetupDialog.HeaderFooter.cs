using System.Windows;
using System.Windows.Controls;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class PageSetupDialog
{
    private void HeaderPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HeaderPresetBox.SelectedIndex < 0)
            return;

        Header = PageSetupDialogPlanner.ApplyHeaderPreset(Header, HeaderPresetBox.SelectedIndex);
        UpdateHeaderFooterPreview();
    }

    private void FooterPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FooterPresetBox.SelectedIndex < 0)
            return;

        Footer = PageSetupDialogPlanner.ApplyFooterPreset(Footer, FooterPresetBox.SelectedIndex);
        UpdateHeaderFooterPreview();
    }

    private void CustomHeaderFooterButton_Click(object sender, RoutedEventArgs e)
    {
        var initial = new HeaderFooterEditorState(
            Header,
            Footer,
            FirstPageHeader,
            FirstPageFooter,
            EvenPageHeader,
            EvenPageFooter,
            HeaderPictures,
            FooterPictures,
            FirstPageHeaderPictures,
            FirstPageFooterPictures,
            EvenPageHeaderPictures,
            EvenPageFooterPictures,
            DifferentFirstPageBox.IsChecked == true,
            DifferentOddEvenBox.IsChecked == true,
            ScaleWithDocumentBox.IsChecked == true,
            AlignWithMarginsBox.IsChecked == true);

        var dialog = new HeaderFooterDialog(initial) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var result = dialog.ResultState;
        Header = result.Header;
        Footer = result.Footer;
        FirstPageHeader = result.FirstPageHeader;
        FirstPageFooter = result.FirstPageFooter;
        EvenPageHeader = result.EvenPageHeader;
        EvenPageFooter = result.EvenPageFooter;
        HeaderPictures = result.HeaderPictures;
        FooterPictures = result.FooterPictures;
        FirstPageHeaderPictures = result.FirstPageHeaderPictures;
        FirstPageFooterPictures = result.FirstPageFooterPictures;
        EvenPageHeaderPictures = result.EvenPageHeaderPictures;
        EvenPageFooterPictures = result.EvenPageFooterPictures;
        DifferentFirstPage = result.DifferentFirstPage;
        DifferentOddEvenPages = result.DifferentOddEvenPages;
        ScaleHeaderFooterWithDocument = result.ScaleWithDocument;
        AlignHeaderFooterWithMargins = result.AlignWithMargins;
        DifferentFirstPageBox.IsChecked = DifferentFirstPage;
        DifferentOddEvenBox.IsChecked = DifferentOddEvenPages;
        ScaleWithDocumentBox.IsChecked = ScaleHeaderFooterWithDocument;
        AlignWithMarginsBox.IsChecked = AlignHeaderFooterWithMargins;
        HeaderPresetBox.SelectedIndex = PageSetupDialogPlanner.ResolveHeaderPresetIndex(Header);
        FooterPresetBox.SelectedIndex = PageSetupDialogPlanner.ResolveFooterPresetIndex(Footer);
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

        comboBox.ItemsSource = PageSetupDialogPlanner.ResolveChoiceLabels(choices, UiText.Get);
    }

    private void UpdateHeaderFooterPreview()
    {
        HeaderPreviewText.Text = PageSetupDialogModel.BuildHeaderFooterPreview(Header, UiText.Get("PageSetup_None"));
        FooterPreviewText.Text = PageSetupDialogModel.BuildHeaderFooterPreview(Footer, UiText.Get("PageSetup_None"));
    }
}
