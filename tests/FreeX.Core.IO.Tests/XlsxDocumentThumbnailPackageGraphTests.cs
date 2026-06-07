using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxDocumentThumbnailPackageGraphTests
{
    private const string RootRelationshipsPath = "_rels/.rels";
    private const string ThumbnailRelationshipType =
        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail";

    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void LoadedWorkbookFullSave_PreservesValidThumbnailAndPrunesStaleContentType()
    {
        using var source = CreateWorkbookWithThumbnail(
            thumbnailPart: "docProps/thumbnail.jpeg",
            thumbnailContentType: "image/jpeg",
            includeValidRelationship: true,
            addStaleRelationship: false,
            addWrongExistingOverride: false,
            addStaleOverride: true);
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("full save edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        AssertThumbnailPackageGraph(saved, "docProps/thumbnail.jpeg", "image/jpeg");
        AssertNoStaleThumbnailGraph(saved, "docProps/thumbnail.png");

        saved.Position = 0;
        adapter.Load(saved).GetSheetAt(0).GetValue(2, 2).Should().Be(new TextValue("full save edit"));
    }

    [Fact]
    public void LoadedWorkbookPatchSave_RepairsMissingThumbnailRelationshipAndWrongContentType()
    {
        using var source = CreateWorkbookWithThumbnail(
            thumbnailPart: "docProps/thumbnail.png",
            thumbnailContentType: "application/octet-stream",
            includeValidRelationship: false,
            addStaleRelationship: false,
            addWrongExistingOverride: true,
            addStaleOverride: true);
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        AssertThumbnailPackageGraph(saved, "docProps/thumbnail.png", "image/png");
        AssertNoStaleThumbnailGraph(saved, "docProps/thumbnail.jpeg");

        saved.Position = 0;
        adapter.Load(saved).GetSheetAt(0).GetValue(2, 2).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void NormalizePackage_WithStaleThumbnailRootGraph_RemovesMissingRelationshipAndContentType()
    {
        using var package = CreateWorkbookWithThumbnail(
            thumbnailPart: "docProps/thumbnail.jpeg",
            thumbnailContentType: "image/jpeg",
            includeValidRelationship: true,
            addStaleRelationship: true,
            addWrongExistingOverride: false,
            addStaleOverride: true);

        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxDocumentThumbnailPackageGraphNormalizer.NormalizePackage(archive);
        }

        AssertThumbnailPackageGraph(package, "docProps/thumbnail.jpeg", "image/jpeg");
        AssertNoStaleThumbnailGraph(package, "docProps/thumbnail.png");
        AssertNoStaleThumbnailGraph(package, "docProps/thumbnail-missing.jpeg");
    }

    private static MemoryStream CreateWorkbookWithThumbnail(
        string thumbnailPart,
        string thumbnailContentType,
        bool includeValidRelationship,
        bool addStaleRelationship,
        bool addWrongExistingOverride,
        bool addStaleOverride)
    {
        var workbook = new Workbook("ThumbnailPackageGraph");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("source"));

        var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            AddBinaryPart(archive, thumbnailPart, Encoding.ASCII.GetBytes("thumbnail"));
            var relationshipsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, RootRelationshipsPath);
            if (includeValidRelationship)
            {
                relationshipsXml.Root!.Add(new XElement(
                    PackageRelationshipNs + "Relationship",
                    new XAttribute("Id", "rIdThumbnail"),
                    new XAttribute("Type", ThumbnailRelationshipType),
                    new XAttribute("Target", thumbnailPart)));
            }

            if (addStaleRelationship)
            {
                relationshipsXml.Root!.Add(new XElement(
                    PackageRelationshipNs + "Relationship",
                    new XAttribute("Id", "rIdStaleThumbnail"),
                    new XAttribute("Type", ThumbnailRelationshipType),
                    new XAttribute("Target", "docProps/thumbnail-missing.jpeg")));
            }

            ReplacePackageXml(archive, RootRelationshipsPath, relationshipsXml);

            var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
            if (addWrongExistingOverride)
                AddContentTypeOverride(contentTypesXml, "/" + thumbnailPart, thumbnailContentType);
            else
                AddContentTypeDefault(contentTypesXml, Path.GetExtension(thumbnailPart).TrimStart('.'), thumbnailContentType);

            if (addStaleOverride)
            {
                var stalePart = thumbnailPart.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? "/docProps/thumbnail.jpeg"
                    : "/docProps/thumbnail.png";
                AddContentTypeOverride(contentTypesXml, stalePart, "image/png");
            }

            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);
        }

        package.Position = 0;
        return package;
    }

    private static void AssertThumbnailPackageGraph(Stream package, string thumbnailPart, string contentType)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry(thumbnailPart).Should().NotBeNull();

        var relationshipsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, RootRelationshipsPath);
        var thumbnailRelationships = relationshipsXml.Root!
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(IsThumbnailRelationship)
            .ToList();
        thumbnailRelationships
            .Should()
            .ContainSingle(relationship =>
                (string?)relationship.Attribute("Target") == thumbnailPart &&
                relationship.Attribute("TargetMode") == null);

        var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        HasEffectiveContentType(contentTypesXml, thumbnailPart, contentType).Should().BeTrue();
    }

    private static void AssertNoStaleThumbnailGraph(Stream package, string stalePart)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry(stalePart).Should().BeNull();

        var relationshipsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, RootRelationshipsPath);
        var thumbnailRelationships = relationshipsXml.Root!
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(IsThumbnailRelationship)
            .ToList();
        thumbnailRelationships
            .Should()
            .NotContain(relationship => (string?)relationship.Attribute("Target") == stalePart);

        var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        var contentTypeOverrides = contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .ToList();
        contentTypeOverrides
            .Should()
            .NotContain(element => string.Equals(
                (string?)element.Attribute("PartName"),
                "/" + stalePart,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasEffectiveContentType(XDocument contentTypesXml, string partName, string contentType)
    {
        var overrideContentType = contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .Where(element => string.Equals(
                element.Attribute("PartName")?.Value,
                "/" + partName,
                StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("ContentType")?.Value)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(overrideContentType))
            return string.Equals(overrideContentType, contentType, StringComparison.OrdinalIgnoreCase);

        var extension = Path.GetExtension(partName).TrimStart('.');
        return contentTypesXml.Root!
            .Elements(ContentTypeNs + "Default")
            .Any(element =>
                string.Equals(element.Attribute("Extension")?.Value, extension, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(element.Attribute("ContentType")?.Value, contentType, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsThumbnailRelationship(XElement relationship) =>
        string.Equals(
            relationship.Attribute("Type")?.Value,
            ThumbnailRelationshipType,
            StringComparison.OrdinalIgnoreCase);

    private static void AddBinaryPart(ZipArchive archive, string path, byte[] bytes)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static void AddContentTypeDefault(XDocument contentTypesXml, string extension, string contentType)
    {
        var existing = contentTypesXml.Root!
            .Elements(ContentTypeNs + "Default")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("Extension")?.Value,
                extension,
                StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.SetAttributeValue("ContentType", contentType);
            return;
        }

        contentTypesXml.Root!.Add(new XElement(
            ContentTypeNs + "Default",
            new XAttribute("Extension", extension),
            new XAttribute("ContentType", contentType)));
    }

    private static void AddContentTypeOverride(XDocument contentTypesXml, string partName, string contentType)
    {
        contentTypesXml.Root!.Add(new XElement(
            ContentTypeNs + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));
    }

    private static void ReplacePackageXml(ZipArchive archive, string path, XDocument xml)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        xml.Save(stream, SaveOptions.DisableFormatting);
    }
}
