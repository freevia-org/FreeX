using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.Services;

namespace FreeX.App.Host;

internal static class InsertObjectPlacementPlanner
{
    public const double DefaultPictureWidth = InsertPictureCommandFactory.DefaultWidth;
    public const double DefaultPictureHeight = InsertPictureCommandFactory.DefaultHeight;

    public static InsertPictureCommand CreateInsertPictureCommand(
        SheetId sheetId,
        CellAddress anchor,
        byte[] imageBytes,
        string contentType)
    {
        var size = GetPictureSize(imageBytes);
        return InsertPictureCommandFactory.Build(
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
