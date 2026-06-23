using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public readonly record struct PictureInsertionSize(double Width, double Height);

/// <summary>
/// Shared picture placement planning. Platform shells own file picking and native image decoding; this
/// planner owns the size fallback and the Core command that places the picture on the worksheet.
/// </summary>
public static class PictureInsertionPlacementPlanner
{
    public const double DefaultPictureWidth = InsertPictureCommandFactory.DefaultWidth;
    public const double DefaultPictureHeight = InsertPictureCommandFactory.DefaultHeight;

    public static PictureInsertionSize DefaultSize { get; } = new(DefaultPictureWidth, DefaultPictureHeight);

    public static PictureInsertionSize? NormalizeSize(double width, double height) =>
        double.IsFinite(width) && width > 0 &&
        double.IsFinite(height) && height > 0
            ? new PictureInsertionSize(width, height)
            : null;

    public static InsertPictureCommand CreateInsertPictureCommand(
        SheetId sheetId,
        CellAddress anchor,
        byte[] imageBytes,
        string contentType,
        PictureInsertionSize? nativeSize = null)
    {
        var size = nativeSize is { } decoded
            ? NormalizeSize(decoded.Width, decoded.Height) ?? DefaultSize
            : DefaultSize;

        return InsertPictureCommandFactory.Build(
            sheetId,
            anchor,
            imageBytes,
            contentType,
            size.Width,
            size.Height);
    }
}
