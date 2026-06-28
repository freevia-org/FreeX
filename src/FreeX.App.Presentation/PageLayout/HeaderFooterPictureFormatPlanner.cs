using System.Globalization;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public sealed record HeaderFooterPictureFormatState(
    string FileName,
    string WidthText,
    string HeightText,
    ObjectSizeDialogSize OriginalSize,
    ObjectSizeDialogField InitialFocusField,
    ObjectSizeDialogField FirstInvalidField,
    bool LockAspectRatio);

public static class HeaderFooterPictureFormatPlanner
{
    public static HeaderFooterPictureFormatState CreateState(
        WorksheetHeaderFooterPicture picture,
        string defaultFileName,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(picture);

        var sizeState = ObjectSizeDialogPlanner.CreateState(
            picture.Width,
            picture.Height,
            ObjectSizeDialogField.Width,
            ObjectSizeDialogField.Width,
            culture ?? CultureInfo.InvariantCulture);

        return new HeaderFooterPictureFormatState(
            NormalizeFileName(picture.FileName, defaultFileName),
            sizeState.WidthText,
            sizeState.HeightText,
            sizeState.OriginalSize,
            sizeState.InitialFocusField,
            sizeState.FirstInvalidField,
            sizeState.LockAspectRatio);
    }

    public static string NormalizeFileName(string? fileName, string defaultFileName) =>
        string.IsNullOrWhiteSpace(fileName) ? defaultFileName : fileName.Trim();

    public static WorksheetHeaderFooterPicture NormalizePictureSize(WorksheetHeaderFooterPicture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        var size = ObjectSizeDialogPlanner.NormalizeSize(picture.Width, picture.Height);
        return picture with { Width = size.Width, Height = size.Height };
    }

    public static bool TryCreateResult(
        WorksheetHeaderFooterPicture source,
        string? widthText,
        string? heightText,
        out WorksheetHeaderFooterPicture? result,
        out ObjectSizeDialogField invalidField)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!ObjectSizeDialogPlanner.TryCreateSize(
                widthText,
                heightText,
                ObjectSizeDialogField.Width,
                out var size,
                out invalidField))
        {
            result = null;
            return false;
        }

        result = source with { Width = size.Width, Height = size.Height };
        return true;
    }

    public static ObjectSizeDialogSize ResetSize(HeaderFooterPictureFormatState state) =>
        state.OriginalSize;

    public static double? SyncHeightFromWidth(string? widthText, ObjectSizeDialogSize originalSize) =>
        ObjectSizeDialogPlanner.SyncHeightFromWidth(widthText, originalSize);

    public static double? SyncWidthFromHeight(string? heightText, ObjectSizeDialogSize originalSize) =>
        ObjectSizeDialogPlanner.SyncWidthFromHeight(heightText, originalSize);

    public static double CalculateLockedAspectHeight(double width, double originalWidth, double originalHeight) =>
        ObjectSizeDialogPlanner.CalculateLockedAspectHeight(width, originalWidth, originalHeight);

    public static double CalculateLockedAspectWidth(double height, double originalWidth, double originalHeight) =>
        ObjectSizeDialogPlanner.CalculateLockedAspectWidth(height, originalWidth, originalHeight);

    public static string FormatSize(double value, CultureInfo? culture = null) =>
        ObjectSizeDialogPlanner.FormatSize(value, culture ?? CultureInfo.InvariantCulture);
}
