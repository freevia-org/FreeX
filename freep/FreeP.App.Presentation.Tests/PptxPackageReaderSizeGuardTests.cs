using System.IO.Compression;
using Free.Shared.Opc;
using FreeP.Core.IO;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// R135: <see cref="PptxPackageReader"/> must reject a package whose declared decompressed
/// size vastly exceeds its on-disk size (a "zip bomb") BEFORE decompressing any part -- the
/// same <see cref="WorkbookOpenSizeGuard"/> the xlsx reader already applies. Without this guard,
/// <c>CapturePackageSnapshot</c> fully decompresses every zip entry into a byte array, so a
/// tiny crafted .pptx can exhaust process memory on open.
/// </summary>
public sealed class PptxPackageReaderSizeGuardTests
{
    [Fact]
    public void Read_RejectsHighRatioPackage_BeforeDecompressing()
    {
        using var bomb = CreateZipBombPackage();

        Action act = () => PptxPackageReader.Read(bomb);

        act.Should().Throw<WorkbookTooLargeException>(
            "a package with an ~1000:1+ declared compression ratio is characteristic of a zip bomb " +
            "and must be rejected before any part is decompressed");
    }

    [Fact]
    public void Read_StillOpensNormalPresentation_AfterSizeGuardAdded()
    {
        // Sibling no-regression check: an ordinary small presentation package (far below the
        // guard's thresholds) must continue to load normally.
        var presentation = PresentationModel.CreateEmpty();
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        Action act = () => PptxPackageReader.Read(stream);

        act.Should().NotThrow();
    }

    /// <summary>
    /// R142: <see cref="PptxPackageReader.Read(Stream)"/> used to unconditionally buffer the whole
    /// raw input into a <see cref="MemoryStream"/> (<c>stream.CopyTo(ms)</c>) before any size check
    /// ran at all -- so a hostile or merely enormous file exhausted memory before the zip-bomb guard
    /// below ever got a chance to reject it. The fix checks the stream's declared length up front
    /// (mirroring <c>XlsxFileAdapter.CreateLoadPackageStream</c>) so an oversized seekable stream is
    /// rejected without ever calling <see cref="Stream.Read(byte[], int, int)"/>.
    /// </summary>
    [Fact]
    public void Read_RejectsStreamWithOversizedDeclaredLength_WithoutBufferingAnyBytes()
    {
        using var oversized = new PathologicalLengthStream(WorkbookOpenSizeGuard.DefaultMaxFileBytes + 1);

        Action act = () => PptxPackageReader.Read(oversized);

        act.Should().Throw<WorkbookTooLargeException>(
            "the declared length must be checked before the file is buffered into memory -- if Read() " +
            "were ever called on this stream it would throw InvalidOperationException instead, proving " +
            "the old code tried to buffer first and check second");
    }

    /// <summary>
    /// Sibling of the guard test above: a non-seekable stream has no declared <see cref="Stream.Length"/>
    /// to pre-check, so it must still be possible to load an ordinary small presentation through the
    /// bounded copy loop alone (the loop must not, say, reject everything or hang).
    /// </summary>
    [Fact]
    public void Read_NonSeekableStream_StillLoadsNormalPresentation()
    {
        var presentation = PresentationModel.CreateEmpty();
        using var seekableBuffer = new MemoryStream();
        PptxPackageWriter.Write(presentation, seekableBuffer);
        using var nonSeekable = new NonSeekableStream(seekableBuffer.ToArray());

        Action act = () => PptxPackageReader.Read(nonSeekable);

        act.Should().NotThrow(
            "a non-seekable source stream must still load correctly through the bounded copy loop, " +
            "even though it has no Length to check up front");
    }

    /// <summary>A seekable stream that reports an oversized <see cref="Length"/> but throws if anything
    /// ever tries to actually read from it -- proves the size check runs strictly before any buffering.</summary>
    private sealed class PathologicalLengthStream(long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length { get; } = length;
        public override long Position { get => _position; set => _position = value; }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException(
                "Read must not be called: the size guard must reject an oversized declared length before any bytes are buffered.");

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => _position = offset;
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Wraps an in-memory buffer as a strictly forward-only, non-seekable stream.</summary>
    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
            var entry = archive.CreateEntry("ppt/media/payload.bin", CompressionLevel.Optimal);
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
