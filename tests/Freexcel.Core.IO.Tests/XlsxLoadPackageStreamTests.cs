using System.Reflection;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using Freexcel.Core.IO;

namespace Freexcel.Core.IO.Tests;

public sealed class XlsxLoadPackageStreamTests
{
    [Fact]
    public void StyleOnlyCellStripper_NoOpPackageReturnsSourceWithoutRewritingLargeEntries()
    {
        using var package = CreatePackageWithWorksheet(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" s="1"/>
                  <c r="B1" s="2"/>
                </row>
              </sheetData>
            </worksheet>
            """,
            includeLargePayload: true);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var beforeAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();

        using var stripped = CreateStyleOnlyStrippedPackage(package);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeAllocatedBytes;
        stripped.Should().BeSameAs(package);
        allocatedBytes.Should().BeLessThan(1_000_000);
    }

    [Fact]
    public void StyleOnlyCellStripper_RemovesDuplicateStyleOnlyCellsIntoNewPackage()
    {
        using var package = CreatePackageWithWorksheet(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" s="1"/>
                  <c r="B1" s="1"/>
                  <c r="C1" s="2"/>
                  <c r="D1" s="2"/>
                </row>
              </sheetData>
            </worksheet>
            """,
            includeLargePayload: false);

        using var stripped = CreateStyleOnlyStrippedPackage(package);

        stripped.Should().NotBeSameAs(package);
        ReadWorksheetCellReferences(stripped).Should().Equal("A1", "C1");
    }

    [Fact]
    public void CreateLoadPackageStream_ReusesAccessibleMemoryStreamSliceWithoutOwningSource()
    {
        var buffer = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        using var source = new MemoryStream(buffer, index: 1, count: 6, writable: true, publiclyVisible: true);
        source.Position = 2;

        using var package = CreateLoadPackageStream(source);

        package.Length.Should().Be(4);
        package.Position.Should().Be(4);
        source.Position.Should().Be(6);

        buffer[3] = 42;
        package.Position = 0;
        package.ReadByte().Should().Be(42);

        package.Dispose();
        source.Position = 0;
        source.ReadByte().Should().Be(1);
    }

    [Fact]
    public void CreateLoadPackageStream_CopiesMemoryStreamWhenBufferIsInaccessible()
    {
        var buffer = new byte[] { 1, 2, 3, 4 };
        using var source = new MemoryStream(buffer, writable: true);
        source.Position = 1;

        using var package = CreateLoadPackageStream(source);

        source.Position.Should().Be(source.Length);
        buffer[1] = 42;
        package.Position = 0;
        package.ReadByte().Should().Be(2);
    }

    [Fact]
    public void CreateLoadPackageStream_CopiesNonMemoryStreams()
    {
        var buffer = new byte[] { 1, 2, 3, 4 };
        using var source = new NonMemoryReadStream(buffer);
        source.Position = 1;

        using var package = CreateLoadPackageStream(source);

        source.Position.Should().Be(source.Length);
        buffer[1] = 42;
        package.Position = 0;
        package.ReadByte().Should().Be(2);
    }

    private static MemoryStream CreateLoadPackageStream(Stream stream)
    {
        var method = typeof(XlsxFileAdapter).GetMethod(
            "CreateLoadPackageStream",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        var package = method!.Invoke(null, [stream]).Should().BeOfType<MemoryStream>().Subject;
        return package;
    }

    private static MemoryStream CreateStyleOnlyStrippedPackage(MemoryStream package)
    {
        var type = typeof(XlsxFileAdapter).Assembly.GetType("Freexcel.Core.IO.XlsxClosedXmlStyleOnlyCellStripper");
        type.Should().NotBeNull();
        var method = type!.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();
        var stripped = method!.Invoke(null, [package]).Should().BeOfType<MemoryStream>().Subject;
        return stripped;
    }

    private static MemoryStream CreatePackageWithWorksheet(string worksheetXml, bool includeLargePayload)
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            var worksheetEntry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
            using (var worksheetStream = worksheetEntry.Open())
            using (var writer = new StreamWriter(worksheetStream))
            {
                writer.Write(worksheetXml);
            }

            if (includeLargePayload)
            {
                var payload = archive.CreateEntry("xl/media/payload.bin", CompressionLevel.NoCompression);
                using var payloadStream = payload.Open();
                var buffer = new byte[4 * 1024 * 1024];
                new Random(42).NextBytes(buffer);
                payloadStream.Write(buffer);
            }
        }

        package.Position = 0;
        return package;
    }

    private static IReadOnlyList<string> ReadWorksheetCellReferences(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        using var worksheetStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var worksheet = XDocument.Load(worksheetStream);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var references = worksheet.Descendants(worksheetNs + "c")
            .Select(cell => cell.Attribute("r")!.Value)
            .ToArray();
        package.Position = 0;
        return references;
    }

    private sealed class NonMemoryReadStream(byte[] buffer) : Stream
    {
        private readonly MemoryStream inner = new(buffer, writable: true);

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
