namespace Free.Shared.Pdf.Skia;

/// <summary>Identifies the writer that produced a PDF stream.</summary>
public enum PdfExportBackend
{
    /// <summary>Unicode-capable Skia/HarfBuzz writer with automatically embedded/subset fonts.</summary>
    Skia,

    /// <summary>Dependency-free WinAnsi writer used when Skia is unavailable.</summary>
    PortableWinAnsi,
}

/// <summary>Result returned by a preferred-Skia PDF write with portable fallback.</summary>
public sealed record PdfBackendResult<TResult>(TResult Result, PdfExportBackend Backend);

/// <summary>
/// Executes a Skia PDF write and retries with a portable writer when Skia's native assets are
/// unavailable. The delegates keep document construction and result formatting with each caller.
/// </summary>
public static class PdfBackendFallbackExecutor
{
    public static PdfBackendResult<TResult> Execute<TResult>(
        Stream stream,
        Func<Stream, TResult> skiaAttempt,
        Func<Stream, TResult> portableFallback)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(skiaAttempt);
        ArgumentNullException.ThrowIfNull(portableFallback);
        if (!stream.CanWrite)
            throw new ArgumentException("PDF export requires a writable stream.", nameof(stream));

        PrepareForWrite(stream);

        try
        {
            return new PdfBackendResult<TResult>(skiaAttempt(stream), PdfExportBackend.Skia);
        }
        catch (Exception ex) when (SkiaPdfAvailabilityHelper.IsSkiaUnavailable(ex))
        {
            PrepareForWrite(stream);
            return new PdfBackendResult<TResult>(portableFallback(stream), PdfExportBackend.PortableWinAnsi);
        }
    }

    private static void PrepareForWrite(Stream stream)
    {
        if (!stream.CanSeek)
            return;

        stream.Position = 0;
        stream.SetLength(0);
    }
}
