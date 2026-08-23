using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxCorePropertiesSaveMetadataTests
{
    private static readonly XNamespace CorePropertiesNamespace =
        "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace DublinCoreTermsNamespace = "http://purl.org/dc/terms/";
    private static readonly XNamespace XmlSchemaInstanceNamespace = "http://www.w3.org/2001/XMLSchema-instance";

    [Theory]
    [InlineData(null, "1")]
    [InlineData("not-a-number", "1")]
    [InlineData("7", "8")]
    public void UpdateCorePropertiesOnSave_UsesUtcAndIncrementsRevision(string? revision, string expectedRevision)
    {
        using var package = CreatePackage(CorePropertiesXml(revision));
        var saveTimestamp = new DateTimeOffset(2026, 8, 23, 15, 30, 45, TimeSpan.FromHours(3));

        Update(package, archive =>
            XlsxDocumentPropertiesPreserver.UpdateCorePropertiesOnSave(archive, saveTimestamp));

        var core = ReadXml(package, "docProps/core.xml").Root!;
        var modified = core.Element(DublinCoreTermsNamespace + "modified")!;
        modified.Value.Should().Be("2026-08-23T12:30:45Z");
        modified.Attribute(XmlSchemaInstanceNamespace + "type")!.Value.Should().Be("dcterms:W3CDTF");
        core.GetNamespaceOfPrefix("dcterms").Should().Be(DublinCoreTermsNamespace);
        core.Element(CorePropertiesNamespace + "revision")!.Value.Should().Be(expectedRevision);
        core.Element(CorePropertiesNamespace + "lastModifiedBy")!.Value.Should().Be("Original Author");
    }

    [Fact]
    public void UpdateCorePropertiesOnSave_LeavesPackageGraphAndUnrelatedPartsUntouched()
    {
        const string contentTypes = "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/></Types>";
        const string relationships = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/></Relationships>";
        using var package = CreatePackage(
            CorePropertiesXml("4"),
            ("[Content_Types].xml", contentTypes),
            ("_rels/.rels", relationships),
            ("xl/workbook.xml", "<workbook/>"));

        Update(package, archive => XlsxDocumentPropertiesPreserver.UpdateCorePropertiesOnSave(
            archive,
            new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero)));

        ReadText(package, "[Content_Types].xml").Should().Be(contentTypes);
        ReadText(package, "_rels/.rels").Should().Be(relationships);
        ReadText(package, "xl/workbook.xml").Should().Be("<workbook/>");
    }

    [Fact]
    public void UpdateCorePropertiesOnSave_MissingPartIsNoOp()
    {
        using var package = CreatePackage(null, ("xl/workbook.xml", "<workbook/>"));
        var before = package.ToArray();

        Update(package, archive => XlsxDocumentPropertiesPreserver.UpdateCorePropertiesOnSave(
            archive,
            DateTimeOffset.UtcNow));

        package.ToArray().Should().Equal(before);
    }

    [Fact]
    public void UpdateCorePropertiesOnSave_CorruptPartThrowsWithoutReplacingIt()
    {
        const string corruptCore = "<cp:coreProperties";
        using var package = CreatePackage(corruptCore);

        var action = () => Update(package, archive =>
            XlsxDocumentPropertiesPreserver.UpdateCorePropertiesOnSave(archive, DateTimeOffset.UtcNow));

        action.Should().Throw<System.Xml.XmlException>();
        ReadText(package, "docProps/core.xml").Should().Be(corruptCore);
    }

    [Fact]
    public void FullAndPatchSavePaths_AdoptSharedUpdater()
    {
        var root = FindRepositoryRoot();
        var preserver = File.ReadAllText(Path.Combine(root, "src", "FreeX.Core.IO", "XlsxDocumentPropertiesPreserver.cs"));
        var patchPath = File.ReadAllText(Path.Combine(root, "src", "FreeX.Core.IO", "XlsxFileAdapter.SourcePackageSnapshot.cs"));

        preserver.Should().Contain("UpdateCorePropertiesOnSave(targetArchive");
        patchPath.Should().Contain("XlsxDocumentPropertiesPreserver.UpdateCorePropertiesOnSave(archive");
        patchPath.Should().NotContain("UpdatePatchedDocumentPropertiesOnSave");
    }

    private static string CorePropertiesXml(string? revision)
    {
        var revisionElement = revision is null
            ? string.Empty
            : $"<cp:revision>{revision}</cp:revision>";
        return $"<cp:coreProperties xmlns:cp=\"{CorePropertiesNamespace}\"><cp:lastModifiedBy>Original Author</cp:lastModifiedBy>{revisionElement}</cp:coreProperties>";
    }

    private static MemoryStream CreatePackage(string? coreXml, params (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (coreXml is not null)
                WriteEntry(archive, "docProps/core.xml", coreXml);
            foreach (var (path, content) in entries)
                WriteEntry(archive, path, content);
        }

        stream.Position = 0;
        return stream;
    }

    private static void Update(MemoryStream package, Action<ZipArchive> update)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        update(archive);
    }

    private static XDocument ReadXml(MemoryStream package, string path)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        using var entry = archive.GetEntry(path)!.Open();
        return XDocument.Load(entry);
    }

    private static string ReadText(MemoryStream package, string path)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry(path)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the FreeX repository root.");
    }
}
