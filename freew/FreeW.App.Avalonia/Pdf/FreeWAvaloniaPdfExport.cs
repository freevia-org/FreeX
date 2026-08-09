using FreeW.App.Avalonia.Editing;
using Free.Shared.AppServices.Printing;
using Free.Shared.Pdf;
using Free.Shared.Pdf.Skia;

namespace FreeW.App.Avalonia.Pdf;

/// <summary>Which backend produced the exported PDF bytes.</summary>
public enum FreeWAvaloniaPdfBackend
{
    /// <summary>Unicode-capable Skia/HarfBuzz writer with automatically embedded/subset fonts.</summary>
    Skia,

    /// <summary>Dependency-free WinAnsi (Helvetica) writer used when Skia is unavailable.</summary>
    PortableWinAnsi,
}

/// <summary>Result of an Avalonia FreeW PDF export: the page count plus the backend used.</summary>
public sealed record FreeWAvaloniaPdfExportResult(int PageCount, FreeWAvaloniaPdfBackend Backend);

/// <summary>
/// FreeW's Avalonia (Linux/macOS) PDF export. It mirrors FreeX's Avalonia routing: build the shared
/// app-agnostic <see cref="PdfContentDocument"/> from the editor layout
/// (<see cref="DocumentView.BuildPdfContent"/>) and prefer the Unicode-capable
/// <see cref="SkiaPdfWriter"/> (auto font embedding); fall back to the dependency-free
/// <see cref="PortablePdfWriter"/> (WinAnsi) when the Skia native asset is missing
/// (headless / no-Skia environments).
/// </summary>
public static class FreeWAvaloniaPdfExport
{
    public static FreeWAvaloniaPdfExportResult Save(DocumentView view, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("PDF export requires a writable stream.", nameof(stream));

        var document = view.BuildPdfContent();
        return Write(document, stream);
    }

    public static FreeWAvaloniaPdfExportResult Save(DocumentView view, string path)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        return Save(view, stream);
    }

    public static FreeWAvaloniaPdfExportResult Save(
        DocumentView view,
        string path,
        PrintSelection selection)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(selection);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var document = PrintPdfContentPlanner.Apply(view.BuildPdfContent(), selection);
        return Write(document, stream);
    }

    private static FreeWAvaloniaPdfExportResult Write(PdfContentDocument document, Stream stream)
    {
        // Skia shapes (HarfBuzz) and automatically embeds/subsets the fonts it draws, so non-WinAnsi
        // text exports correctly without bundling a font. When the Skia native asset is missing it
        // throws on first use; we then fall back to the dependency-free WinAnsi writer.
        try
        {
            var pageCount = SkiaPdfWriter.Write(document, stream);
            return new FreeWAvaloniaPdfExportResult(pageCount, FreeWAvaloniaPdfBackend.Skia);
        }
        catch (Exception ex) when (IsSkiaUnavailable(ex))
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
                stream.SetLength(0);
            }

            var bytes = PortablePdfWriter.WriteToBytes(document, "FreeW portable PDF");
            stream.Write(bytes);
            return new FreeWAvaloniaPdfExportResult(document.Pages.Count, FreeWAvaloniaPdfBackend.PortableWinAnsi);
        }
    }

    private static bool IsSkiaUnavailable(Exception ex) =>
        SkiaPdfAvailabilityHelper.IsSkiaUnavailable(ex);
}
