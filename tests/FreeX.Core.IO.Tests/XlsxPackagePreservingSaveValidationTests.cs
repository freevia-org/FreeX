using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxPackagePreservingSaveValidationTests
{
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void LoadEditSave_PreservesUnknownPackagePartsContentTypesAndRelationships()
    {
        var sourceBytes = CreatePackageWithUnknownPackageGraph("Data");

        var savedBytes = SaveAfterLoadingAndEditing(sourceBytes, workbook =>
        {
            var sheet = workbook.GetSheet("Data");
            sheet.Should().NotBeNull();
            sheet!.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("FreeX edit"));
        });

        AssertRoundTripCellValue(savedBytes, "Data", 2, 2, new TextValue("FreeX edit"));

        using var savedPackage = new MemoryStream(savedBytes);
        using var savedArchive = new ZipArchive(savedPackage, ZipArchiveMode.Read);

        AssertEntryBytesPreserved(sourceBytes, savedBytes, "xl/customPayload/item1.xml");
        AssertEntryBytesPreserved(sourceBytes, savedBytes, "xl/customPayload/item2.freexbin");
        AssertEntryBytesPreserved(sourceBytes, savedBytes, "xl/customPayload/sheet-note.xml");
        AssertEntryBytesPreserved(sourceBytes, savedBytes, "xl/customPayload/_rels/item1.xml.rels");

        AssertContentTypeDefault(savedArchive, "freexbin", "application/vnd.freex.package-preservation-binary");
        AssertContentTypeOverride(savedArchive, "/xl/customPayload/item1.xml", "application/vnd.freex.package-preservation+xml");
        AssertContentTypeOverride(savedArchive, "/xl/customPayload/sheet-note.xml", "application/vnd.freex.package-preservation+xml");

        AssertRelationship(
            savedArchive,
            "_rels/.rels",
            "http://example.com/freex/relationships/customPayload",
            "xl/customPayload/item1.xml");
        AssertRelationship(
            savedArchive,
            "xl/customPayload/_rels/item1.xml.rels",
            "http://example.com/freex/relationships/binaryPayload",
            "item2.freexbin");
        AssertRelationship(
            savedArchive,
            "xl/customPayload/_rels/item1.xml.rels",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink",
            "https://example.com/source-info",
            "External");
        AssertRelationship(
            savedArchive,
            "xl/worksheets/_rels/sheet1.xml.rels",
            "http://example.com/freex/relationships/sheetNote",
            "../customPayload/sheet-note.xml");
        AssertRelationship(
            savedArchive,
            "xl/worksheets/_rels/sheet1.xml.rels",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink",
            "https://example.com/sheet-external",
            "External");
    }

    [Fact]
    public void LoadDeleteWorksheetSave_DropsRemovedWorksheetPackagePartsAndStaleReferences()
    {
        var sourceBytes = CreatePackageWithSheetScopedUnknownParts();

        var savedBytes = SaveAfterLoadingAndEditing(sourceBytes, workbook =>
        {
            var removedSheet = workbook.GetSheet("RemoveMe");
            removedSheet.Should().NotBeNull();
            workbook.RemoveSheet(removedSheet!.Id).Should().BeTrue();

            var keepSheet = workbook.GetSheet("Keep");
            keepSheet.Should().NotBeNull();
            keepSheet!.SetCell(new CellAddress(keepSheet.Id, 3, 1), new TextValue("kept after deletion"));
        });

        AssertRoundTripCellValue(savedBytes, "Keep", 3, 1, new TextValue("kept after deletion"));

        using var savedPackage = new MemoryStream(savedBytes);
        using var savedArchive = new ZipArchive(savedPackage, ZipArchiveMode.Read);

        savedArchive.GetEntry("xl/customPayload/keep-sheet-part.xml").Should().NotBeNull();
        savedArchive.GetEntry("xl/customPayload/keep-child.freexbin").Should().NotBeNull();
        savedArchive.GetEntry("xl/customPayload/_rels/keep-sheet-part.xml.rels").Should().NotBeNull();
        AssertRelationship(
            savedArchive,
            "xl/worksheets/_rels/sheet1.xml.rels",
            "http://example.com/freex/relationships/sheetScopedPayload",
            "../customPayload/keep-sheet-part.xml");
        AssertRelationship(
            savedArchive,
            "xl/customPayload/_rels/keep-sheet-part.xml.rels",
            "http://example.com/freex/relationships/binaryPayload",
            "keep-child.freexbin");

        var removedPaths = new[]
        {
            "xl/worksheets/sheet2.xml",
            "xl/worksheets/_rels/sheet2.xml.rels",
            "xl/customPayload/removed-sheet-part.xml",
            "xl/customPayload/_rels/removed-sheet-part.xml.rels",
            "xl/customPayload/removed-child.freexbin"
        };
        foreach (var path in removedPaths)
            savedArchive.GetEntry(path).Should().BeNull(path);

        var contentTypeOverridePartNames = ReadContentTypeOverridePartNames(savedArchive);
        contentTypeOverridePartNames.Should().NotContain("/xl/worksheets/sheet2.xml");
        contentTypeOverridePartNames.Should().NotContain("/xl/customPayload/removed-sheet-part.xml");

        var relationshipTargets = ReadInternalRelationshipTargets(savedArchive);
        relationshipTargets.Should().NotContain("xl/worksheets/sheet2.xml");
        relationshipTargets.Should().NotContain("xl/customPayload/removed-sheet-part.xml");
        relationshipTargets.Should().NotContain("xl/customPayload/removed-child.freexbin");
    }

    private static byte[] CreatePackageWithUnknownPackageGraph(string sheetName)
    {
        var packageBytes = CreateClosedXmlWorkbook(sheetName);
        using var package = CreateExpandablePackage(packageBytes);
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var contentTypesXml = LoadXml(archive, "[Content_Types].xml");
            AddContentTypeDefault(contentTypesXml, "freexbin", "application/vnd.freex.package-preservation-binary");
            AddContentTypeOverride(
                contentTypesXml,
                "/xl/customPayload/item1.xml",
                "application/vnd.freex.package-preservation+xml");
            AddContentTypeOverride(
                contentTypesXml,
                "/xl/customPayload/sheet-note.xml",
                "application/vnd.freex.package-preservation+xml");
            ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);

            AppendRelationship(
                archive,
                "_rels/.rels",
                "rIdFreeXCustomPayload",
                "http://example.com/freex/relationships/customPayload",
                "xl/customPayload/item1.xml");
            AppendRelationship(
                archive,
                "xl/customPayload/_rels/item1.xml.rels",
                "rIdFreeXBinaryPayload",
                "http://example.com/freex/relationships/binaryPayload",
                "item2.freexbin");
            AppendRelationship(
                archive,
                "xl/customPayload/_rels/item1.xml.rels",
                "rIdFreeXExternalInfo",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink",
                "https://example.com/source-info",
                "External");
            AppendRelationship(
                archive,
                "xl/worksheets/_rels/sheet1.xml.rels",
                "rIdFreeXSheetNote",
                "http://example.com/freex/relationships/sheetNote",
                "../customPayload/sheet-note.xml");
            AppendRelationship(
                archive,
                "xl/worksheets/_rels/sheet1.xml.rels",
                "rIdFreeXSheetExternal",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink",
                "https://example.com/sheet-external",
                "External");

            WriteTextEntry(
                archive,
                "xl/customPayload/item1.xml",
                """<fx:payload xmlns:fx="http://example.com/freex/package-preservation" id="root">preserve me</fx:payload>""");
            WriteBinaryEntry(archive, "xl/customPayload/item2.freexbin", [0x46, 0x58, 0x50, 0x4B, 0x47]);
            WriteTextEntry(
                archive,
                "xl/customPayload/sheet-note.xml",
                """<fx:sheetNote xmlns:fx="http://example.com/freex/package-preservation">sheet relationship sidecar</fx:sheetNote>""");
        }

        return package.ToArray();
    }

    private static byte[] CreatePackageWithSheetScopedUnknownParts()
    {
        var packageBytes = CreateClosedXmlWorkbook("Keep", "RemoveMe");
        using var package = CreateExpandablePackage(packageBytes);
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var contentTypesXml = LoadXml(archive, "[Content_Types].xml");
            AddContentTypeDefault(contentTypesXml, "freexbin", "application/vnd.freex.package-preservation-binary");
            AddContentTypeOverride(
                contentTypesXml,
                "/xl/customPayload/keep-sheet-part.xml",
                "application/vnd.freex.package-preservation+xml");
            AddContentTypeOverride(
                contentTypesXml,
                "/xl/customPayload/removed-sheet-part.xml",
                "application/vnd.freex.package-preservation+xml");
            ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);

            AppendRelationship(
                archive,
                "xl/worksheets/_rels/sheet1.xml.rels",
                "rIdFreeXKeepPayload",
                "http://example.com/freex/relationships/sheetScopedPayload",
                "../customPayload/keep-sheet-part.xml");
            AppendRelationship(
                archive,
                "xl/customPayload/_rels/keep-sheet-part.xml.rels",
                "rIdFreeXKeepChild",
                "http://example.com/freex/relationships/binaryPayload",
                "keep-child.freexbin");
            AppendRelationship(
                archive,
                "xl/worksheets/_rels/sheet2.xml.rels",
                "rIdFreeXRemovedPayload",
                "http://example.com/freex/relationships/sheetScopedPayload",
                "../customPayload/removed-sheet-part.xml");
            AppendRelationship(
                archive,
                "xl/worksheets/_rels/sheet2.xml.rels",
                "rIdFreeXRemovedExternal",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink",
                "https://example.com/removed-sheet",
                "External");
            AppendRelationship(
                archive,
                "xl/customPayload/_rels/removed-sheet-part.xml.rels",
                "rIdFreeXRemovedChild",
                "http://example.com/freex/relationships/binaryPayload",
                "removed-child.freexbin");

            WriteTextEntry(
                archive,
                "xl/customPayload/keep-sheet-part.xml",
                """<fx:payload xmlns:fx="http://example.com/freex/package-preservation" sheet="keep"/>""");
            WriteBinaryEntry(archive, "xl/customPayload/keep-child.freexbin", [0x4B, 0x45, 0x45, 0x50]);
            WriteTextEntry(
                archive,
                "xl/customPayload/removed-sheet-part.xml",
                """<fx:payload xmlns:fx="http://example.com/freex/package-preservation" sheet="removed"/>""");
            WriteBinaryEntry(archive, "xl/customPayload/removed-child.freexbin", [0x44, 0x52, 0x4F, 0x50]);
        }

        return package.ToArray();
    }

    private static MemoryStream CreateExpandablePackage(byte[] packageBytes)
    {
        var package = new MemoryStream(packageBytes.Length + 4096);
        package.Write(packageBytes);
        package.Position = 0;
        return package;
    }

    private static byte[] CreateClosedXmlWorkbook(params string[] sheetNames)
    {
        using var package = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            for (var index = 0; index < sheetNames.Length; index++)
            {
                var worksheet = workbook.Worksheets.Add(sheetNames[index]);
                worksheet.Cell(1, 1).Value = sheetNames[index];
                worksheet.Cell(2, 1).Value = index + 1;
            }

            workbook.SaveAs(package);
        }

        return package.ToArray();
    }

    private static byte[] SaveAfterLoadingAndEditing(byte[] sourceBytes, Action<Workbook> edit)
    {
        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream(sourceBytes, writable: false);
        var workbook = adapter.Load(source);
        edit(workbook);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        return saved.ToArray();
    }

    private static void AssertRoundTripCellValue(byte[] packageBytes, string sheetName, uint row, uint col, ScalarValue value)
    {
        using var package = new MemoryStream(packageBytes, writable: false);
        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.GetSheet(sheetName);
        sheet.Should().NotBeNull();
        sheet!.GetValue(row, col).Should().Be(value);
    }

    private static void AssertEntryBytesPreserved(byte[] sourceBytes, byte[] savedBytes, string path)
    {
        ReadEntryBytes(savedBytes, path).Should().Equal(ReadEntryBytes(sourceBytes, path), path);
    }

    private static byte[] ReadEntryBytes(byte[] packageBytes, string path)
    {
        using var package = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(package, ZipArchiveMode.Read);
        var entry = archive.GetEntry(path);
        entry.Should().NotBeNull(path);
        using var entryStream = entry!.Open();
        using var bytes = new MemoryStream();
        entryStream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static void AssertContentTypeDefault(ZipArchive archive, string extension, string contentType)
    {
        var contentTypesXml = LoadXml(archive, "[Content_Types].xml");
        contentTypesXml.Root!
            .Elements(ContentTypeNs + "Default")
            .Should()
            .ContainSingle(element =>
                string.Equals((string?)element.Attribute("Extension"), extension, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string?)element.Attribute("ContentType"), contentType, StringComparison.Ordinal));
    }

    private static void AssertContentTypeOverride(ZipArchive archive, string partName, string contentType)
    {
        var contentTypesXml = LoadXml(archive, "[Content_Types].xml");
        contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .Should()
            .ContainSingle(element =>
                string.Equals((string?)element.Attribute("PartName"), partName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string?)element.Attribute("ContentType"), contentType, StringComparison.Ordinal));
    }

    private static void AssertRelationship(
        ZipArchive archive,
        string relationshipPartPath,
        string type,
        string target,
        string? targetMode = null)
    {
        var relationshipsXml = LoadXml(archive, relationshipPartPath);
        relationshipsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Where(element => RelationshipMatches(element, type, target, targetMode))
            .Should()
            .ContainSingle();
    }

    private static bool RelationshipMatches(XElement element, string type, string target, string? targetMode) =>
        string.Equals((string?)element.Attribute("Type"), type, StringComparison.Ordinal) &&
        string.Equals((string?)element.Attribute("Target"), target, StringComparison.Ordinal) &&
        (targetMode == null
            ? element.Attribute("TargetMode") is null
            : string.Equals((string?)element.Attribute("TargetMode"), targetMode, StringComparison.Ordinal));

    private static IReadOnlyList<string> ReadContentTypeOverridePartNames(ZipArchive archive)
    {
        var contentTypesXml = LoadXml(archive, "[Content_Types].xml");
        return contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .Select(element => element.Attribute("PartName")?.Value)
            .OfType<string>()
            .ToArray();
    }

    private static IReadOnlyList<string> ReadInternalRelationshipTargets(ZipArchive archive)
    {
        var targets = new List<string>();
        foreach (var relationshipEntry in archive.Entries.Where(entry =>
                     entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var relationshipsXml = LoadXml(relationshipEntry);
            var sourcePartPath = RelationshipPartToSourcePart(relationshipEntry.FullName);
            foreach (var relationship in relationshipsXml.Root?.Elements(PackageRelNs + "Relationship") ?? [])
            {
                if (string.Equals(
                        relationship.Attribute("TargetMode")?.Value.Trim(),
                        "External",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target = relationship.Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(target))
                    continue;

                targets.Add(XlsxPackagePath.ResolveRelationshipTarget(sourcePartPath, target));
            }
        }

        return targets;
    }

    private static string RelationshipPartToSourcePart(string relationshipPartPath)
    {
        var normalized = XlsxPackagePath.NormalizeZipPath(relationshipPartPath.Replace('\\', '/'));
        if (string.Equals(normalized, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            return "";

        const string relsSegment = "/_rels/";
        var relsIndex = normalized.IndexOf(relsSegment, StringComparison.OrdinalIgnoreCase);
        if (relsIndex < 0 || !normalized.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            return normalized;

        var directory = normalized[..relsIndex];
        var fileName = normalized[(relsIndex + relsSegment.Length)..^".rels".Length];
        return string.IsNullOrEmpty(directory) ? fileName : $"{directory}/{fileName}";
    }

    private static void AddContentTypeDefault(XDocument contentTypesXml, string extension, string contentType)
    {
        if (contentTypesXml.Root!.Elements(ContentTypeNs + "Default").Any(element =>
                string.Equals((string?)element.Attribute("Extension"), extension, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        contentTypesXml.Root.Add(new XElement(
            ContentTypeNs + "Default",
            new XAttribute("Extension", extension),
            new XAttribute("ContentType", contentType)));
    }

    private static void AddContentTypeOverride(XDocument contentTypesXml, string partName, string contentType)
    {
        contentTypesXml.Root!.Elements(ContentTypeNs + "Override")
            .Where(element => string.Equals((string?)element.Attribute("PartName"), partName, StringComparison.OrdinalIgnoreCase))
            .Remove();
        contentTypesXml.Root.Add(new XElement(
            ContentTypeNs + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));
    }

    private static void AppendRelationship(
        ZipArchive archive,
        string relationshipPartPath,
        string id,
        string type,
        string target,
        string? targetMode = null)
    {
        var relationshipsXml = archive.GetEntry(relationshipPartPath) is { } entry
            ? LoadXml(entry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));

        var relationship = new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", type),
            new XAttribute("Target", target));
        if (targetMode is not null)
            relationship.SetAttributeValue("TargetMode", targetMode);

        relationshipsXml.Root!.Add(relationship);
        ReplaceXml(archive, relationshipPartPath, relationshipsXml);
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        entry.Should().NotBeNull(path);
        return LoadXml(entry!);
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplaceXml(ZipArchive archive, string path, XDocument xml)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        xml.Save(writer, System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    private static void WriteTextEntry(ZipArchive archive, string path, string content)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }

    private static void WriteBinaryEntry(ZipArchive archive, string path, byte[] content)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }
}
