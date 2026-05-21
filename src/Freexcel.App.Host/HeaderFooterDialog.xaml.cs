using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class HeaderFooterDialog : Window
{
    private TextBox? _activeTextBox;

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
        FirstHeaderLeftBox.Text = FirstPageHeader.Left;
        FirstHeaderCenterBox.Text = FirstPageHeader.Center;
        FirstHeaderRightBox.Text = FirstPageHeader.Right;
        FirstFooterLeftBox.Text = FirstPageFooter.Left;
        FirstFooterCenterBox.Text = FirstPageFooter.Center;
        FirstFooterRightBox.Text = FirstPageFooter.Right;
        EvenHeaderLeftBox.Text = EvenPageHeader.Left;
        EvenHeaderCenterBox.Text = EvenPageHeader.Center;
        EvenHeaderRightBox.Text = EvenPageHeader.Right;
        EvenFooterLeftBox.Text = EvenPageFooter.Left;
        EvenFooterCenterBox.Text = EvenPageFooter.Center;
        EvenFooterRightBox.Text = EvenPageFooter.Right;
        DifferentFirstPageBox.IsChecked = DifferentFirstPage;
        DifferentOddEvenBox.IsChecked = DifferentOddEvenPages;
        ScaleWithDocumentBox.IsChecked = ScaleWithDocument;
        AlignWithMarginsBox.IsChecked = AlignWithMargins;
        _activeTextBox = HeaderCenterBox;
    }

    public static string InsertToken(string text, int caretIndex, string token)
    {
        var boundedCaretIndex = Math.Clamp(caretIndex, 0, text.Length);
        return text.Insert(boundedCaretIndex, token);
    }

    private void HeaderFooterBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
            _activeTextBox = textBox;
    }

    private void InsertTokenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string token })
            return;

        InsertTokenIntoActiveBox(token);
    }

    private void HeaderPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyPreset(HeaderCenterBox, HeaderPresetBox.SelectedItem);
    }

    private void FooterPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyPreset(FooterCenterBox, FooterPresetBox.SelectedItem);
    }

    private static void ApplyPreset(TextBox target, object? selectedItem)
    {
        if (selectedItem is not ComboBoxItem { Tag: string preset })
            return;

        target.Text = preset;
        target.CaretIndex = target.Text.Length;
        target.Focus();
    }

    private void InsertTokenIntoActiveBox(string token)
    {
        var target = _activeTextBox ?? HeaderCenterBox;
        var caretIndex = target.CaretIndex;
        target.Text = InsertToken(target.Text, caretIndex, token);
        target.CaretIndex = caretIndex + token.Length;
        target.Focus();
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
        DifferentFirstPage = DifferentFirstPageBox.IsChecked == true;
        DifferentOddEvenPages = DifferentOddEvenBox.IsChecked == true;
        ScaleWithDocument = ScaleWithDocumentBox.IsChecked == true;
        AlignWithMargins = AlignWithMarginsBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

}
