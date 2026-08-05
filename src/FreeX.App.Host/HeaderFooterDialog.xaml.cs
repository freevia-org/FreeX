using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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

    public HeaderFooterEditorState ResultState => new(
        Header,
        Footer,
        FirstPageHeader,
        FirstPageFooter,
        EvenPageHeader,
        EvenPageFooter,
        HeaderPictures.DeepClone(),
        FooterPictures.DeepClone(),
        FirstPageHeaderPictures.DeepClone(),
        FirstPageFooterPictures.DeepClone(),
        EvenPageHeaderPictures.DeepClone(),
        EvenPageFooterPictures.DeepClone(),
        DifferentFirstPage,
        DifferentOddEvenPages,
        ScaleWithDocument,
        AlignWithMargins);

    public HeaderFooterDialog(Sheet sheet)
        : this(HeaderFooterEditorState.FromSheet(sheet))
    {
    }

    public HeaderFooterDialog(HeaderFooterEditorState initial)
    {
        ArgumentNullException.ThrowIfNull(initial);

        InitializeComponent();
        Header = initial.Header;
        Footer = initial.Footer;
        FirstPageHeader = initial.FirstPageHeader;
        FirstPageFooter = initial.FirstPageFooter;
        EvenPageHeader = initial.EvenPageHeader;
        EvenPageFooter = initial.EvenPageFooter;
        HeaderPictures = initial.HeaderPictures.DeepClone();
        FooterPictures = initial.FooterPictures.DeepClone();
        FirstPageHeaderPictures = initial.FirstPageHeaderPictures.DeepClone();
        FirstPageFooterPictures = initial.FirstPageFooterPictures.DeepClone();
        EvenPageHeaderPictures = initial.EvenPageHeaderPictures.DeepClone();
        EvenPageFooterPictures = initial.EvenPageFooterPictures.DeepClone();
        DifferentFirstPage = initial.DifferentFirstPage;
        DifferentOddEvenPages = initial.DifferentOddEvenPages;
        ScaleWithDocument = initial.ScaleWithDocument;
        AlignWithMargins = initial.AlignWithMargins;

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
        _activeTextBox = HeaderCenterBox;
        RefreshOptionalSectionState();
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

    private void HeaderFooterTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, HeaderFooterTabs) || HeaderCenterBox is null || FooterCenterBox is null)
            return;

        if (_activeTextBox is null || !IsActiveTextBoxInSelectedTab(_activeTextBox))
            _activeTextBox = GetDefaultTextBoxForSelectedTab();
        UpdatePictureButtonState();
    }

    private void RefreshOptionalSectionState()
    {
        var firstEnabled = DifferentFirstPageBox.IsChecked == true;
        var evenEnabled = DifferentOddEvenBox.IsChecked == true;
        FirstPageHeaderGroup.Visibility = firstEnabled ? Visibility.Visible : Visibility.Collapsed;
        FirstPageFooterGroup.Visibility = firstEnabled ? Visibility.Visible : Visibility.Collapsed;
        EvenPageHeaderGroup.Visibility = evenEnabled ? Visibility.Visible : Visibility.Collapsed;
        EvenPageFooterGroup.Visibility = evenEnabled ? Visibility.Visible : Visibility.Collapsed;

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

        _activeTextBox = CoerceActiveTextBox(_activeTextBox);
        UpdatePictureButtonState();
    }

    private TextBox GetActiveTextBox()
    {
        _activeTextBox = CoerceActiveTextBox(_activeTextBox);

        return _activeTextBox;
    }

    private TextBox CoerceActiveTextBox(TextBox? candidate)
    {
        if (candidate is null || !IsActiveTextBoxInSelectedTab(candidate))
            return GetDefaultTextBoxForSelectedTab();

        var target = ResolvePictureTarget(candidate);
        var coerced = HeaderFooterEditorPlanner.CoerceToEnabledTarget(
            target,
            DifferentFirstPageBox.IsChecked == true,
            DifferentOddEvenBox.IsChecked == true);

        return GetTextBox(coerced);
    }

    private TextBox GetDefaultTextBoxForSelectedTab() =>
        ReferenceEquals(HeaderFooterTabs.SelectedItem, FooterTab)
            ? FooterCenterBox
            : HeaderCenterBox;

    private bool IsActiveTextBoxInSelectedTab(TextBox textBox) =>
        ReferenceEquals(HeaderFooterTabs.SelectedItem, FooterTab)
            ? IsFooterTextBox(textBox)
            : IsHeaderTextBox(textBox);

    private bool IsHeaderTextBox(TextBox textBox) =>
        ReferenceEquals(textBox, HeaderLeftBox)
        || ReferenceEquals(textBox, HeaderCenterBox)
        || ReferenceEquals(textBox, HeaderRightBox)
        || ReferenceEquals(textBox, FirstHeaderLeftBox)
        || ReferenceEquals(textBox, FirstHeaderCenterBox)
        || ReferenceEquals(textBox, FirstHeaderRightBox)
        || ReferenceEquals(textBox, EvenHeaderLeftBox)
        || ReferenceEquals(textBox, EvenHeaderCenterBox)
        || ReferenceEquals(textBox, EvenHeaderRightBox);

    private bool IsFooterTextBox(TextBox textBox) =>
        ReferenceEquals(textBox, FooterLeftBox)
        || ReferenceEquals(textBox, FooterCenterBox)
        || ReferenceEquals(textBox, FooterRightBox)
        || ReferenceEquals(textBox, FirstFooterLeftBox)
        || ReferenceEquals(textBox, FirstFooterCenterBox)
        || ReferenceEquals(textBox, FirstFooterRightBox)
        || ReferenceEquals(textBox, EvenFooterLeftBox)
        || ReferenceEquals(textBox, EvenFooterCenterBox)
        || ReferenceEquals(textBox, EvenFooterRightBox);
}
