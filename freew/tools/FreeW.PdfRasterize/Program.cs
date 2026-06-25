// FreeW.PdfRasterize — rasterizes a PDF to PNG (one per page) using the WinRT
// Windows.Data.Pdf API.  No external binaries required.
//
// Usage: FreeW.PdfRasterize <input.pdf> <outDir> [width [height]]
//
// Defaults: width=816, height=1056  (8.5×11 in @ 96 dpi — matches FreeW.FidelityRender)
// Output:   <outDir>/<pdfname>_pN.png   (N = 1-based page index)
//
// The rasterizer sets PdfPageRenderOptions.DestinationWidth/Height so the output
// pixel dimensions are deterministic regardless of the PDF's internal DPI.

using System.IO;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: FreeW.PdfRasterize <input.pdf> <outDir> [width [height]]");
    return 2;
}

string pdfPath = Path.GetFullPath(args[0]);
string outDir  = Path.GetFullPath(args[1]);
uint   width   = args.Length > 2 && uint.TryParse(args[2], out var w) ? w : 816u;
uint   height  = args.Length > 3 && uint.TryParse(args[3], out var h) ? h : 1056u;

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
    Console.WriteLine($"PDF has {pageCount} page(s); rendering at {width}x{height} px");

    var opts = new PdfPageRenderOptions
    {
        DestinationWidth  = width,
        DestinationHeight = height,
    };

    for (uint i = 0; i < pageCount; i++)
    {
        using PdfPage page = pdf.GetPage(i);
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
