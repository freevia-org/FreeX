using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class PptxPackageReaderSourceTests
{
    [Fact]
    public void SmartArtAndDspXmlParsing_UsesSharedOpcXmlLoader()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freep",
            "FreeP.Core.IO",
            "PptxPackageReader.cs"));

        ExtractMethod(source, "private static SmartArtData? ReadSmartArtData(")
            .Should()
            .Contain("OpcXml.LoadXml(")
            .And.NotContain("XDocument.Load(");

        ExtractMethod(source, "private static void ReadDspDrawing(")
            .Should()
            .Contain("OpcXml.LoadXml(")
            .And.NotContain("XDocument.Load(");
    }

    [Fact]
    public void PackageLoadXml_UsesSharedHardenedOpcXmlLoader()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freep",
            "FreeP.Core.IO",
            "PptxPackageReader.cs"));

        ExtractMethod(source, "private static XDocument? LoadXml(")
            .Should()
            .Contain("OpcXml.TryLoadXml(archive, path)")
            .And.NotContain("XDocument.Load(");
    }

    [Fact]
    public void CorePropertiesRead_UsesSharedOpcDocumentPropertiesHelper()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freep",
            "FreeP.Core.IO",
            "PptxPackageReader.cs"));

        ExtractMethod(source, "private static void ReadCoreProperties(")
            .Should()
            .Contain("OpcDocumentProperties.ReadCoreProperties(archive, path)")
            .And.NotContain("Element(")
            .And.NotContain("XDocument.Load(");
    }

    [Fact]
    public void Read_PresentationXmlWithDtd_DoesNotApplyParsedPayload()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml" />
                </Relationships>
                """);

            WriteEntry(archive, "ppt/presentation.xml", """
                <!DOCTYPE p:presentation [ <!ENTITY x "blocked"> ]>
                <p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
                  <p:sldSz cx="9144000" cy="5143500" />
                  <p:sldIdLst>&x;</p:sldIdLst>
                </p:presentation>
                """);
        }

        stream.Position = 0;

        var presentation = PptxPackageReader.Read(stream);

        presentation.SlideSizeCxEmu.Should().Be(new Presentation().SlideSizeCxEmu);
        presentation.SlideSizeCyEmu.Should().Be(new Presentation().SlideSizeCyEmu);
        presentation.Slides.Should().BeEmpty();
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"method '{signature}' should exist");

        var nextMethod = Regex.Match(
            source[(start + signature.Length)..],
            @"\r?\n    (private|internal|public) static ");

        return nextMethod.Success
            ? source[start..(start + signature.Length + nextMethod.Index)]
            : source[start..];
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
