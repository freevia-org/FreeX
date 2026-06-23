using System.Windows;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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
        HeaderPictures = HeaderFooterEditorPlanner.PrunePicturesWithoutTokens(Header, HeaderPictures);
        FooterPictures = HeaderFooterEditorPlanner.PrunePicturesWithoutTokens(Footer, FooterPictures);
        FirstPageHeaderPictures = HeaderFooterEditorPlanner.PrunePicturesWithoutTokens(FirstPageHeader, FirstPageHeaderPictures);
        FirstPageFooterPictures = HeaderFooterEditorPlanner.PrunePicturesWithoutTokens(FirstPageFooter, FirstPageFooterPictures);
        EvenPageHeaderPictures = HeaderFooterEditorPlanner.PrunePicturesWithoutTokens(EvenPageHeader, EvenPageHeaderPictures);
        EvenPageFooterPictures = HeaderFooterEditorPlanner.PrunePicturesWithoutTokens(EvenPageFooter, EvenPageFooterPictures);
        DifferentFirstPage = DifferentFirstPageBox.IsChecked == true;
        DifferentOddEvenPages = DifferentOddEvenBox.IsChecked == true;
        ScaleWithDocument = ScaleWithDocumentBox.IsChecked == true;
        AlignWithMargins = AlignWithMarginsBox.IsChecked == true;
        DialogResult = true;
        Close();
    }
}
