namespace Free.Shared.Pdf;

/// <summary>
/// App-agnostic 8-bit-per-channel RGB color used by the shared PDF model. Apps map their own color
/// types (FreeX <c>CellColor</c>, WPF <c>Color</c>, etc.) onto this when supplying draw ops.
/// </summary>
public readonly record struct PdfColor(byte R, byte G, byte B)
{
    public static PdfColor Black => new(0, 0, 0);
}
