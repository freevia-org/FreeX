using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Opc;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 18 regression coverage: source-patch save must not drop cp:lastPrinted, cp:revision
/// (docProps/core.xml) or app:HyperlinkBase (docProps/app.xml) when ClosedXML regenerates those
/// parts without them.
/// </summary>
public sealed class R18_docprops_Tests
{
    private const string CoreXmlNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private const string DcNs = "http://purl.org/dc/elements/1.1/";
    private const string DcTermsNs = "http://purl.org/dc/terms/";
    private const string XsiNs = "http://www.w3.org/2001/XMLSchema-instance";
    private const string AppNs = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";

    [Fact]
    public void WorkbookStableCorePropertyElementNames_IncludesLastPrintedAndRevision()
    {
        OpcDocumentProperties.WorkbookStableCorePropertyElementNames
            .Should().Contain(OpcDocumentProperties.CorePropertiesNamespace + "lastPrinted");
        OpcDocumentProperties.WorkbookStableCorePropertyElementNames
            .Should().Contain(OpcDocumentProperties.CorePropertiesNamespace + "revision");
    }

    [Fact]
    public void StableExtendedPropertyElementNames_IncludesHyperlinkBase()
    {
        OpcDocumentProperties.StableExtendedPropertyElementNames
            .Should().Contain(OpcDocumentProperties.ExtendedPropertiesNamespace + "HyperlinkBase");
    }

    [Fact]
    public void Preserve_SourcePatchSave_KeepsLastPrintedRevisionAndHyperlinkBase()
    {
        var sourceCoreXml =
            $"""
             <cp:coreProperties xmlns:cp="{CoreXmlNs}" xmlns:dc="{DcNs}" xmlns:dcterms="{DcTermsNs}" xmlns:xsi="{XsiNs}">
               <dc:title>Original Title</dc:title>
               <cp:lastPrinted>2026-01-15T09:30:00Z</cp:lastPrinted>
               <cp:revision>7</cp:revision>
             </cp:coreProperties>
             """;
        var sourceAppXml =
            $"""
             <Properties xmlns="{AppNs}">
               <Application>FreeX</Application>
               <HyperlinkBase>https://example.com/original/</HyperlinkBase>
             </Properties>
             """;

        // Simulate ClosedXML regenerating core.xml/app.xml on a source-patch save: the
        // freshly-written target parts carry the edited title/application but omit the
        // stable-but-uncommon fields that only existed in the original source workbook.
        var targetCoreXml =
            $"""
             <cp:coreProperties xmlns:cp="{CoreXmlNs}" xmlns:dc="{DcNs}" xmlns:dcterms="{DcTermsNs}" xmlns:xsi="{XsiNs}">
               <dc:title>Edited Title</dc:title>
             </cp:coreProperties>
             """;
        var targetAppXml =
            $"""
             <Properties xmlns="{AppNs}">
               <Application>FreeX</Application>
             </Properties>
             """;

        using var sourceStream = new MemoryStream();
        using (var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(sourceArchive, "docProps/core.xml", sourceCoreXml);
            WriteEntry(sourceArchive, "docProps/app.xml", sourceAppXml);
        }

        using var targetStream = new MemoryStream();
        using (var targetArchive = new ZipArchive(targetStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(targetArchive, "docProps/core.xml", targetCoreXml);
            WriteEntry(targetArchive, "docProps/app.xml", targetAppXml);
        }

        sourceStream.Position = 0;
        targetStream.Position = 0;
        using (var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: true))
        using (var targetArchive = new ZipArchive(targetStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxDocumentPropertiesPreserver.Preserve(sourceArchive, targetArchive);
        }

        targetStream.Position = 0;
        using (var resultArchive = new ZipArchive(targetStream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var core = XDocument.Load(resultArchive.GetEntry("docProps/core.xml")!.Open()).Root!;
            var app = XDocument.Load(resultArchive.GetEntry("docProps/app.xml")!.Open()).Root!;

            core.Element((XNamespace)DcNs + "title")!.Value.Should().Be("Edited Title");
            core.Element((XNamespace)CoreXmlNs + "lastPrinted")!.Value.Should().Be("2026-01-15T09:30:00Z");
            // R39-io-custom-xml-docprops-2-1: Excel increments cp:revision on every save
            // rather than freezing the source's value forever.
            core.Element((XNamespace)CoreXmlNs + "revision")!.Value.Should().Be("8");

            app.Element((XNamespace)AppNs + "Application")!.Value.Should().Be("FreeX");
            app.Element((XNamespace)AppNs + "HyperlinkBase")!.Value.Should().Be("https://example.com/original/");
        }
    }

    private static void WriteEntry(ZipArchive archive, string path, string xml)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(xml);
    }
}
