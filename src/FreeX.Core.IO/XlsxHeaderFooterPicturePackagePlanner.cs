using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal enum XlsxHeaderFooterPictureSetKind
{
    PageHeader,
    PageFooter,
    FirstPageHeader,
    FirstPageFooter,
    EvenPageHeader,
    EvenPageFooter
}

internal enum XlsxHeaderFooterPicturePosition
{
    Left,
    Center,
    Right
}

internal sealed record XlsxHeaderFooterPictureSlot(
    string ShapeId,
    XlsxHeaderFooterPictureSetKind Kind,
    XlsxHeaderFooterPicturePosition Position);

internal static class XlsxHeaderFooterPicturePackagePlanner
{
    public static readonly XlsxHeaderFooterPictureSlot[] Slots =
    [
        new("LH", XlsxHeaderFooterPictureSetKind.PageHeader, XlsxHeaderFooterPicturePosition.Left),
        new("CH", XlsxHeaderFooterPictureSetKind.PageHeader, XlsxHeaderFooterPicturePosition.Center),
        new("RH", XlsxHeaderFooterPictureSetKind.PageHeader, XlsxHeaderFooterPicturePosition.Right),
        new("LF", XlsxHeaderFooterPictureSetKind.PageFooter, XlsxHeaderFooterPicturePosition.Left),
        new("CF", XlsxHeaderFooterPictureSetKind.PageFooter, XlsxHeaderFooterPicturePosition.Center),
        new("RF", XlsxHeaderFooterPictureSetKind.PageFooter, XlsxHeaderFooterPicturePosition.Right),
        new("LFH", XlsxHeaderFooterPictureSetKind.FirstPageHeader, XlsxHeaderFooterPicturePosition.Left),
        new("CFH", XlsxHeaderFooterPictureSetKind.FirstPageHeader, XlsxHeaderFooterPicturePosition.Center),
        new("RFH", XlsxHeaderFooterPictureSetKind.FirstPageHeader, XlsxHeaderFooterPicturePosition.Right),
        new("LFF", XlsxHeaderFooterPictureSetKind.FirstPageFooter, XlsxHeaderFooterPicturePosition.Left),
        new("CFF", XlsxHeaderFooterPictureSetKind.FirstPageFooter, XlsxHeaderFooterPicturePosition.Center),
        new("RFF", XlsxHeaderFooterPictureSetKind.FirstPageFooter, XlsxHeaderFooterPicturePosition.Right),
        new("LEH", XlsxHeaderFooterPictureSetKind.EvenPageHeader, XlsxHeaderFooterPicturePosition.Left),
        new("CEH", XlsxHeaderFooterPictureSetKind.EvenPageHeader, XlsxHeaderFooterPicturePosition.Center),
        new("REH", XlsxHeaderFooterPictureSetKind.EvenPageHeader, XlsxHeaderFooterPicturePosition.Right),
        new("LEF", XlsxHeaderFooterPictureSetKind.EvenPageFooter, XlsxHeaderFooterPicturePosition.Left),
        new("CEF", XlsxHeaderFooterPictureSetKind.EvenPageFooter, XlsxHeaderFooterPicturePosition.Center),
        new("REF", XlsxHeaderFooterPictureSetKind.EvenPageFooter, XlsxHeaderFooterPicturePosition.Right)
    ];

    public static bool HasPictures(Sheet sheet) =>
        HasPictures(sheet.PageHeaderPictures) ||
        HasPictures(sheet.PageFooterPictures) ||
        HasPictures(sheet.FirstPageHeaderPictures) ||
        HasPictures(sheet.FirstPageFooterPictures) ||
        HasPictures(sheet.EvenPageHeaderPictures) ||
        HasPictures(sheet.EvenPageFooterPictures);

    public static WorksheetHeaderFooterPictureSet ToSet(
        IReadOnlyDictionary<(XlsxHeaderFooterPictureSetKind Kind, XlsxHeaderFooterPicturePosition Position), WorksheetHeaderFooterPicture> pictures,
        XlsxHeaderFooterPictureSetKind kind) =>
        new(
            pictures.TryGetValue((kind, XlsxHeaderFooterPicturePosition.Left), out var left) ? left : null,
            pictures.TryGetValue((kind, XlsxHeaderFooterPicturePosition.Center), out var center) ? center : null,
            pictures.TryGetValue((kind, XlsxHeaderFooterPicturePosition.Right), out var right) ? right : null);

