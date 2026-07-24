using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Opc;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R83-io-workbook-props-5-1: a source-patch save must not drop docProps/app.xml's
/// AppVersion/LinksUpToDate/SharedDoc/HyperlinksChanged elements when ClosedXML regenerates
/// that part without them.
/// </summary>
public sealed class R83_OpcAppXmlBooleanFlagsRoundTripTests
{
    private const string CoreXmlNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private const string DcNs = "http://purl.org/dc/elements/1.1/";
    private const string DcTermsNs = "http://purl.org/dc/terms/";
    private const string XsiNs = "http://www.w3.org/2001/XMLSchema-instance";
    private const string AppNs = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";

    [Fact]
    public void StableExtendedPropertyElementNames_IncludesAppVersionAndBooleanFlags()
    {
        OpcDocumentProperties.StableExtendedPropertyElementNames
            .Should().Contain(OpcDocumentProperties.ExtendedPropertiesNamespace + "AppVersion");
        OpcDocumentProperties.StableExtendedPropertyElementNames
            .Should().Contain(OpcDocumentProperties.ExtendedPropertiesNamespace + "LinksUpToDate");
        OpcDocumentProperties.StableExtendedPropertyElementNames
            .Should().Contain(OpcDocumentProperties.ExtendedPropertiesNamespace + "SharedDoc");
        OpcDocumentProperties.StableExtendedPropertyElementNames
            .Should().Contain(OpcDocumentProperties.ExtendedPropertiesNamespace + "HyperlinksChanged");
    }

    [Fact]
    public void Preserve_SourcePatchSave_KeepsAppVersionAndBooleanFlags()
    {
        var sourceCoreXml =
            $"""
             <cp:coreProperties xmlns:cp="{CoreXmlNs}" xmlns:dc="{DcNs}" xmlns:dcterms="{DcTermsNs}" xmlns:xsi="{XsiNs}">
               <dc:title>Original Title</dc:title>
             </cp:coreProperties>
             """;
        var sourceAppXml =
            $"""
             <Properties xmlns="{AppNs}">
               <Application>Microsoft Excel</Application>
               <AppVersion>16.0300</AppVersion>
               <LinksUpToDate>true</LinksUpToDate>
               <SharedDoc>true</SharedDoc>
               <HyperlinksChanged>true</HyperlinksChanged>
             </Properties>
             """;

        // Simulate ClosedXML regenerating app.xml on a source-patch save: the freshly-written
        // target part carries the edited Application but omits the fields ClosedXML never
        // models at all (AppVersion/LinksUpToDate/SharedDoc/HyperlinksChanged).
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
            var app = XDocument.Load(resultArchive.GetEntry("docProps/app.xml")!.Open()).Root!;

            // Application was already a preserved element before this fix; it round-trips too.
            app.Element((XNamespace)AppNs + "Application")!.Value.Should().Be("Microsoft Excel");

            app.Element((XNamespace)AppNs + "AppVersion")!.Value.Should().Be("16.0300");
            app.Element((XNamespace)AppNs + "LinksUpToDate")!.Value.Should().Be("true");
            app.Element((XNamespace)AppNs + "SharedDoc")!.Value.Should().Be("true");
            app.Element((XNamespace)AppNs + "HyperlinksChanged")!.Value.Should().Be("true");
        }
    }

    [Fact]
    public void Preserve_SourcePatchSave_LeavesAppXmlUnchangedWhenSourceHasNoBooleanFlags()
    {
        // No-regression sibling: when the source app.xml never had these elements (the common
        // case for a FreeX-authored file), the save must not fabricate them out of thin air.
        var sourceCoreXml =
            $"""
             <cp:coreProperties xmlns:cp="{CoreXmlNs}" xmlns:dc="{DcNs}" xmlns:dcterms="{DcTermsNs}" xmlns:xsi="{XsiNs}">
               <dc:title>Original Title</dc:title>
             </cp:coreProperties>
             """;
        var sourceAppXml =
            $"""
             <Properties xmlns="{AppNs}">
               <Application>FreeX</Application>
             </Properties>
             """;

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
            var app = XDocument.Load(resultArchive.GetEntry("docProps/app.xml")!.Open()).Root!;

            app.Element((XNamespace)AppNs + "Application")!.Value.Should().Be("FreeX");
            app.Element((XNamespace)AppNs + "AppVersion").Should().BeNull();
            app.Element((XNamespace)AppNs + "LinksUpToDate").Should().BeNull();
            app.Element((XNamespace)AppNs + "SharedDoc").Should().BeNull();
            app.Element((XNamespace)AppNs + "HyperlinksChanged").Should().BeNull();
        }
    }

    private static void WriteEntry(ZipArchive archive, string path, string xml)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(xml);
    }
}
