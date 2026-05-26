using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal sealed record XlsxHeaderFooterPictureSets(
    WorksheetHeaderFooterPictureSet PageHeader,
    WorksheetHeaderFooterPictureSet PageFooter,
    WorksheetHeaderFooterPictureSet FirstPageHeader,
    WorksheetHeaderFooterPictureSet FirstPageFooter,
    WorksheetHeaderFooterPictureSet EvenPageHeader,
    WorksheetHeaderFooterPictureSet EvenPageFooter)
{
    public static XlsxHeaderFooterPictureSets Empty { get; } = new(
        WorksheetHeaderFooterPictureSet.Empty,
        WorksheetHeaderFooterPictureSet.Empty,
        WorksheetHeaderFooterPictureSet.Empty,
        WorksheetHeaderFooterPictureSet.Empty,
        WorksheetHeaderFooterPictureSet.Empty,
        WorksheetHeaderFooterPictureSet.Empty);
}
