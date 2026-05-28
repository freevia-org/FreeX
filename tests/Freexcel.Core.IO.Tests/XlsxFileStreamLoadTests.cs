using System.IO;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

/// <summary>
/// Verifies that <see cref="XlsxFileAdapter.Load"/> accepts a seekable
/// <see cref="FileStream"/> directly — i.e. no intermediate byte-array
/// buffering is required before passing the stream to the adapter.
/// </summary>
public sealed class XlsxFileStreamLoadTests
{
    /// <summary>
    /// Saves a workbook to a temporary file, then loads it back through a
    /// <see cref="FileStream"/> (seekable, not a MemoryStream).  The test
    /// verifies that the round-trip succeeds and the cell content is
    /// preserved, confirming that the adapter works without an external
    /// byte-array buffer.
    /// </summary>
    [Fact]
    public void Load_FromFileStream_CompletesWithoutError()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = CreateSimpleWorkbook();

        var tempPath = Path.GetTempFileName();
        try
        {
            // Save to a real file on disk.
            using (var saveStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                adapter.Save(workbook, saveStream);

            // Load back using a FileStream — this is the seekable-stream path
            // that previously required the caller to buffer to byte[] first.
            Workbook loaded;
            using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                loaded = adapter.Load(fs);

            loaded.Should().NotBeNull();
            loaded.Sheets.Should().HaveCount(1);
            var cell = loaded.GetSheetAt(0)!.GetCell(1, 1);
            cell.Should().NotBeNull();
            cell!.Value.Should().Be(new TextValue("Hello"));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Confirms that loading via <see cref="XlsxFileAdapter.LoadWithWarnings"/>
    /// from a <see cref="FileStream"/> produces no warnings for a clean file.
    /// </summary>
    [Fact]
    public void LoadWithWarnings_FromFileStream_ReturnsNoWarnings()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = CreateSimpleWorkbook();

        var tempPath = Path.GetTempFileName();
        try
        {
            using (var saveStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                adapter.Save(workbook, saveStream);

            XlsxLoadResult result;
            using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                result = adapter.LoadWithWarnings(fs);

            result.Workbook.Should().NotBeNull();
            result.Warnings.Should().BeEmpty("a cleanly saved XLSX loaded from FileStream should produce no warnings");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Validates that <see cref="XlsxFileAdapter.Load"/> handles a non-seekable
    /// stream by buffering it internally — the caller must NOT need to do this.
    /// </summary>
    [Fact]
    public void Load_FromNonSeekableStream_CompletesWithoutError()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = CreateSimpleWorkbook();

        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        var bytes = ms.ToArray();

        // Wrap in a non-seekable stream to exercise the buffering fallback path
        using var nonSeekable = new NonSeekableStream(bytes);
        var loaded = adapter.Load(nonSeekable);

        loaded.Should().NotBeNull();
        loaded.Sheets.Should().HaveCount(1);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Workbook CreateSimpleWorkbook()
    {
        var workbook = new Workbook("TestBook");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42));
        return workbook;
    }

    /// <summary>
    /// A stream wrapper that reports <see cref="CanSeek"/> as <c>false</c>,
    /// simulating e.g. a network or pipe stream.
    /// </summary>
    private sealed class NonSeekableStream(byte[] buffer) : Stream
    {
        private readonly MemoryStream _inner = new(buffer, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
