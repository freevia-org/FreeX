// FreeW.PdfRasterize — rasterizes a PDF to PNG (one per page) using the WinRT
// Windows.Data.Pdf API.  No external binaries required.
//
// Usage: FreeW.PdfRasterize <input.pdf> <outDir> [width [height]]
//
// Defaults to each PDF page's 96-DPI geometry; supply width and height to use a fixed surface.
// Output:   <outDir>/<pdfname>_pN.png   (N = 1-based page index)
// When dimensions are omitted, output dimensions follow each PDF page's native 96-DPI size.
// Supply both dimensions only when a fixed output surface is intentional.
//
// Explicit dimensions override the page-derived output surface.

using System.IO;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

if (args.Length < 2 || args.Length == 3 || args.Length > 4)
{
    Console.Error.WriteLine("usage: FreeW.PdfRasterize <input.pdf> <outDir> [width height]");
    return 2;
}

string pdfPath = Path.GetFullPath(args[0]);
string outDir  = Path.GetFullPath(args[1]);
uint? width = null;
uint? height = null;
if (args.Length == 4)
{
    if (!uint.TryParse(args[2], out var parsedWidth) || parsedWidth == 0 ||
        !uint.TryParse(args[3], out var parsedHeight) || parsedHeight == 0)
    {
        Console.Error.WriteLine("width and height must both be positive integers");
        return 2;
    }

    width = parsedWidth;
    height = parsedHeight;
}

if (!File.Exists(pdfPath))
{
    Console.Error.WriteLine($"PDF not found: {pdfPath}");
    return 2;
}

Directory.CreateDirectory(outDir);
string stem = Path.GetFileNameWithoutExtension(pdfPath);

// WinRT async on a plain console thread — use .GetAwaiter().GetResult() via Task.Run
// to marshal off the synchronisation-context-free entry thread.
int exitCode = await Task.Run(async () =>
{
    StorageFile file;
    try
    {
        file = await StorageFile.GetFileFromPathAsync(pdfPath);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Cannot open file: {ex.Message}");
        return 2;
    }

    PdfDocument pdf;
    try
    {
        pdf = await PdfDocument.LoadFromFileAsync(file);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"PdfDocument.LoadFromFileAsync failed: {ex.Message}");
        return 2;
    }

    uint pageCount = pdf.PageCount;
    Console.WriteLine(width is { } fixedWidth
        ? $"PDF has {pageCount} page(s); rendering at {fixedWidth}x{height!.Value} px"
        : $"PDF has {pageCount} page(s); rendering at native page dimensions");

    for (uint i = 0; i < pageCount; i++)
    {
        using PdfPage page = pdf.GetPage(i);
        // Windows.Data.Pdf reports each page in its native 96-DPI geometry. Preserve that
        // geometry per page, then bound it to the shared Word evidence surface. In particular,
        // a landscape section must not inherit the first portrait page's output dimensions.
        const double MaximumEvidenceWidth = 816.0;
        const double MaximumEvidenceHeight = 1056.0;
        var nativeWidth = Math.Max(1.0, page.Size.Width);
        var nativeHeight = Math.Max(1.0, page.Size.Height);
        var nativeScale = Math.Min(1.0, Math.Min(
            MaximumEvidenceWidth / nativeWidth,
            MaximumEvidenceHeight / nativeHeight));
        var outputWidth = width ?? Math.Max(1u, (uint)Math.Floor(nativeWidth * nativeScale));
        var outputHeight = height ?? Math.Max(1u, (uint)Math.Floor(nativeHeight * nativeScale));
        var opts = new PdfPageRenderOptions
        {
            DestinationWidth = outputWidth,
            DestinationHeight = outputHeight
        };
        using var stream   = new InMemoryRandomAccessStream();

        await page.RenderToStreamAsync(stream, opts);

        // Copy WinRT stream → managed byte array → disk file
        stream.Seek(0);
        byte[] bytes = new byte[stream.Size];
        using (var reader = new DataReader(stream))
        {
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
        }

        string outPath = Path.Combine(outDir, $"{stem}_p{i + 1}.png");
        await File.WriteAllBytesAsync(outPath, bytes);
        Console.WriteLine($"ok    {Path.GetFileName(outPath)}");
    }

    Console.WriteLine($"rasterized {pageCount} page(s) into {outDir}");
    return 0;
});

return exitCode;
