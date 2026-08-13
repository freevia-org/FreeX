using System.Windows;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class HeaderFooterDialog
{
    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var result = new HeaderFooterEditorState(
            new WorksheetHeaderFooter(HeaderLeftBox.Text, HeaderCenterBox.Text, HeaderRightBox.Text),
            new WorksheetHeaderFooter(FooterLeftBox.Text, FooterCenterBox.Text, FooterRightBox.Text),
            new WorksheetHeaderFooter(FirstHeaderLeftBox.Text, FirstHeaderCenterBox.Text, FirstHeaderRightBox.Text),
            new WorksheetHeaderFooter(FirstFooterLeftBox.Text, FirstFooterCenterBox.Text, FirstFooterRightBox.Text),
            new WorksheetHeaderFooter(EvenHeaderLeftBox.Text, EvenHeaderCenterBox.Text, EvenHeaderRightBox.Text),
            new WorksheetHeaderFooter(EvenFooterLeftBox.Text, EvenFooterCenterBox.Text, EvenFooterRightBox.Text),
            HeaderPictures,
            FooterPictures,
            FirstPageHeaderPictures,
            FirstPageFooterPictures,
            EvenPageHeaderPictures,
            EvenPageFooterPictures,
            DifferentFirstPageBox.IsChecked == true,
            DifferentOddEvenBox.IsChecked == true,
            ScaleWithDocumentBox.IsChecked == true,
            AlignWithMarginsBox.IsChecked == true)
            .PrunePicturesWithoutTokens();

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
        ScaleWithDocument = result.ScaleWithDocument;
        AlignWithMargins = result.AlignWithMargins;
        DialogResult = true;
        Close();
    }
}
