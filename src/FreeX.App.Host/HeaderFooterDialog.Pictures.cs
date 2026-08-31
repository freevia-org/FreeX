using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class HeaderFooterDialog
{
    private async void PictureButton_Click(object sender, RoutedEventArgs e)
    {
        var result = WpfFileDialogService.ShowOpenDialog(
            this,
            UiText.Get("HeaderFooterPicture_OpenFileFilter"),
            title: UiText.Get("HeaderFooterPicture_InsertPictureTitle"));
        if (!result.Chosen)
            return;

        // Round 172 (freep-media F1 follow-up): reject an unsupported picture instead of falling back
        // to image/png. HeaderFooterPicture_OpenFileFilter offers "All files (*.*)", so a .wmf/.emf can
        // be chosen here; XlsxHeaderFooterPicturePackageWriter names the media part from this stored
        // content type (OpcMediaTypes.GetImageExtension), so the old `?? "image/png"` wrote metafile
        // bytes into a part called .png and declared image/png -- self-consistent but undecodable.
        var contentType = InsertPictureCommandFactory.ContentTypeForPath(result.FileName!);
        if (contentType is null)
        {
            DialogMessageHelper.ShowInfo(this, UiText.Get("InsertLoc_UnsupportedImageFormat"), Title);
            FocusActiveTextBox();
            return;
        }

        var readResult = await FileByteReadWorkflow.ReadLocalPathAsync(result.FileName!);
        if (readResult.Outcome == FileByteReadOutcome.Canceled)
            return;
        if (!readResult.IsReadable)
        {
            ShowPictureOpenFailure(readResult.FailureMessage);
            return;
        }

        var bytes = readResult.Bytes;
        double width;
        double height;
        try
        {
            (width, height) = GetImageSize(bytes);
        }
        catch (Exception ex)
        {
            // This is an `async void` handler, so an unreadable or undecodable file (corrupt,
            // truncated, zero-byte, wrong extension, locked, or removed between the picker and the
            // read) would otherwise escape as an unhandled exception and crash the app. The Insert
            // Picture ribbon command already degrades this way; match it here.
            ShowPictureOpenFailure(ex.Message);
            return;
        }

        var picture = new WorksheetHeaderFooterPicture(
            bytes,
            contentType,
            Path.GetFileName(result.FileName!),
            width,
            height);
        SetPictureForActiveBox(picture);
        if (!HeaderFooterEditorPlanner.ContainsPictureToken(GetActiveTextBox().Text))
            InsertTokenIntoActiveBox(HeaderFooterEditorPlanner.PictureToken);
        UpdatePictureButtonState();
    }

    private void FormatPictureButton_Click(object sender, RoutedEventArgs e)
    {
        var picture = GetPictureForActiveBox();
        if (picture is null)
        {
            DialogMessageHelper.ShowInfo(this, UiText.Get("HeaderFooterPicture_InsertBeforeFormattingMessage"), Title);
            FocusActiveTextBox();
            return;
        }

        var dialog = new HeaderFooterPictureFormatDialog(picture) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        SetPictureForActiveBox(dialog.Result);
        if (!HeaderFooterEditorPlanner.ContainsPictureToken(GetActiveTextBox().Text))
            InsertTokenIntoActiveBox(HeaderFooterEditorPlanner.PictureToken);
        UpdatePictureButtonState();
    }

    private WorksheetHeaderFooterPicture? GetPictureForActiveBox()
    {
        var target = ResolvePictureTarget(GetActiveTextBox());
        return HeaderFooterEditorPlanner.GetPicture(GetPictureSet(target.Scope), target.Section);
    }

    private void UpdatePictureButtonState()
    {
        var target = GetActiveTextBox();
        var hasPicture = GetPictureForActiveBox() is not null;
        FormatPictureButton.IsEnabled = hasPicture;
        FormatPictureButton.ToolTip = hasPicture
            ? UiText.Format("HeaderFooterPicture_FormatPictureToolTip", ActiveBoxLabel(target))
            : UiText.Format("HeaderFooterPicture_InsertBeforeFormattingToolTip", ActiveBoxLabel(target));
        PictureTargetStatusText.Text = hasPicture
            ? UiText.Format("HeaderFooterPicture_TargetHasPictureStatus", ActiveBoxLabel(target))
            : UiText.Format("HeaderFooterPicture_TargetHasNoPictureStatus", ActiveBoxLabel(target));
    }

    private void FocusActiveTextBox()
    {
        var target = GetActiveTextBox();
        target.Focus();
        Keyboard.Focus(target);
    }

    private static string ActiveBoxLabel(TextBox target)
    {
        var editorTarget = ResolvePictureTarget(target);
        var scope = UiText.Get(HeaderFooterEditorPlanner.ScopeLabelResourceKey(editorTarget.Scope));
        var section = UiText.Get(HeaderFooterEditorPlanner.SectionLabelResourceKey(editorTarget.Section));
        return HeaderFooterEditorPlanner.ComposeTargetLabel(scope, section, UiText.Format);
    }

    private void SetPictureForActiveBox(WorksheetHeaderFooterPicture picture)
    {
        var target = ResolvePictureTarget(GetActiveTextBox());
        SetPictureSet(
            target.Scope,
            HeaderFooterEditorPlanner.SetPicture(GetPictureSet(target.Scope), target.Section, picture));
    }

    private WorksheetHeaderFooterPictureSet GetPictureSet(HeaderFooterEditorScope scope) =>
        scope switch
        {
            HeaderFooterEditorScope.Footer => FooterPictures,
            HeaderFooterEditorScope.FirstPageHeader => FirstPageHeaderPictures,
            HeaderFooterEditorScope.FirstPageFooter => FirstPageFooterPictures,
            HeaderFooterEditorScope.EvenPageHeader => EvenPageHeaderPictures,
            HeaderFooterEditorScope.EvenPageFooter => EvenPageFooterPictures,
            _ => HeaderPictures
        };

    private void SetPictureSet(HeaderFooterEditorScope scope, WorksheetHeaderFooterPictureSet pictures)
    {
        switch (scope)
        {
            case HeaderFooterEditorScope.Footer:
                FooterPictures = pictures;
                break;
            case HeaderFooterEditorScope.FirstPageHeader:
                FirstPageHeaderPictures = pictures;
                break;
            case HeaderFooterEditorScope.FirstPageFooter:
                FirstPageFooterPictures = pictures;
                break;
            case HeaderFooterEditorScope.EvenPageHeader:
                EvenPageHeaderPictures = pictures;
                break;
            case HeaderFooterEditorScope.EvenPageFooter:
                EvenPageFooterPictures = pictures;
                break;
            default:
                HeaderPictures = pictures;
                break;
        }
    }

    private static HeaderFooterEditorTarget ResolvePictureTarget(TextBox target) =>
        target.Name switch
        {
            "HeaderLeftBox" => new(HeaderFooterEditorScope.Header, HeaderFooterEditorSection.Left),
            "HeaderRightBox" => new(HeaderFooterEditorScope.Header, HeaderFooterEditorSection.Right),
            "FooterLeftBox" => new(HeaderFooterEditorScope.Footer, HeaderFooterEditorSection.Left),
            "FooterCenterBox" => new(HeaderFooterEditorScope.Footer, HeaderFooterEditorSection.Center),
            "FooterRightBox" => new(HeaderFooterEditorScope.Footer, HeaderFooterEditorSection.Right),
            "FirstHeaderLeftBox" => new(HeaderFooterEditorScope.FirstPageHeader, HeaderFooterEditorSection.Left),
            "FirstHeaderCenterBox" => new(HeaderFooterEditorScope.FirstPageHeader, HeaderFooterEditorSection.Center),
            "FirstHeaderRightBox" => new(HeaderFooterEditorScope.FirstPageHeader, HeaderFooterEditorSection.Right),
            "FirstFooterLeftBox" => new(HeaderFooterEditorScope.FirstPageFooter, HeaderFooterEditorSection.Left),
            "FirstFooterCenterBox" => new(HeaderFooterEditorScope.FirstPageFooter, HeaderFooterEditorSection.Center),
            "FirstFooterRightBox" => new(HeaderFooterEditorScope.FirstPageFooter, HeaderFooterEditorSection.Right),
            "EvenHeaderLeftBox" => new(HeaderFooterEditorScope.EvenPageHeader, HeaderFooterEditorSection.Left),
            "EvenHeaderCenterBox" => new(HeaderFooterEditorScope.EvenPageHeader, HeaderFooterEditorSection.Center),
            "EvenHeaderRightBox" => new(HeaderFooterEditorScope.EvenPageHeader, HeaderFooterEditorSection.Right),
            "EvenFooterLeftBox" => new(HeaderFooterEditorScope.EvenPageFooter, HeaderFooterEditorSection.Left),
            "EvenFooterCenterBox" => new(HeaderFooterEditorScope.EvenPageFooter, HeaderFooterEditorSection.Center),
            "EvenFooterRightBox" => new(HeaderFooterEditorScope.EvenPageFooter, HeaderFooterEditorSection.Right),
            _ => new(HeaderFooterEditorScope.Header, HeaderFooterEditorSection.Center)
        };

    private TextBox GetTextBox(HeaderFooterEditorTarget target) =>
        target switch
        {
            { Scope: HeaderFooterEditorScope.Header, Section: HeaderFooterEditorSection.Left } => HeaderLeftBox,
            { Scope: HeaderFooterEditorScope.Header, Section: HeaderFooterEditorSection.Right } => HeaderRightBox,
            { Scope: HeaderFooterEditorScope.Footer, Section: HeaderFooterEditorSection.Left } => FooterLeftBox,
            { Scope: HeaderFooterEditorScope.Footer, Section: HeaderFooterEditorSection.Center } => FooterCenterBox,
            { Scope: HeaderFooterEditorScope.Footer, Section: HeaderFooterEditorSection.Right } => FooterRightBox,
            { Scope: HeaderFooterEditorScope.FirstPageHeader, Section: HeaderFooterEditorSection.Left } => FirstHeaderLeftBox,
            { Scope: HeaderFooterEditorScope.FirstPageHeader, Section: HeaderFooterEditorSection.Center } => FirstHeaderCenterBox,
            { Scope: HeaderFooterEditorScope.FirstPageHeader, Section: HeaderFooterEditorSection.Right } => FirstHeaderRightBox,
            { Scope: HeaderFooterEditorScope.FirstPageFooter, Section: HeaderFooterEditorSection.Left } => FirstFooterLeftBox,
            { Scope: HeaderFooterEditorScope.FirstPageFooter, Section: HeaderFooterEditorSection.Center } => FirstFooterCenterBox,
            { Scope: HeaderFooterEditorScope.FirstPageFooter, Section: HeaderFooterEditorSection.Right } => FirstFooterRightBox,
            { Scope: HeaderFooterEditorScope.EvenPageHeader, Section: HeaderFooterEditorSection.Left } => EvenHeaderLeftBox,
            { Scope: HeaderFooterEditorScope.EvenPageHeader, Section: HeaderFooterEditorSection.Center } => EvenHeaderCenterBox,
            { Scope: HeaderFooterEditorScope.EvenPageHeader, Section: HeaderFooterEditorSection.Right } => EvenHeaderRightBox,
            { Scope: HeaderFooterEditorScope.EvenPageFooter, Section: HeaderFooterEditorSection.Left } => EvenFooterLeftBox,
            { Scope: HeaderFooterEditorScope.EvenPageFooter, Section: HeaderFooterEditorSection.Center } => EvenFooterCenterBox,
            { Scope: HeaderFooterEditorScope.EvenPageFooter, Section: HeaderFooterEditorSection.Right } => EvenFooterRightBox,
            _ => HeaderCenterBox
        };

    private static (double Width, double Height) GetImageSize(byte[] bytes)
    {
        // Convert native pixel dimensions to the app's device-independent 1/96-inch unit
        // convention (matching the ordinary Insert>Picture path's ImageDimensionDecoder), rather
        // than storing raw pixel counts as if they were already DIP units. Storing raw pixels
        // verbatim treated e.g. a 4032x3024px photo as 4032x3024 DIP units (42in x 31.5in),
        // ballooning the header/footer band far beyond the page in WorksheetPrintPageContentPlanner.
        var decoded = ImageDimensionDecoder.Decode(bytes);
        return (decoded.Width, decoded.Height);
    }

    private void ShowPictureOpenFailure(string detail)
    {
        var presentation = PageLayoutMessagePresentationCatalog
            .DescribeHeaderFooterPictureOpenFailure(detail)
            .Resolve(UiText.Get, UiText.Format);
        DialogMessageHelper.ShowMessage(
            this,
            presentation.Message,
            presentation.Title,
            presentation.Buttons,
            presentation.Kind);
    }
}
