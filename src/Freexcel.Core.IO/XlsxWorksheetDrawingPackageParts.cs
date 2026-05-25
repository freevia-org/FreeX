using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal sealed record XlsxChartPackagePart(XDocument Xml, XDocument? Relationships, string? Name, XlsxDrawingAnchor? Anchor);

internal sealed record XlsxPicturePackagePart(
    byte[] ImageBytes,
    string ContentType,
    string? Name,
    string? Title,
    string? AltText,
    XlsxDrawingAnchor? Anchor,
    double CropLeft,
    double CropTop,
    double CropRight,
    double CropBottom);

internal sealed record XlsxTextBoxPackagePart(
    string Text,
    string? Name,
    string? Title,
    string? AltText,
    XlsxDrawingAnchor? Anchor,
    double RotationDegrees,
    CellColor? FillColor,
    CellColor? OutlineColor,
    WorkbookThemeColorReference? FillThemeColor,
    WorkbookThemeColorReference? OutlineThemeColor);

internal sealed record XlsxShapePackagePart(
    DrawingShapeKind Kind,
    string? Name,
    string? Title,
    string? AltText,
    XlsxDrawingAnchor? Anchor,
    double RotationDegrees,
    CellColor? FillColor,
    CellColor? OutlineColor,
    CellColor? GradientFillEndColor,
    WorkbookThemeColorReference? FillThemeColor,
    WorkbookThemeColorReference? OutlineThemeColor,
    bool HasShadowEffect);

internal sealed record XlsxWorksheetDrawingPackageParts(
    IReadOnlyList<XlsxChartPackagePart> ChartParts,
    IReadOnlyList<XlsxPicturePackagePart> PictureParts,
    IReadOnlyList<XlsxTextBoxPackagePart> TextBoxParts,
    IReadOnlyList<XlsxShapePackagePart> ShapeParts)
{
    public static XlsxWorksheetDrawingPackageParts Empty { get; } = new([], [], [], []);
}

internal sealed record XlsxDrawingAnchor(
    ChartDrawingAnchorKind Kind,
    uint FromRowZeroBased,
    uint FromColumnZeroBased,
    double FromRowOffset,
    double FromColumnOffset,
    double? AbsoluteLeft,
    double? AbsoluteTop,
    uint? ToRowZeroBased,
    uint? ToColumnZeroBased,
    double? ToRowOffset,
    double? ToColumnOffset,
    double? Width,
    double? Height);
