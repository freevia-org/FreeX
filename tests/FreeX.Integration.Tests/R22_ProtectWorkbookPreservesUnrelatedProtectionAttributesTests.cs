using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R22-protection-security-3: ProtectWorkbookCommand/UnprotectWorkbookCommand
/// must not null the ENTIRE preserved workbookProtection metadata bag, since that also discards
/// unrelated attributes Core doesn't model (lockWindows, lockRevision, revisionsPassword, ...).
/// Only the modern-hash quartet (workbookAlgorithmName/workbookHashValue/workbookSaltValue/
/// workbookSpinCount) and the structure lock/password the command itself manages must be cleared.
/// </summary>
public sealed class R22_ProtectWorkbookPreservesUnrelatedProtectionAttributesTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void ProtectWorkbookCommand_ReprotectingWithNewPassword_PreservesLockWindows()
    {
        // Arrange: a workbook whose xl/workbook.xml is locked the way VBA's
        // `ActiveWorkbook.Protect Structure:=True, Windows:=True` leaves it --
        // `<workbookProtection lockStructure="1" lockWindows="1" workbookPassword="OLD"/>`.
        // FreeX's reader preserves lockWindows verbatim in Workbook.ProtectionMetadata since Core
        // doesn't model window locking (only "lockStructure"/"workbookPassword" are excluded from
        // that bag -- see XlsxWorkbookMetadataReader.LoadProtectionMetadata).
        var workbook = new Workbook("ReprotectPreservesLockWindows");
        workbook.AddSheet("S1");

        var adapter = new XlsxFileAdapter();
        var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        RewriteWorkbookProtection(source, protection =>
        {
            protection.SetAttributeValue("lockStructure", "1");
            protection.SetAttributeValue("lockWindows", "1");
            protection.SetAttributeValue("workbookPassword", "CACA");
        });
        source.Position = 0;

        var loaded = adapter.Load(source);
        loaded.IsStructureProtected.Should().BeTrue();
        // Sanity: the loaded model carries the preserved bag (with lockWindows) that a naive
        // "null the whole bag" re-protect would wipe out as collateral damage.
        loaded.ProtectionMetadata.Should().NotBeNull();

        // Act: the user only wants to change the structure-protection password -- unrelated to the
        // previously-set window lock -- exactly what the Protect Workbook dialog does when
        // re-protecting with a new password.
        var ctx = new TestCommandContext(loaded);
        new ProtectWorkbookCommand("new password").Apply(ctx).Success.Should().BeTrue();

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var entryStream = entry.Open();
        var workbookXml = XDocument.Load(entryStream);
        var savedProtection = workbookXml.Root!.Element(WorkbookNs + "workbookProtection");

        savedProtection.Should().NotBeNull();
        savedProtection!.Attribute("lockWindows").Should().NotBeNull(
            "the previously-set window-protection lock is unrelated to the structure password " +
            "change and must survive a re-protect, not be silently dropped as collateral damage " +
            "from nulling the whole preserved-attribute bag");
        savedProtection.Attribute("lockWindows")!.Value.Should().Be("1");
        savedProtection.Attribute("lockStructure")!.Value.Should().Be("1");
        savedProtection.Attribute("workbookPassword").Should().NotBeNull(
            "the new password must still be written");
        savedProtection.Attribute("workbookPassword")?.Value.Should().NotBe("CACA",
            "the old password's legacy hash must be replaced by the new one, not left stale");

        // And the new password actually unlocks the reloaded workbook.
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        ProtectionPasswordHelper.VerifyStoredPassword(reloaded.StructureProtectionPassword, "new password")
            .Should().BeTrue();
    }

    [Fact]
    public void UnprotectWorkbookCommand_RemovingStructureProtection_PreservesLockWindows()
    {
        // Arrange: same starting point as above, but this time the user just removes structure
        // protection entirely (no re-protect afterwards).
        var workbook = new Workbook("UnprotectPreservesLockWindows");
        workbook.AddSheet("S1");

        var adapter = new XlsxFileAdapter();
        var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        RewriteWorkbookProtection(source, protection =>
        {
            protection.SetAttributeValue("lockStructure", "1");
            protection.SetAttributeValue("lockWindows", "1");
            protection.SetAttributeValue("workbookPassword", "CACA");
        });
        source.Position = 0;

        var loaded = adapter.Load(source);
        loaded.IsStructureProtected.Should().BeTrue();
        loaded.ProtectionMetadata.Should().NotBeNull();

        // Act: Unprotect Workbook with no password verification needed here because the loaded
        // legacy "CACA" placeholder round-trips as a stored hash the helper below can match.
        var ctx = new TestCommandContext(loaded);
        var typedPassword = LegacyPasswordFor(loaded.StructureProtectionPassword);
        new UnprotectWorkbookCommand(typedPassword).Apply(ctx).Success.Should().BeTrue();
        loaded.IsStructureProtected.Should().BeFalse();

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var entryStream = entry.Open();
        var workbookXml = XDocument.Load(entryStream);
        var savedProtection = workbookXml.Root!.Element(WorkbookNs + "workbookProtection");

        savedProtection.Should().NotBeNull(
            "the unrelated lockWindows attribute must still be written even though structure " +
            "protection itself was removed");
        savedProtection!.Attribute("lockWindows").Should().NotBeNull();
        savedProtection.Attribute("lockWindows")!.Value.Should().Be("1");
        savedProtection.Attribute("lockStructure").Should().BeNull(
            "structure protection was removed, so lockStructure must not be re-written");
        savedProtection.Attribute("workbookPassword").Should().BeNull(
            "structure protection was removed, so the password must not be re-written");
    }

    // The fixture's raw "CACA" placeholder is not a real legacy hash we can verify against a
    // plaintext password, so read it back out unmodified: XlsxWorkbookMetadataReader.
    // LoadStructureProtectionPassword copies workbookPassword verbatim into
    // Workbook.StructureProtectionPassword without re-hashing, and
    // ProtectionPasswordHelper.VerifyStoredPassword treats a stored value that isn't a recognized
    // hash format as an already-hashed legacy value to compare case-insensitively.
    private static string LegacyPasswordFor(string? storedPassword) => storedPassword ?? string.Empty;

    private static void RewriteWorkbookProtection(MemoryStream packageStream, Action<XElement> mutate)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument workbookXml;
            using (var entryStream = entry.Open())
                workbookXml = XDocument.Load(entryStream);

            workbookXml.Root!.Element(WorkbookNs + "workbookProtection")?.Remove();
            var protection = new XElement(WorkbookNs + "workbookProtection");
            mutate(protection);

            var bookViews = workbookXml.Root.Element(WorkbookNs + "bookViews");
            if (bookViews is not null)
                bookViews.AddBeforeSelf(protection);
            else
                workbookXml.Root.AddFirst(protection);

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/workbook.xml");
            using var writeStream = newEntry.Open();
            workbookXml.Save(writeStream);
        }

        packageStream.Position = 0;
    }
}
