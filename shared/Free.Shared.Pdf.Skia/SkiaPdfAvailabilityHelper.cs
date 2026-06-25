namespace Free.Shared.Pdf.Skia;

/// <summary>
/// Shared predicate for detecting that the SkiaSharp native asset is unavailable on this
/// platform, used by all Avalonia PDF exporters (FreeX, FreeW, …) to decide whether to
/// fall back to the dependency-free WinAnsi <c>PortablePdfWriter</c>.
/// </summary>
public static class SkiaPdfAvailabilityHelper
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="ex"/> indicates the SkiaSharp
    /// native library (or its HarfBuzz shaper) could not be loaded or initialized on this
    /// platform, signalling that the portable fallback writer should be used instead.
    /// </summary>
    /// <remarks>
    /// Argument/usage errors and export-plan errors are <em>not</em> matched here — those
    /// represent real failures and should propagate to the caller.
    /// </remarks>
    public static bool IsSkiaUnavailable(Exception ex) =>
        ex is DllNotFoundException
            or TypeInitializationException
            or PlatformNotSupportedException
            or EntryPointNotFoundException
            or BadImageFormatException;
}
