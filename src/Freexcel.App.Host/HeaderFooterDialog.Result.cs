using System.Windows;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class HeaderFooterDialog
{
    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Header = new WorksheetHeaderFooter(
            HeaderLeftBox.Text,
            HeaderCenterBox.Text,
            HeaderRightBox.Text);
        Footer = new WorksheetHeaderFooter(
            FooterLeftBox.Text,
            FooterCenterBox.Text,
            FooterRightBox.Text);
        FirstPageHeader = new WorksheetHeaderFooter(
            FirstHeaderLeftBox.Text,
            FirstHeaderCenterBox.Text,
            FirstHeaderRightBox.Text);
        FirstPageFooter = new WorksheetHeaderFooter(
            FirstFooterLeftBox.Text,
            FirstFooterCenterBox.Text,
            FirstFooterRightBox.Text);
        EvenPageHeader = new WorksheetHeaderFooter(
            EvenHeaderLeftBox.Text,
            EvenHeaderCenterBox.Text,
            EvenHeaderRightBox.Text);
        EvenPageFooter = new WorksheetHeaderFooter(
            EvenFooterLeftBox.Text,
            EvenFooterCenterBox.Text,
            EvenFooterRightBox.Text);
        HeaderPictures = PrunePicturesWithoutTokens(Header, HeaderPictures);
        FooterPictures = PrunePicturesWithoutTokens(Footer, FooterPictures);
        FirstPageHeaderPictures = PrunePicturesWithoutTokens(FirstPageHeader, FirstPageHeaderPictures);
        FirstPageFooterPictures = PrunePicturesWithoutTokens(FirstPageFooter, FirstPageFooterPictures);
        EvenPageHeaderPictures = PrunePicturesWithoutTokens(EvenPageHeader, EvenPageHeaderPictures);
        EvenPageFooterPictures = PrunePicturesWithoutTokens(EvenPageFooter, EvenPageFooterPictures);
        DifferentFirstPage = DifferentFirstPageBox.IsChecked == true;
        DifferentOddEvenPages = DifferentOddEvenBox.IsChecked == true;
        ScaleWithDocument = ScaleWithDocumentBox.IsChecked == true;
        AlignWithMargins = AlignWithMarginsBox.IsChecked == true;
        DialogResult = true;
        Close();
    }
}
