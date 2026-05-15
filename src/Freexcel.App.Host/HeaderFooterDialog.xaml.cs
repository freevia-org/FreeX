using System.Windows;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class HeaderFooterDialog : Window
{
    public WorksheetHeaderFooter Header { get; private set; }
    public WorksheetHeaderFooter Footer { get; private set; }
    public WorksheetHeaderFooter FirstPageHeader { get; private set; }
    public WorksheetHeaderFooter FirstPageFooter { get; private set; }
    public WorksheetHeaderFooter EvenPageHeader { get; private set; }
    public WorksheetHeaderFooter EvenPageFooter { get; private set; }
    public bool DifferentFirstPage { get; private set; }
    public bool DifferentOddEvenPages { get; private set; }
    public bool ScaleWithDocument { get; private set; }
    public bool AlignWithMargins { get; private set; }

    public HeaderFooterDialog(Sheet sheet)
    {
        InitializeComponent();
        Header = sheet.PageHeader;
        Footer = sheet.PageFooter;
        FirstPageHeader = sheet.FirstPageHeader;
        FirstPageFooter = sheet.FirstPageFooter;
        EvenPageHeader = sheet.EvenPageHeader;
        EvenPageFooter = sheet.EvenPageFooter;
        DifferentFirstPage = sheet.DifferentFirstPageHeaderFooter;
        DifferentOddEvenPages = sheet.DifferentOddEvenHeaderFooter;
        ScaleWithDocument = sheet.HeaderFooterScaleWithDocument;
        AlignWithMargins = sheet.HeaderFooterAlignWithMargins;

        HeaderLeftBox.Text = Header.Left;
        HeaderCenterBox.Text = Header.Center;
        HeaderRightBox.Text = Header.Right;
        FooterLeftBox.Text = Footer.Left;
        FooterCenterBox.Text = Footer.Center;
        FooterRightBox.Text = Footer.Right;
        FirstHeaderBox.Text = ToCombinedText(FirstPageHeader);
        FirstFooterBox.Text = ToCombinedText(FirstPageFooter);
        EvenHeaderBox.Text = ToCombinedText(EvenPageHeader);
        EvenFooterBox.Text = ToCombinedText(EvenPageFooter);
        DifferentFirstPageBox.IsChecked = DifferentFirstPage;
        DifferentOddEvenBox.IsChecked = DifferentOddEvenPages;
        ScaleWithDocumentBox.IsChecked = ScaleWithDocument;
        AlignWithMarginsBox.IsChecked = AlignWithMargins;
    }

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
        FirstPageHeader = FromCombinedText(FirstHeaderBox.Text);
        FirstPageFooter = FromCombinedText(FirstFooterBox.Text);
        EvenPageHeader = FromCombinedText(EvenHeaderBox.Text);
        EvenPageFooter = FromCombinedText(EvenFooterBox.Text);
        DifferentFirstPage = DifferentFirstPageBox.IsChecked == true;
        DifferentOddEvenPages = DifferentOddEvenBox.IsChecked == true;
        ScaleWithDocument = ScaleWithDocumentBox.IsChecked == true;
        AlignWithMargins = AlignWithMarginsBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private static string ToCombinedText(WorksheetHeaderFooter value) =>
        string.Join(" | ", new[] { value.Left, value.Center, value.Right });

    private static WorksheetHeaderFooter FromCombinedText(string text)
    {
        var parts = text.Split('|', 3, StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            1 => new WorksheetHeaderFooter("", parts[0], ""),
            2 => new WorksheetHeaderFooter(parts[0], parts[1], ""),
            _ => new WorksheetHeaderFooter(parts[0], parts[1], parts[2])
        };
    }
}
