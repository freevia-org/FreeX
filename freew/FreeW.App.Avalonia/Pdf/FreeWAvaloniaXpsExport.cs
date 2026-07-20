using Free.Shared.Xps;
using FreeW.App.Avalonia.Editing;

namespace FreeW.App.Avalonia.Pdf;

/// <summary>
/// Avalonia adapter for the shared fixed-layout XPS writer. It intentionally does not turn PDF bytes
/// into an <c>.xps</c> file. The current editor model contains text strings but no embeddable XPS font
/// resource, so normal text documents return a precise <see cref="XpsUnsupportedContentException"/>
/// until the renderer supplies that dependency.
/// </summary>
public static class FreeWAvaloniaXpsExport
{
    public static XpsExportabilityReport Analyze(DocumentView view, XpsWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        return PortableXpsWriter.Analyze(view.BuildPdfContent(), options);
    }

    public static void Save(DocumentView view, Stream stream, XpsWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("XPS export requires a writable stream.", nameof(stream));

        var bytes = PortableXpsWriter.WriteToBytes(view.BuildPdfContent(), options);
        stream.Write(bytes);
    }

    public static void Save(DocumentView view, string path, XpsWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        Save(view, stream, options);
    }
}
