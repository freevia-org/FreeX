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
    public WorksheetHeaderFooterPictureSet HeaderPictures { get; private set; }
    public WorksheetHeaderFooterPictureSet FooterPictures { get; private set; }
    public WorksheetHeaderFooterPictureSet FirstPageHeaderPictures { get; private set; }
    public WorksheetHeaderFooterPictureSet FirstPageFooterPictures { get; private set; }
    public WorksheetHeaderFooterPictureSet EvenPageHeaderPictures { get; private set; }
    public WorksheetHeaderFooterPictureSet EvenPageFooterPictures { get; private set; }
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
        HeaderPictures = sheet.PageHeaderPictures.DeepClone();
        FooterPictures = sheet.PageFooterPictures.DeepClone();
        FirstPageHeaderPictures = sheet.FirstPageHeaderPictures.DeepClone();
        FirstPageFooterPictures = sheet.FirstPageFooterPictures.DeepClone();
        EvenPageHeaderPictures = sheet.EvenPageHeaderPictures.DeepClone();
        EvenPageFooterPictures = sheet.EvenPageFooterPictures.DeepClone();
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
        DifferentFirstPageBox.Checked += (_, _) => RefreshOptionalSectionState();
        DifferentFirstPageBox.Unchecked += (_, _) => RefreshOptionalSectionState();
        DifferentOddEvenBox.Checked += (_, _) => RefreshOptionalSectionState();
        DifferentOddEvenBox.Unchecked += (_, _) => RefreshOptionalSectionState();
        RefreshOptionalSectionState();
        _activeTextBox = HeaderCenterBox;
        UpdatePictureButtonState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void HeaderFooterBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            _activeTextBox = textBox;
            UpdatePictureButtonState();
        }
    }

    private void FocusInitialKeyboardTarget()
    {
        HeaderCenterBox.Focus();
        HeaderCenterBox.SelectAll();
        Keyboard.Focus(HeaderCenterBox);
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

    private void RefreshOptionalSectionState()
    {
        var firstEnabled = DifferentFirstPageBox.IsChecked == true;
        var evenEnabled = DifferentOddEvenBox.IsChecked == true;
        SetControlsEnabled(firstEnabled,
            FirstHeaderLeftBox,
            FirstHeaderCenterBox,
            FirstHeaderRightBox,
            FirstFooterLeftBox,
            FirstFooterCenterBox,
            FirstFooterRightBox);
        SetControlsEnabled(evenEnabled,
            EvenHeaderLeftBox,
            EvenHeaderCenterBox,
            EvenHeaderRightBox,
            EvenFooterLeftBox,
            EvenFooterCenterBox,
            EvenFooterRightBox);

        if (_activeTextBox is not null && !_activeTextBox.IsEnabled)
            _activeTextBox = HeaderCenterBox;
        UpdatePictureButtonState();
    }

}

