using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression test for R61-io-workbook-protection-6-1: a workbook protected via Windows-only
/// (&lt;workbookProtection lockWindows="1" workbookPassword="..."/&gt;, i.e. lockStructure absent)
/// must not silently lose its password on load/resave. Before the fix, LoadProtection returned
/// <c>WorkbookProtectionState.None</c> (nulling the password) whenever lockStructure was
/// false/absent, regardless of a present workbookPassword, and the native-metadata preserve bag
/// separately excluded the workbookPassword attribute -- so the password ended up in neither
/// place and a full rebuild save permanently dropped it.
/// </summary>
public sealed class WorkbookWindowsOnlyProtectionPasswordTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void XlsxAdapter_LoadWindowsOnlyProtectionWithPassword_PreservesPasswordAndResavesIt()
    {
        // Arrange: Excel's Protect Workbook dialog with only "Windows" checked and a password typed
        // writes exactly this shape -- lockWindows + workbookPassword, no lockStructure attribute.
        var workbook = new Workbook("WindowsOnlyProtectionRoundTrip");
        workbook.AddSheet("S1");

        var adapter = new XlsxFileAdapter();
        var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        RewriteWorkbookProtection(source, protection =>
        {
            protection.SetAttributeValue("lockWindows", "1");
            protection.SetAttributeValue("workbookPassword", "CC81");
        });
        source.Position = 0;

        // Act
        var loaded = adapter.Load(source);

        // Assert: structure is correctly reported as NOT protected (lockStructure was absent), but
        // the password must survive -- it still guards unprotecting the Windows lock in Excel.
        loaded.IsStructureProtected.Should().BeFalse(
            "lockStructure was absent from the source -- only Windows protection was requested");
        loaded.StructureProtectionPassword.Should().Be("CC81",
            "the workbookPassword must be preserved even though only Windows (not Structure) is locked");

        // Act: resave without any further edits (e.g. a plain Save As, or any edit that forces a
        // full workbook.xml rebuild).
        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml");
            var savedProtection = workbookXml.Root!.Element(WorkbookNs + "workbookProtection");
            savedProtection.Should().NotBeNull("lockWindows was set, so the element must still be written");
            savedProtection!.Attribute("lockWindows")!.Value.Should().Be("1", "Windows protection must round-trip");
            savedProtection.Attribute("workbookPassword")!.Value.Should().Be("CC81",
                "the password must be re-emitted, not silently dropped, on a full rebuild save");
            savedProtection.Attribute("lockStructure").Should().BeNull(
                "structure was never locked -- lockStructure must not be fabricated");
        }

        saved.Position = 0;

        // And a fresh reload agrees.
        var reloaded = adapter.Load(saved);
        reloaded.IsStructureProtected.Should().BeFalse();
        reloaded.StructureProtectionPassword.Should().Be("CC81");
    }

    [Fact]
    public void XlsxAdapter_LoadStructureProtectionWithPassword_StillWorks()
    {
        // Sibling no-regression test: the ordinary case (lockStructure="1" with a password) must
        // keep working exactly as before -- both IsStructureProtected and the password survive.
        var workbook = new Workbook("StructureProtectionRoundTrip");
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

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml");
        var savedProtection = workbookXml.Root!.Element(WorkbookNs + "workbookProtection");
        savedProtection.Should().NotBeNull();
        savedProtection!.Attribute("lockStructure")!.Value.Should().Be("1");
        savedProtection.Attribute("workbookPassword")!.Value.Should().Be("83AF");
    }

    // ── Test helper (mirrors WorkbookUnprotectMetadataPreservationTests/PProtectionFixesTests) ──

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
