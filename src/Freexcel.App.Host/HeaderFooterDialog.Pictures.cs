using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Freexcel.Core.Model;
using Microsoft.Win32;

namespace Freexcel.App.Host;

public partial class HeaderFooterDialog
{
    private const string PictureToken = "&[Picture]";

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
