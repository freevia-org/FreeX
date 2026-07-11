using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression test for R22-protection-security-1: MergeWorkbookProtection must not resurrect a
/// stale, still-protected &lt;workbookProtection&gt; element from the pre-edit source package once
/// the in-memory model has deliberately cleared ALL workbook-structure protection (Unprotect
/// Workbook). Before the fix, XlsxWorkbookMetadataPreserver.MergeWorkbookProtection blindly cloned
/// the original source element back into the target whenever the freshly-generated workbook.xml had
/// no &lt;workbookProtection&gt; of its own -- silently reverting "Unprotect Workbook" on save.
/// </summary>
public sealed class WorkbookUnprotectMetadataPreservationTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void XlsxAdapter_UnprotectWorkbookThenSave_DoesNotResurrectStaleProtectionElement()
    {
        // Arrange: a workbook whose structure is locked the classic (legacy hash) way Excel writes
        // it -- lockStructure + workbookPassword only, no modern ISO 29500 hash quartet, matching
        // the finding's exact repro (<workbookProtection lockStructure="1" workbookPassword="83AF"/>).
        var workbook = new Workbook("UnprotectPreservationRoundTrip");
        workbook.AddSheet("S1");

        var adapter = new XlsxFileAdapter();
        var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        RewriteWorkbookProtection(source, protection =>
        {
            protection.SetAttributeValue("lockStructure", "1");
            protection.SetAttributeValue("workbookPassword", "83AF");
        });
        source.Position = 0;

        var loaded = adapter.Load(source);
        loaded.IsStructureProtected.Should().BeTrue();
        loaded.StructureProtectionPassword.Should().Be("83AF");
        // Sanity: with only lockStructure/workbookPassword present, there is nothing left over for
        // the native-preserve bag to carry -- ProtectionMetadata stays null, exactly like the
        // finding's failure scenario.
        loaded.ProtectionMetadata.Should().BeNull();

        // Act: exactly what UnprotectWorkbookCommand.Apply does on a successful unprotect.
        loaded.IsStructureProtected = false;
        loaded.StructureProtectionPassword = null;
        loaded.ProtectionMetadata = null;

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        // Assert on the raw saved XML: the resave must NOT carry the stale, still-protected element
        // forward -- "Unprotect Workbook" must actually stick.
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml");
            var savedProtection = workbookXml.Root!.Element(WorkbookNs + "workbookProtection");
            savedProtection.Should().BeNull(
                "Unprotect Workbook cleared all protection state, so the resaved workbook.xml must not " +
                "carry the stale pre-edit <workbookProtection> element (with the OLD password) forward");
        }

        saved.Position = 0;

        // And a fresh reload agrees: the workbook comes back unprotected, not silently reprotected
        // with the old password.
        var reloaded = adapter.Load(saved);
        reloaded.IsStructureProtected.Should().BeFalse(
            "the saved file must reopen unprotected -- Unprotect Workbook must not be silently reverted");
        reloaded.StructureProtectionPassword.Should().BeNullOrEmpty();
    }

    // ── Test helper (mirrors PProtectionFixesTests.RewriteWorkbookProtection) ──────────────────

    private static void RewriteWorkbookProtection(MemoryStream packageStream, Action<XElement> mutate)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml");
            workbookXml.Root!.Element(WorkbookNs + "workbookProtection")?.Remove();
            var protection = new XElement(WorkbookNs + "workbookProtection");
            mutate(protection);
            // workbookProtection must precede bookViews/sheets per the ECMA-376 sequence.
            var bookViews = workbookXml.Root.Element(WorkbookNs + "bookViews");
            if (bookViews is not null)
                bookViews.AddBeforeSelf(protection);
            else
                workbookXml.Root.AddFirst(protection);
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream);
    }
}
