using FreeW.App.Avalonia.Editing;
using Free.Shared.AppServices.Printing;
using Free.Shared.Pdf;
using Free.Shared.Pdf.Skia;

namespace FreeW.App.Avalonia.Pdf;

/// <summary>Result of an Avalonia FreeW PDF export: the page count plus the backend used.</summary>
public sealed record FreeWAvaloniaPdfExportResult(int PageCount, PdfExportBackend Backend);

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

    public static FreeWAvaloniaPdfExportResult Save(
        DocumentView view,
        Stream stream,
        PrintSelection selection)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(selection);
        if (!stream.CanWrite)
            throw new ArgumentException("PDF export requires a writable stream.", nameof(stream));

        return Write(PrintPdfContentPlanner.Apply(view.BuildPdfContent(), selection), stream);
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
        return Save(view, stream, selection);
    }

    private static FreeWAvaloniaPdfExportResult Write(PdfContentDocument document, Stream stream)
    {
        // Skia shapes (HarfBuzz) and automatically embeds/subsets the fonts it draws, so non-WinAnsi
        // text exports correctly without bundling a font. When the Skia native asset is missing it
        // throws on first use; we then fall back to the dependency-free WinAnsi writer.
        var result = PdfBackendFallbackExecutor.Execute(
            stream,
            target => SkiaPdfWriter.Write(document, target),
            target =>
            {
                var bytes = PortablePdfWriter.WriteToBytes(document, "FreeW portable PDF");
                target.Write(bytes);
                return document.Pages.Count;
            });

        return new FreeWAvaloniaPdfExportResult(result.Result, result.Backend);
    }
}
