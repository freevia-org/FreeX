using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 39 (R39-io-custom-xml-docprops-2-1): docProps/core.xml's dcterms:modified and
/// cp:revision must not be frozen forever from the source workbook -- real Excel updates
/// dcterms:modified to the actual save time and increments cp:revision on every save.
/// dcterms:created must remain untouched.
/// </summary>
public sealed class R39_DocPropsModifiedRevisionTests
{
    private const string CoreXmlNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private const string DcNs = "http://purl.org/dc/elements/1.1/";
    private const string DcTermsNs = "http://purl.org/dc/terms/";
    private const string XsiNs = "http://www.w3.org/2001/XMLSchema-instance";

    [Fact]
    public void Preserve_UpdatesModifiedTimestampAndIncrementsRevision_InsteadOfFreezingSourceValues()
    {
        var sourceCoreXml =
            $"""
             <cp:coreProperties xmlns:cp="{CoreXmlNs}" xmlns:dc="{DcNs}" xmlns:dcterms="{DcTermsNs}" xmlns:xsi="{XsiNs}">
               <dc:title>Original Title</dc:title>
               <dcterms:created xsi:type="dcterms:W3CDTF">2018-03-04T12:00:00Z</dcterms:created>
               <dcterms:modified xsi:type="dcterms:W3CDTF">2019-06-01T00:00:00Z</dcterms:modified>
               <cp:lastModifiedBy>John Smith</cp:lastModifiedBy>
               <cp:revision>7</cp:revision>
             </cp:coreProperties>
             """;

        // Simulate a full-rebuild save: the freshly-generated target part starts out as a
        // byte-for-byte copy of the source part (ClosedXML doesn't emit its own
        // docProps/core.xml), so before this fix the modified/revision values would be
        // carried through completely unchanged forever.
        var targetCoreXml = sourceCoreXml;

        using var sourceStream = new MemoryStream();
        using (var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Create, leaveOpen: true))
            WriteEntry(sourceArchive, "docProps/core.xml", sourceCoreXml);

        using var targetStream = new MemoryStream();
        using (var targetArchive = new ZipArchive(targetStream, ZipArchiveMode.Create, leaveOpen: true))
            WriteEntry(targetArchive, "docProps/core.xml", targetCoreXml);

        var saveTimestamp = new DateTimeOffset(2026, 7, 12, 15, 30, 0, TimeSpan.Zero);

        sourceStream.Position = 0;
        targetStream.Position = 0;
        using (var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: true))
        using (var targetArchive = new ZipArchive(targetStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxDocumentPropertiesPreserver.Preserve(sourceArchive, targetArchive, saveTimestamp);
        }

        targetStream.Position = 0;
        using (var resultArchive = new ZipArchive(targetStream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var core = XDocument.Load(resultArchive.GetEntry("docProps/core.xml")!.Open()).Root!;

            // dcterms:modified must reflect the actual save time, not the frozen source value.
            core.Element((XNamespace)DcTermsNs + "modified")!.Value.Should().Be("2026-07-12T15:30:00Z");
            core.Element((XNamespace)DcTermsNs + "modified")!.Value.Should().NotBe("2019-06-01T00:00:00Z");

            // cp:revision must be incremented, not frozen at the source's value.
            core.Element((XNamespace)CoreXmlNs + "revision")!.Value.Should().Be("8");

            // dcterms:created is a stable/frozen fact about the document and must be preserved verbatim.
            core.Element((XNamespace)DcTermsNs + "created")!.Value.Should().Be("2018-03-04T12:00:00Z");
        }
    }

    [Fact]
    public void Preserve_MultipleSequentialSaves_IncrementsRevisionEachTime_NoRegression()
    {
        var sourceCoreXml =
            $"""
             <cp:coreProperties xmlns:cp="{CoreXmlNs}" xmlns:dc="{DcNs}" xmlns:dcterms="{DcTermsNs}" xmlns:xsi="{XsiNs}">
               <dc:title>Original Title</dc:title>
               <cp:revision>1</cp:revision>
             </cp:coreProperties>
             """;

        using var sourceStream = new MemoryStream();
        using (var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Create, leaveOpen: true))
            WriteEntry(sourceArchive, "docProps/core.xml", sourceCoreXml);

        using var targetStream = new MemoryStream();
        using (var targetArchive = new ZipArchive(targetStream, ZipArchiveMode.Create, leaveOpen: true))
            WriteEntry(targetArchive, "docProps/core.xml", sourceCoreXml);

        var firstSave = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

        sourceStream.Position = 0;
        targetStream.Position = 0;
        using (var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: true))
        using (var targetArchive = new ZipArchive(targetStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxDocumentPropertiesPreserver.Preserve(sourceArchive, targetArchive, firstSave);
        }

        targetStream.Position = 0;
        using (var afterFirstSave = new ZipArchive(targetStream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var core = XDocument.Load(afterFirstSave.GetEntry("docProps/core.xml")!.Open()).Root!;
            core.Element((XNamespace)CoreXmlNs + "revision")!.Value.Should().Be("2");
            core.Element((XNamespace)DcTermsNs + "modified")!.Value.Should().Be("2026-01-01T08:00:00Z");
        }

        // Second save: the target from the previous save now becomes the "source" that a
        // subsequent save's package snapshot would be diffed against; revision must keep
        // incrementing rather than resetting or freezing again.
        targetStream.Position = 0;
        var secondSourceBytes = targetStream.ToArray();
        using var secondSourceStream = new MemoryStream(secondSourceBytes);

        using var secondTargetStream = new MemoryStream();
        using (var secondTargetArchive = new ZipArchive(secondTargetStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            targetStream.Position = 0;
            using var priorArchive = new ZipArchive(targetStream, ZipArchiveMode.Read, leaveOpen: true);
            var priorCoreXml = XDocument.Load(priorArchive.GetEntry("docProps/core.xml")!.Open()).ToString();
            WriteEntry(secondTargetArchive, "docProps/core.xml", priorCoreXml);
        }

        var secondSave = new DateTimeOffset(2026, 1, 2, 9, 15, 0, TimeSpan.Zero);
        secondSourceStream.Position = 0;
        secondTargetStream.Position = 0;
        using (var secondSourceArchive = new ZipArchive(secondSourceStream, ZipArchiveMode.Read, leaveOpen: true))
        using (var secondTargetArchive = new ZipArchive(secondTargetStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxDocumentPropertiesPreserver.Preserve(secondSourceArchive, secondTargetArchive, secondSave);
        }

        secondTargetStream.Position = 0;
        using (var afterSecondSave = new ZipArchive(secondTargetStream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var core = XDocument.Load(afterSecondSave.GetEntry("docProps/core.xml")!.Open()).Root!;
            core.Element((XNamespace)CoreXmlNs + "revision")!.Value.Should().Be("3");
            core.Element((XNamespace)DcTermsNs + "modified")!.Value.Should().Be("2026-01-02T09:15:00Z");
        }
    }

    private static void WriteEntry(ZipArchive archive, string path, string xml)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(xml);
    }
}