    public static WorksheetHeaderFooterPicture? GetPicture(
        Sheet sheet,
        XlsxHeaderFooterPictureSetKind kind,
        XlsxHeaderFooterPicturePosition position)
    {
        var set = kind switch
        {
            XlsxHeaderFooterPictureSetKind.PageHeader => sheet.PageHeaderPictures,
            XlsxHeaderFooterPictureSetKind.PageFooter => sheet.PageFooterPictures,
            XlsxHeaderFooterPictureSetKind.FirstPageHeader => sheet.FirstPageHeaderPictures,
            XlsxHeaderFooterPictureSetKind.FirstPageFooter => sheet.FirstPageFooterPictures,
            XlsxHeaderFooterPictureSetKind.EvenPageHeader => sheet.EvenPageHeaderPictures,
            XlsxHeaderFooterPictureSetKind.EvenPageFooter => sheet.EvenPageFooterPictures,
            _ => WorksheetHeaderFooterPictureSet.Empty
        };

        return position switch
        {
            XlsxHeaderFooterPicturePosition.Left => set.Left,
            XlsxHeaderFooterPicturePosition.Center => set.Center,
            XlsxHeaderFooterPicturePosition.Right => set.Right,
            _ => null
        };
    }

    public static bool PictureSetsEqual(XlsxHeaderFooterPictureSets sourcePictures, Sheet sheet) =>
        PictureSetEqual(sourcePictures.PageHeader, sheet.PageHeaderPictures) &&
        PictureSetEqual(sourcePictures.PageFooter, sheet.PageFooterPictures) &&
        PictureSetEqual(sourcePictures.FirstPageHeader, sheet.FirstPageHeaderPictures) &&
        PictureSetEqual(sourcePictures.FirstPageFooter, sheet.FirstPageFooterPictures) &&
        PictureSetEqual(sourcePictures.EvenPageHeader, sheet.EvenPageHeaderPictures) &&
        PictureSetEqual(sourcePictures.EvenPageFooter, sheet.EvenPageFooterPictures);

    public static string GetMediaFileName(string? fileName, int sheetIndex, int pictureIndex, string extension)
    {
        var candidate = Path.GetFileName(fileName ?? "");
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return $"freexHeaderFooter{sheetIndex}_{pictureIndex}{extension}";
        }

        return Path.HasExtension(candidate)
            ? candidate
            : $"{candidate}{extension}";
    }

    public static double? ParseStyleDimension(string? style, string name)
    {
        if (string.IsNullOrWhiteSpace(style))
            return null;

        foreach (var part in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(':', 2, StringSplitOptions.TrimEntries);
            if (pieces.Length != 2 || !string.Equals(pieces[0], name, StringComparison.OrdinalIgnoreCase))
                continue;

            var raw = pieces[1].Trim();
            if (raw.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                raw = raw[..^2];
            else if (raw.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[..^2];
                return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var points)
                    ? points * (96.0 / 72.0)
                    : null;
            }

            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels)
                ? pixels
                : null;
        }

        return null;
    }

    private static bool HasPictures(WorksheetHeaderFooterPictureSet set) =>
        set.Left is not null || set.Center is not null || set.Right is not null;

    private static bool PictureSetEqual(WorksheetHeaderFooterPictureSet left, WorksheetHeaderFooterPictureSet right) =>
        PictureEqual(left.Left, right.Left) &&
        PictureEqual(left.Center, right.Center) &&
        PictureEqual(left.Right, right.Right);

    private static bool PictureEqual(WorksheetHeaderFooterPicture? left, WorksheetHeaderFooterPicture? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return string.Equals(left.ContentType, right.ContentType, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase) &&
               left.Width.Equals(right.Width) &&
               left.Height.Equals(right.Height) &&
               left.ImageBytes.AsSpan().SequenceEqual(right.ImageBytes);
    }
}
