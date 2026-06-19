namespace Free.Shared.Pdf;

/// <summary>
/// Which of the two built-in WinAnsi Helvetica faces a text op draws with. The portable WinAnsi
/// writer maps these to the standard <c>/Helvetica</c> and <c>/Helvetica-Bold</c> Type1 fonts;
/// the Skia writer maps them to a normal / bold system typeface (with per-codepoint fallback).
/// </summary>
public enum PdfFontFace
{
    Regular,
    Bold,
}

/// <summary>
/// One drawing primitive on a content page, expressed in PDF user space (points, origin at the
/// bottom-left, y increasing upward). The set is deliberately small — filled rectangle, stroked
/// rectangle, and a single line of text — because that is the full vocabulary the spreadsheet/
/// document grid exporters need, and it serializes losslessly to both the WinAnsi and Skia backends.
/// </summary>
public abstract record PdfDrawOp;

/// <summary>Fills an axis-aligned rectangle with a solid color.</summary>
public sealed record PdfFillRect(double X, double Y, double Width, double Height, PdfColor Color) : PdfDrawOp;

/// <summary>Strokes the outline of an axis-aligned rectangle with a solid color and line width.</summary>
public sealed record PdfStrokeRect(
    double X,
    double Y,
    double Width,
    double Height,
    PdfColor Color,
    double LineWidth) : PdfDrawOp;

/// <summary>
/// Draws a single run of text. <paramref name="X"/>/<paramref name="Y"/> is the text origin
/// (baseline left) in PDF user space.
/// </summary>
public sealed record PdfText(
    double X,
    double Y,
    double FontSize,
    PdfFontFace Face,
    PdfColor Color,
    string Text) : PdfDrawOp;
