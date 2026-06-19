using SharedWinAnsi = Free.Shared.Pdf.PdfWinAnsiTextCapability;

namespace FreeX.App.Services;

/// <summary>
/// FreeX-facing shim over the shared <see cref="Free.Shared.Pdf.PdfWinAnsiTextCapability"/>. The
/// WinAnsi (Helvetica) text-encoding rules now live in the shared PDF tier so FreeX, FreeW, and
/// future apps share one capability surface; this type forwards to them and adapts the diagnostics
/// to FreeX's public <see cref="PortablePdfUnsupportedUnicodeScalar"/> record.
/// </summary>
internal static class PortablePdfWinAnsiTextCapability
{
    public const string DeferredUnicodePdfPathRequirements = SharedWinAnsi.DeferredUnicodePdfPathRequirements;

    public const string UnsupportedUnicodeTextMessage = SharedWinAnsi.UnsupportedUnicodeTextMessage;

    public static string NormalizePdfText(string text) => SharedWinAnsi.NormalizePdfText(text);

    public static string Truncate(string text, int maximumLength) => SharedWinAnsi.Truncate(text, maximumLength);

    public static IReadOnlyList<PortablePdfUnsupportedUnicodeScalar> FindUnsupportedUnicodeScalars(string text) =>
        SharedWinAnsi.FindUnsupportedUnicodeScalars(text)
            .Select(scalar => new PortablePdfUnsupportedUnicodeScalar(scalar.TextIndex, scalar.CodePoint, scalar.TextElement))
            .ToArray();

    public static bool TryEncodeWinAnsiByte(char ch, out byte value) =>
        SharedWinAnsi.TryEncodeWinAnsiByte(ch, out value);
}
