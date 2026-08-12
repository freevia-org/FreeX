using System.IO;
using System.IO.Compression;
using Free.Shared.Opc;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// R135: <see cref="DocxReader"/> must reject a package whose declared decompressed size vastly
/// exceeds its on-disk size (a "zip bomb") BEFORE decompressing any part -- the same
/// <see cref="WorkbookOpenSizeGuard"/> the xlsx reader already applies. Without this guard, a
/// tiny crafted .docx can exhaust process memory on open.
/// </summary>
public sealed class DocxReaderSizeGuardTests
{
    [Fact]
    public void Read_RejectsHighRatioPackage_BeforeDecompressing()
    {
        using var bomb = CreateZipBombPackage();

        Action act = () => DocxReader.Read(bomb);

        act.Should().Throw<WorkbookTooLargeException>(
            "a package with an ~1000:1+ declared compression ratio is characteristic of a zip bomb " +
            "and must be rejected before any part is decompressed");
    }

    [Fact]
    public void Read_StillOpensNormalDocument_AfterSizeGuardAdded()
    {
        // Sibling no-regression check: an ordinary small document package (far below the
        // guard's thresholds) must continue to load normally.
        var document = new TextDocument();
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;

        Action act = () => DocxReader.Read(stream);

        act.Should().NotThrow();
    }

    /// <summary>
    /// Builds a minimal but genuine zip archive containing one entry whose real deflate stream
    /// decompresses ~1029x larger than its compressed size (80MB of zeros -> ~80KB compressed),
    /// clearing both the default compression-ratio cap (1000:1) and the ratio-check floor
    /// (64 KB compressed) used by <see cref="WorkbookOpenSizeGuard.EnsureArchiveWithinLimits(ZipArchive,long,double,long)"/>.
    /// </summary>
    private static MemoryStream CreateZipBombPackage()
    {
        const int uncompressedBytes = 80 * 1024 * 1024;
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/media/payload.bin", CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            var buffer = new byte[64 * 1024];
            var remaining = uncompressedBytes;
            while (remaining > 0)
            {
                var chunk = Math.Min(buffer.Length, remaining);
                entryStream.Write(buffer, 0, chunk);
                remaining -= chunk;
            }
        }

        package.Position = 0;
        return package;
    }
}
