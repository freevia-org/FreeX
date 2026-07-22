using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxVbaProjectSignaturePackageGraphTests
{
    private const string VbaProjectPath = "xl/vbaProject.bin";
    private const string VbaProjectSignaturePath = "xl/vbaProjectSignature.bin";
    private const string VbaProjectRelationshipsPath = "xl/_rels/vbaProject.bin.rels";
    private const string VbaProjectContentType = "application/vnd.ms-office.vbaProject";
    private const string VbaProjectSignatureContentType = "application/vnd.ms-office.vbaProjectSignature";
    private const string MacroEnabledWorkbookContentType = "application/vnd.ms-excel.sheet.macroEnabled.main+xml";
    private const string VbaProjectRelationshipType = "http://schemas.microsoft.com/office/2006/relationships/vbaProject";
    private const string VbaProjectSignatureRelationshipType = "http://schemas.microsoft.com/office/2006/relationships/vbaProjectSignature";

    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    [Fact]
    public void LoadedWorkbookPatchSave_PreservesVbaProjectSignaturePackageGraph()
    {
        using var source = CreateWorkbookWithSignedVbaProject();
        var sourceVbaProjectBytes = ReadPackageEntryBytes(source, VbaProjectPath);
        var sourceSignatureBytes = ReadPackageEntryBytes(source, VbaProjectSignaturePath);
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42));

        // R70-io-vba-6-1: the plain Save now intentionally DROPS a source's VBA project (matching
        // Excel's Save-As-plain-format behavior), so this "signed VBA project survives an edited
        // save" scenario is exercised via the VBA-preserving entry point (what
        // XlsmFileAdapter/XltmFileAdapter delegate to) rather than plain Save.
        using var saved = new MemoryStream();
        adapter.SavePreservingVbaProject(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        AssertSignedVbaProjectGraph(saved);
        ReadPackageEntryBytes(saved, VbaProjectPath).Should().Equal(sourceVbaProjectBytes);
        ReadPackageEntryBytes(saved, VbaProjectSignaturePath).Should().Equal(sourceSignatureBytes);

        saved.Position = 0;
        adapter.Load(saved).GetSheetAt(0).GetValue(2, 1).Should().Be(new NumberValue(42));
    }

    // R60-io-vba-macro-6-2: a full-save's ONLY authority for stripping xl/vbaProjectSignature.bin
    // used to be "this is a full/rebuilt save", with no check on whether xl/vbaProject.bin itself
    // changed. Since FreeX has no VBA editor, every edited save (patch or full) copies
    // vbaProject.bin through byte-for-byte unchanged -- so a valid signature over those same
    // unchanged bytes was unforced data loss (real Excel would then report the macros unsigned
    // and a "digitally signed macros only" security policy would silently disable them). The
    // signature must now survive a full save exactly like it already does a patch save, as long
    // as vbaProject.bin itself is unchanged.
    [Fact]
    public void LoadedWorkbookFullSave_PreservesVbaProjectSignatureWhenVbaProjectUnchanged()
    {
        using var source = CreateWorkbookWithSignedVbaProject();
        var sourceVbaProjectBytes = ReadPackageEntryBytes(source, VbaProjectPath);
        var sourceSignatureBytes = ReadPackageEntryBytes(source, VbaProjectSignaturePath);
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("full-save-edit"));

        // R70-io-vba-6-1: see the comment in LoadedWorkbookPatchSave_PreservesVbaProjectSignaturePackageGraph
        // above -- plain Save now intentionally drops VBA, so use the preserving entry point here.
        using var saved = new MemoryStream();
        adapter.SavePreservingVbaProject(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        ReadPackageEntryBytes(saved, VbaProjectPath).Should().Equal(sourceVbaProjectBytes);
        ReadPackageEntryBytes(saved, VbaProjectSignaturePath).Should().Equal(
            sourceSignatureBytes,
            "the VBA project's own signature stays cryptographically valid when vbaProject.bin's bytes never changed");

        AssertSignedVbaProjectGraph(saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        AssertContentTypeOverride(contentTypesXml, "xl/workbook.xml", MacroEnabledWorkbookContentType);

        saved.Position = 0;
        adapter.Load(saved).GetSheetAt(0).GetValue(1, 2).Should().Be(new TextValue("full-save-edit"));
    }

    // Sibling no-regression test: the WHOLE-PACKAGE digital signature (_xmlsignatures/*) signs
    // the entire OPC graph, which a full/edited save always regenerates -- so unlike the VBA
    // project's own signature, it must still be unconditionally stripped on every edited save.
    // This guards against the fix above accidentally widening past the VBA-specific signature.
    [Fact]
    public void LoadedWorkbookFullSave_StillRemovesWholePackageDigitalSignature()
    {
        using var source = CreateWorkbookWithSignedVbaProject();
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            WriteBinaryEntry(archive, "_xmlsignatures/sig1.xml", [0x53, 0x49, 0x47]);

            var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
            EnsureContentTypeOverride(
                contentTypesXml,
                "_xmlsignatures/sig1.xml",
                "application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);
        }
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("full-save-edit"));

        // R70-io-vba-6-1: see the comment in LoadedWorkbookPatchSave_PreservesVbaProjectSignaturePackageGraph
        // above -- plain Save now intentionally drops VBA, so use the preserving entry point here
        // (this test's own point is the WHOLE-PACKAGE signature, which must still be stripped
        // regardless of the VBA project's preservation).
        using var saved = new MemoryStream();
        adapter.SavePreservingVbaProject(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        savedArchive.GetEntry("_xmlsignatures/sig1.xml").Should().BeNull(
            "the whole-package digital signature always goes stale on an edited save and must still be stripped");

        // ...while the VBA project's own signature (a different part entirely) still survives,
        // since vbaProject.bin itself was not touched by this edit.
        savedArchive.GetEntry(VbaProjectSignaturePath).Should().NotBeNull(
            "the unrelated VBA project signature must not be collaterally stripped");
    }

    private static MemoryStream CreateWorkbookWithSignedVbaProject()
    {
        var workbook = new Workbook("SignedVbaProject");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kept"));

        var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            WriteBinaryEntry(archive, VbaProjectPath, [0xD0, 0xCF, 0x11, 0xE0, 0x56, 0x42, 0x41]);
            WriteBinaryEntry(archive, VbaProjectSignaturePath, [0x53, 0x49, 0x47, 0x01, 0x02, 0x03]);

            var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
            EnsureContentTypeOverride(contentTypesXml, "xl/workbook.xml", MacroEnabledWorkbookContentType);
            EnsureContentTypeOverride(contentTypesXml, VbaProjectPath, VbaProjectContentType);
            EnsureContentTypeOverride(contentTypesXml, VbaProjectSignaturePath, VbaProjectSignatureContentType);
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var workbookRelsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
            EnsureRelationship(workbookRelsXml, "rIdVbaProject", VbaProjectRelationshipType, "vbaProject.bin");
            ReplacePackageXml(archive, "xl/_rels/workbook.xml.rels", workbookRelsXml);

            var vbaProjectRelsXml = new XDocument(new XElement(
                PackageRelationshipNs + "Relationships",
                new XElement(
                    PackageRelationshipNs + "Relationship",
                    new XAttribute("Id", "rIdVbaProjectSignature"),
                    new XAttribute("Type", VbaProjectSignatureRelationshipType),
                    new XAttribute("Target", "vbaProjectSignature.bin"))));
            ReplacePackageXml(archive, VbaProjectRelationshipsPath, vbaProjectRelsXml);
        }

        package.Position = 0;
        return package;
    }

    private static void AssertSignedVbaProjectGraph(Stream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        archive.GetEntry(VbaProjectPath).Should().NotBeNull("VBA project bytes must be preserved");
        archive.GetEntry(VbaProjectSignaturePath).Should().NotBeNull("safe byte-preserving saves keep the VBA project signature");
        archive.GetEntry(VbaProjectRelationshipsPath).Should().NotBeNull("the VBA project signature relationship graph must remain complete");

        var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        AssertContentTypeOverride(contentTypesXml, "xl/workbook.xml", MacroEnabledWorkbookContentType);
        AssertContentTypeOverride(contentTypesXml, VbaProjectPath, VbaProjectContentType);
        AssertContentTypeOverride(contentTypesXml, VbaProjectSignaturePath, VbaProjectSignatureContentType);

        var workbookRelsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
        AssertRelationship(workbookRelsXml, VbaProjectRelationshipType, "vbaProject.bin");

        var vbaProjectRelsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, VbaProjectRelationshipsPath);
        AssertRelationship(vbaProjectRelsXml, VbaProjectSignatureRelationshipType, "vbaProjectSignature.bin");
    }

    private static byte[] ReadPackageEntryBytes(Stream package, string path)
    {
        var previousPosition = package.Position;
        try
        {
            package.Position = 0;
            using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
            var entry = archive.GetEntry(path);
            entry.Should().NotBeNull(path);
            using var stream = entry!.Open();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            return bytes.ToArray();
        }
        finally
        {
            package.Position = previousPosition;
        }
    }

    private static void WriteBinaryEntry(ZipArchive archive, string path, byte[] bytes)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static void ReplacePackageXml(ZipArchive archive, string path, XDocument document)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        document.Save(writer, SaveOptions.DisableFormatting);
    }

    private static void EnsureContentTypeOverride(XDocument contentTypesXml, string partName, string contentType)
    {
        var normalizedPartName = "/" + partName.TrimStart('/');
        var root = contentTypesXml.Root!;
        var existing = root
            .Elements(ContentTypeNs + "Override")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("PartName"), normalizedPartName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.SetAttributeValue("ContentType", contentType);
            return;
        }

        root.Add(new XElement(
            ContentTypeNs + "Override",
            new XAttribute("PartName", normalizedPartName),
            new XAttribute("ContentType", contentType)));
    }

    private static void EnsureRelationship(XDocument relationshipsXml, string id, string relationshipType, string target)
    {
        relationshipsXml.Root!.Add(new XElement(
            PackageRelationshipNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", relationshipType),
            new XAttribute("Target", target)));
    }

    private static void AssertContentTypeOverride(XDocument contentTypesXml, string partName, string contentType)
    {
        ContentTypeOverrideExists(contentTypesXml, partName, contentType).Should().BeTrue();
    }

    private static bool ContentTypeOverrideExists(XDocument contentTypesXml, string partName, string contentType)
    {
        var normalizedPartName = "/" + partName.TrimStart('/');
        return contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .Any(element =>
                string.Equals((string?)element.Attribute("PartName"), normalizedPartName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string?)element.Attribute("ContentType"), contentType, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertRelationship(XDocument relationshipsXml, string relationshipType, string target)
    {
        relationshipsXml.Root!
            .Elements(PackageRelationshipNs + "Relationship")
            .Should()
            .ContainSingle(element =>
                string.Equals((string?)element.Attribute("Type"), relationshipType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string?)element.Attribute("Target"), target, StringComparison.OrdinalIgnoreCase) &&
                element.Attribute("TargetMode") == null);
    }
}
