using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Free.Shared.Opc;
using FreeW.Core.IO;
using Xunit;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r401: opening a Strict .docx must be bounded by the same zip-bomb guard as a transitional one.
///
/// <para><c>WorkbookOpenSizeGuard</c> lives in the shared tier and every main package reader calls it
/// -- FreeX's xlsx/ods, FreeW's DocxReader and OdtFileAdapter, FreeP's PptxPackageReader. The strict
/// branch slipped past it: <c>DocxFileAdapter.Load</c> ran <c>StrictOoxmlTransform.IsStrict</c> and
/// <c>RewriteStrictToTransitional</c> FIRST, both of which decompress the package, and only then
/// handed the rewritten result to DocxReader. A guard that runs after the expansion it exists to
/// prevent protects nothing.</para>
///
/// <para>Measured before the fix: an archive this guard rejects outright with
/// <c>WorkbookTooLargeException</c> was rewritten by the strict path without complaint, 408 KB of
/// input expanding through 400 MB of parts in ~100ms. A larger pad is the same code path with a
/// bigger number.</para>
/// </summary>
public sealed class R401_StrictDocxIsBoundedByTheSizeGuardTests
{
    private const string StrictNamespace = "http://purl.oclc.org/ooxml/wordprocessingml/main";

    /// <summary>
    /// A well-formed Strict .docx carrying one hugely compressible part, so the archive exceeds the
    /// guard's compression-ratio ceiling while staying small on disk.
    /// </summary>
    private static byte[] BuildStrictDocxWithCompressionBomb(int padMegabytes)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void WriteText(string name, string content)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }

            WriteText("[Content_Types].xml",
                "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "</Types>");
            WriteText("_rels/.rels",
                "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "</Relationships>");
            WriteText("word/document.xml",
                $"<?xml version=\"1.0\"?><w:document xmlns:w=\"{StrictNamespace}\">" +
                "<w:body><w:p><w:r><w:t>hi</w:t></w:r></w:p></w:body></w:document>");

            var pad = archive.CreateEntry("word/pad.bin", CompressionLevel.SmallestSize);
            using var padStream = pad.Open();
            var chunk = new byte[1024 * 1024];
            for (var i = 0; i < padMegabytes; i++)
                padStream.Write(chunk, 0, chunk.Length);
        }

        return buffer.ToArray();
    }

    [Fact]
    public void TheGuardConsidersThisArchiveABomb()
    {
        // The control. If this ever stops throwing, the test below proves nothing -- it would be
        // asserting that a harmless file is rejected.
        var bytes = BuildStrictDocxWithCompressionBomb(padMegabytes: 400);
        using var stream = new MemoryStream(bytes, writable: false);

        var act = () => WorkbookOpenSizeGuard.EnsureArchiveWithinLimits(stream);

        act.Should().Throw<WorkbookTooLargeException>(
            "the fixture has to exceed the shared guard's limits for the strict-path test to mean anything");
    }

    [Fact]
    public void OpeningItAsStrictIsRefusedBeforeTheRewriteExpandsIt()
    {
        var bytes = BuildStrictDocxWithCompressionBomb(padMegabytes: 400);
        using var stream = new MemoryStream(bytes, writable: false);

        var act = () => DocxFileAdapter.Strict().Load(stream);

        act.Should().Throw<WorkbookTooLargeException>(
            "the strict branch decompresses the package twice before DocxReader's guard runs, so the " +
            "check has to happen at the adapter entry -- otherwise the expansion the guard exists to " +
            "prevent has already happened by the time it is consulted");
    }

    [Fact]
    public void AnOrdinaryStrictDocumentStillOpens()
    {
        // The positive control: a guard that refused every strict file would satisfy the test above.
        var bytes = BuildStrictDocxWithCompressionBomb(padMegabytes: 0);
        using var stream = new MemoryStream(bytes, writable: false);

        var document = DocxFileAdapter.Strict().Load(stream);

        document.Paragraphs.Should().NotBeEmpty("a legitimate strict document must still load");
    }
}
