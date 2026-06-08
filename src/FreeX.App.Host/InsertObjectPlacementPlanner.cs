using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class InsertObjectPlacementPlanner
{
    public const double DefaultPictureWidth = 240d;
    public const double DefaultPictureHeight = 140d;

    public static InsertPictureCommand CreateInsertPictureCommand(
        SheetId sheetId,
        CellAddress anchor,
        byte[] imageBytes,
        string contentType)
    {
        var size = GetPictureSize(imageBytes);
        return new InsertPictureCommand(
            sheetId,
            anchor,
            imageBytes,
            contentType,
            size.Width,
            size.Height);
    }

    public static DecodedImageDimensions GetPictureSize(byte[]? imageBytes) =>
        ImageDimensionDecoder.TryDecode(imageBytes, out var decoded)
            ? decoded
            : new DecodedImageDimensions(DefaultPictureWidth, DefaultPictureHeight);
}
