using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Freexcel.Core.Model;
using Microsoft.Win32;

namespace Freexcel.App.Host;

public partial class HeaderFooterDialog : Window
{
    private TextBox? _activeTextBox;
    private const string PictureToken = "&[Picture]";

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

    private void PictureButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Insert Picture",
            Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var bytes = File.ReadAllBytes(dialog.FileName);
        var (width, height) = GetImageSize(bytes);
        var picture = new WorksheetHeaderFooterPicture(
            bytes,
            GetContentType(dialog.FileName),
            Path.GetFileName(dialog.FileName),
            width,
            height);
        SetPictureForActiveBox(picture);
        if (!(_activeTextBox ?? HeaderCenterBox).Text.Contains(PictureToken, StringComparison.OrdinalIgnoreCase))
            InsertTokenIntoActiveBox(PictureToken);
        UpdatePictureButtonState();
    }

    private void FormatPictureButton_Click(object sender, RoutedEventArgs e)
    {
        var picture = GetPictureForActiveBox();
        if (picture is null)
        {
            MessageBox.Show(this, "Insert a header or footer picture before formatting it.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new HeaderFooterPictureFormatDialog(picture) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        SetPictureForActiveBox(dialog.Result);
        if (!(_activeTextBox ?? HeaderCenterBox).Text.Contains(PictureToken, StringComparison.OrdinalIgnoreCase))
            InsertTokenIntoActiveBox(PictureToken);
        UpdatePictureButtonState();
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

    private static WorksheetHeaderFooterPictureSet PrunePicturesWithoutTokens(
        WorksheetHeaderFooter text,
        WorksheetHeaderFooterPictureSet pictures) =>
        new(
            ContainsPictureToken(text.Left) ? pictures.Left : null,
            ContainsPictureToken(text.Center) ? pictures.Center : null,
            ContainsPictureToken(text.Right) ? pictures.Right : null);

    private static bool ContainsPictureToken(string text) =>
        text.Contains(PictureToken, StringComparison.OrdinalIgnoreCase);

    private WorksheetHeaderFooterPicture? GetPictureForActiveBox()
    {
        var target = _activeTextBox ?? HeaderCenterBox;
        if (ReferenceEquals(target, HeaderLeftBox)) return HeaderPictures.Left;
        if (ReferenceEquals(target, HeaderCenterBox)) return HeaderPictures.Center;
        if (ReferenceEquals(target, HeaderRightBox)) return HeaderPictures.Right;
        if (ReferenceEquals(target, FooterLeftBox)) return FooterPictures.Left;
        if (ReferenceEquals(target, FooterCenterBox)) return FooterPictures.Center;
        if (ReferenceEquals(target, FooterRightBox)) return FooterPictures.Right;
        if (ReferenceEquals(target, FirstHeaderLeftBox)) return FirstPageHeaderPictures.Left;
        if (ReferenceEquals(target, FirstHeaderCenterBox)) return FirstPageHeaderPictures.Center;
        if (ReferenceEquals(target, FirstHeaderRightBox)) return FirstPageHeaderPictures.Right;
        if (ReferenceEquals(target, FirstFooterLeftBox)) return FirstPageFooterPictures.Left;
        if (ReferenceEquals(target, FirstFooterCenterBox)) return FirstPageFooterPictures.Center;
        if (ReferenceEquals(target, FirstFooterRightBox)) return FirstPageFooterPictures.Right;
        if (ReferenceEquals(target, EvenHeaderLeftBox)) return EvenPageHeaderPictures.Left;
        if (ReferenceEquals(target, EvenHeaderCenterBox)) return EvenPageHeaderPictures.Center;
        if (ReferenceEquals(target, EvenHeaderRightBox)) return EvenPageHeaderPictures.Right;
        if (ReferenceEquals(target, EvenFooterLeftBox)) return EvenPageFooterPictures.Left;
        if (ReferenceEquals(target, EvenFooterCenterBox)) return EvenPageFooterPictures.Center;
        if (ReferenceEquals(target, EvenFooterRightBox)) return EvenPageFooterPictures.Right;
        return null;
    }

    private void UpdatePictureButtonState()
    {
        var target = _activeTextBox ?? HeaderCenterBox;
        var hasPicture = GetPictureForActiveBox() is not null;
        FormatPictureButton.IsEnabled = hasPicture;
        FormatPictureButton.ToolTip = hasPicture
            ? $"Format picture in {ActiveBoxLabel(target)}"
            : $"Insert a picture in {ActiveBoxLabel(target)} before formatting it.";
        PictureTargetStatusText.Text = hasPicture
            ? $"Target: {ActiveBoxLabel(target)} has a picture."
            : $"Target: {ActiveBoxLabel(target)} has no picture.";
    }

    private static string ActiveBoxLabel(TextBox target)
    {
        if (target.Name.EndsWith("LeftBox", StringComparison.Ordinal)) return "left section";
        if (target.Name.EndsWith("CenterBox", StringComparison.Ordinal)) return "center section";
        if (target.Name.EndsWith("RightBox", StringComparison.Ordinal)) return "right section";
        return "current section";
    }

    private void SetPictureForActiveBox(WorksheetHeaderFooterPicture picture)
    {
        var target = _activeTextBox ?? HeaderCenterBox;
        if (ReferenceEquals(target, HeaderLeftBox)) HeaderPictures = HeaderPictures with { Left = picture };
        else if (ReferenceEquals(target, HeaderCenterBox)) HeaderPictures = HeaderPictures with { Center = picture };
        else if (ReferenceEquals(target, HeaderRightBox)) HeaderPictures = HeaderPictures with { Right = picture };
        else if (ReferenceEquals(target, FooterLeftBox)) FooterPictures = FooterPictures with { Left = picture };
        else if (ReferenceEquals(target, FooterCenterBox)) FooterPictures = FooterPictures with { Center = picture };
        else if (ReferenceEquals(target, FooterRightBox)) FooterPictures = FooterPictures with { Right = picture };
        else if (ReferenceEquals(target, FirstHeaderLeftBox)) FirstPageHeaderPictures = FirstPageHeaderPictures with { Left = picture };
        else if (ReferenceEquals(target, FirstHeaderCenterBox)) FirstPageHeaderPictures = FirstPageHeaderPictures with { Center = picture };
        else if (ReferenceEquals(target, FirstHeaderRightBox)) FirstPageHeaderPictures = FirstPageHeaderPictures with { Right = picture };
        else if (ReferenceEquals(target, FirstFooterLeftBox)) FirstPageFooterPictures = FirstPageFooterPictures with { Left = picture };
        else if (ReferenceEquals(target, FirstFooterCenterBox)) FirstPageFooterPictures = FirstPageFooterPictures with { Center = picture };
        else if (ReferenceEquals(target, FirstFooterRightBox)) FirstPageFooterPictures = FirstPageFooterPictures with { Right = picture };
        else if (ReferenceEquals(target, EvenHeaderLeftBox)) EvenPageHeaderPictures = EvenPageHeaderPictures with { Left = picture };
        else if (ReferenceEquals(target, EvenHeaderCenterBox)) EvenPageHeaderPictures = EvenPageHeaderPictures with { Center = picture };
        else if (ReferenceEquals(target, EvenHeaderRightBox)) EvenPageHeaderPictures = EvenPageHeaderPictures with { Right = picture };
        else if (ReferenceEquals(target, EvenFooterLeftBox)) EvenPageFooterPictures = EvenPageFooterPictures with { Left = picture };
        else if (ReferenceEquals(target, EvenFooterCenterBox)) EvenPageFooterPictures = EvenPageFooterPictures with { Center = picture };
        else if (ReferenceEquals(target, EvenFooterRightBox)) EvenPageFooterPictures = EvenPageFooterPictures with { Right = picture };
    }

    private static (double Width, double Height) GetImageSize(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private static string GetContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            _ => "image/png"
        };
}

