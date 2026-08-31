using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for the P-protection review-5 fixes:
/// K17 (sheet protection must round-trip the full set of chosen permissions through XLSX, not a
/// hardcoded 2-item allow-list), K18 (workbook structure protection loaded from the modern ISO
/// 29500 hash must not yield a null/unverifiable password), and K34 (same for sheet protection).
/// </summary>
public sealed class PProtectionFixesTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── K17: sheet protection permissions must round-trip through XLSX ─────────────────────────

    [Fact]
    public void XlsxAdapter_RoundTrip_PreservesNonDefaultSheetProtectionPermissions()
    {
        var workbook = new Workbook("PermissionRoundTrip");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("data"));
        sheet.IsProtected = true;
        sheet.ProtectionPassword = "secret";
        sheet.ProtectionPermissions.Clear();
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatCells);
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.Sort);
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UseAutoFilter);

        var adapter = new XlsxFileAdapter();
        var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.GetSheetAt(0);

        loadedSheet.IsProtected.Should().BeTrue();
        loadedSheet.ProtectionPermissions.Should().BeEquivalentTo(
        [
            SheetProtectionPermission.FormatCells,
            SheetProtectionPermission.Sort,
            SheetProtectionPermission.UseAutoFilter
        ]);
        // The two "Select" defaults were NOT selected by the user and must not silently reappear.
        loadedSheet.ProtectionPermissions.Should().NotContain(SheetProtectionPermission.SelectLockedCells);
        loadedSheet.ProtectionPermissions.Should().NotContain(SheetProtectionPermission.SelectUnlockedCells);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_DefaultSheetProtectionPermissionsStayDefault()
    {
        var workbook = new Workbook("PermissionDefaultRoundTrip");
        var sheet = workbook.AddSheet("S1");
        sheet.IsProtected = true;
        sheet.ProtectionPassword = "secret";
        // Leave sheet.ProtectionPermissions at the Sheet constructor's default.

        var adapter = new XlsxFileAdapter();
        var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.GetSheetAt(0);

        loadedSheet.ProtectionPermissions.Should().BeEquivalentTo(
        [
            SheetProtectionPermission.SelectLockedCells,
            SheetProtectionPermission.SelectUnlockedCells
        ]);
    }

    [Fact]
    public void XlsxAdapter_LoadsPermissionsFromExcelAuthoredSheetProtectionXml()
    {
        // Simulates a real Excel-authored file where sort is explicitly permitted
        // (sort="0", i.e. explicitly not-denied) alongside the implicit defaults.
        var workbook = new Workbook("ExcelAuthoredPermissions");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("data"));

        var adapter = new XlsxFileAdapter();
        var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        RewriteSheetProtection(source, protection =>
        {
            protection.SetAttributeValue("sheet", "1");
            protection.SetAttributeValue("formatCells", "1");
            protection.SetAttributeValue("sort", "0");
            protection.SetAttributeValue("autoFilter", "0");
        });

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);

        loadedSheet.IsProtected.Should().BeTrue();
        loadedSheet.ProtectionPermissions.Should().Contain(SheetProtectionPermission.Sort);
        loadedSheet.ProtectionPermissions.Should().Contain(SheetProtectionPermission.UseAutoFilter);
        loadedSheet.ProtectionPermissions.Should().NotContain(SheetProtectionPermission.FormatCells);
    }

    // ── K34: sheet-level modern ISO 29500 hash must be verifiable, not permanently locked out ──

    [Fact]
    public void XlsxAdapter_LoadsSheetProtectedWithModernHash_AndVerifiesCorrectPassword()
    {
        var workbook = new Workbook("SheetModernHash");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));

        var adapter = new XlsxFileAdapter();
        var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        var (saltBase64, hashBase64) = ComputeReferenceHash("correct password", "SHA-512", 100_000,
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);

        RewriteSheetProtection(source, protection =>
        {
            protection.SetAttributeValue("sheet", "1");
            protection.SetAttributeValue("algorithmName", "SHA-512");
            protection.SetAttributeValue("hashValue", hashBase64);
            protection.SetAttributeValue("saltValue", saltBase64);
            protection.SetAttributeValue("spinCount", "100000");
        });

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);

        loadedSheet.IsProtected.Should().BeTrue();
        loadedSheet.ProtectionPassword.Should().NotBeNullOrEmpty();

        // The exact original password must verify...
        ProtectionPasswordHelper.VerifyStoredPassword(loadedSheet.ProtectionPassword, "correct password")
            .Should().BeTrue("the modern hash must be verifiable against the real password");
        // ...and no other password (including blank) may unprotect it.
        ProtectionPasswordHelper.VerifyStoredPassword(loadedSheet.ProtectionPassword, "wrong guess")
            .Should().BeFalse("a modern-hash-protected sheet must not be unprotectable by any password");
        ProtectionPasswordHelper.VerifyStoredPassword(loadedSheet.ProtectionPassword, "")
            .Should().BeFalse();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesModernSheetHashAcrossRoundTrip()
    {
        var workbook = new Workbook("SheetModernHashRoundTrip");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));

        var adapter = new XlsxFileAdapter();
        var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        var (saltBase64, hashBase64) = ComputeReferenceHash("correct password", "SHA-512", 100_000,
            [21, 22, 23, 24, 25, 26, 27, 28]);

        RewriteSheetProtection(source, protection =>
        {
            protection.SetAttributeValue("sheet", "1");
            protection.SetAttributeValue("algorithmName", "SHA-512");
            protection.SetAttributeValue("hashValue", hashBase64);
            protection.SetAttributeValue("saltValue", saltBase64);
            protection.SetAttributeValue("spinCount", "100000");
        });

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);

        ProtectionPasswordHelper.VerifyStoredPassword(reloadedSheet.ProtectionPassword, "correct password")
            .Should().BeTrue("the modern hash must survive a load -> edit -> save -> reload cycle");

        // The raw XML must still carry the modern hash attributes (not a garbage re-derived legacy hash).
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var protectionElement = worksheetXml.Root!.Element(WorksheetNs + "sheetProtection");
        protectionElement.Should().NotBeNull();
        protectionElement!.Attribute("hashValue")!.Value.Should().Be(hashBase64);
        protectionElement.Attribute("password").Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_SourcePatch_PreservesModernSheetHashAndPatchEligibility()
    {
        var workbook = new Workbook("SheetModernHashSourcePatch");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));

        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);

        const string password = "source patch password";
        var (saltBase64, hashBase64) = ComputeReferenceHash(password, "SHA-512", 100_000,
            [61, 62, 63, 64, 65, 66, 67, 68]);
        RewriteSheetProtection(source, protection =>
        {
            protection.SetAttributeValue("sheet", "1");
            protection.SetAttributeValue("algorithmName", "SHA-512");
            protection.SetAttributeValue("hashValue", hashBase64);
            protection.SetAttributeValue("saltValue", saltBase64);
            protection.SetAttributeValue("spinCount", "100000");
        });

        source.Position = 0;
        var loaded = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(loaded, out var blockReason)
            .Should().BeTrue(blockReason);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 2, 1), new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        saved.Position = 0;
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            var protection = worksheetXml.Root!.Element(WorksheetNs + "sheetProtection")!;
            protection.Attribute("algorithmName")!.Value.Should().Be("SHA-512");
            protection.Attribute("spinCount")!.Value.Should().Be("100000");
            protection.Attribute("saltValue")!.Value.Should().Be(saltBase64);
            protection.Attribute("hashValue")!.Value.Should().Be(hashBase64);
            protection.Attribute("password").Should().BeNull();
        }

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        ProtectionPasswordHelper.VerifyStoredPassword(reloaded.GetSheetAt(0).ProtectionPassword, password)
            .Should().BeTrue();
    }

    // ── K18: workbook-level modern ISO 29500 hash must be verifiable, not permanently unlocked ─

    [Fact]
    public void XlsxAdapter_LoadsWorkbookStructureProtectedWithModernHash_AndVerifiesCorrectPassword()
    {
        var workbook = new Workbook("WorkbookModernHash");
        workbook.AddSheet("S1");

        var adapter = new XlsxFileAdapter();
        var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        var (saltBase64, hashBase64) = ComputeReferenceHash("structure secret", "SHA-512", 100_000,
            [30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45]);

        RewriteWorkbookProtection(source, protection =>
        {
            protection.SetAttributeValue("lockStructure", "1");
            protection.SetAttributeValue("workbookAlgorithmName", "SHA-512");
            protection.SetAttributeValue("workbookHashValue", hashBase64);
            protection.SetAttributeValue("workbookSaltValue", saltBase64);
            protection.SetAttributeValue("workbookSpinCount", "100000");
        });

        source.Position = 0;
        var loaded = adapter.Load(source);

        loaded.IsStructureProtected.Should().BeTrue();
        loaded.StructureProtectionPassword.Should().NotBeNullOrEmpty();

        ProtectionPasswordHelper.VerifyStoredPassword(loaded.StructureProtectionPassword, "structure secret")
            .Should().BeTrue("the modern workbook hash must be verifiable against the real password");
        ProtectionPasswordHelper.VerifyStoredPassword(loaded.StructureProtectionPassword, "wrong guess")
            .Should().BeFalse();
        ProtectionPasswordHelper.VerifyStoredPassword(loaded.StructureProtectionPassword, "")
            .Should().BeFalse("a workbook locked with the modern hash must never be unprotectable by any/no password");
        ProtectionPasswordHelper.VerifyStoredPassword(loaded.StructureProtectionPassword, null)
            .Should().BeFalse();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesModernWorkbookHashAcrossRoundTrip()
    {
        var workbook = new Workbook("WorkbookModernHashRoundTrip");
        workbook.AddSheet("S1");

        var adapter = new XlsxFileAdapter();
        var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        var (saltBase64, hashBase64) = ComputeReferenceHash("structure secret", "SHA-512", 100_000,
            [50, 51, 52, 53, 54, 55, 56, 57]);

        RewriteWorkbookProtection(source, protection =>
        {
            protection.SetAttributeValue("lockStructure", "1");
            protection.SetAttributeValue("workbookAlgorithmName", "SHA-512");
            protection.SetAttributeValue("workbookHashValue", hashBase64);
            protection.SetAttributeValue("workbookSaltValue", saltBase64);
            protection.SetAttributeValue("workbookSpinCount", "100000");
        });

        source.Position = 0;
        var loaded = adapter.Load(source);

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        var reloaded = adapter.Load(saved);

        reloaded.IsStructureProtected.Should().BeTrue();
        ProtectionPasswordHelper.VerifyStoredPassword(reloaded.StructureProtectionPassword, "structure secret")
            .Should().BeTrue("the modern workbook hash must survive a load -> save -> reload cycle");

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml");
        var protectionElement = workbookXml.Root!.Element(WorksheetNs + "workbookProtection");
        protectionElement.Should().NotBeNull();
        protectionElement!.Attribute("workbookHashValue")!.Value.Should().Be(hashBase64);
        protectionElement.Attribute("workbookPassword").Should().BeNull();
    }

    // ── Test helpers ─────────────────────────────────────────────────────────────────────────

    private static void RewriteSheetProtection(MemoryStream packageStream, Action<XElement> mutate)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            worksheetXml.Root!.Element(WorksheetNs + "sheetProtection")?.Remove();
            var protection = new XElement(WorksheetNs + "sheetProtection");
            mutate(protection);
            worksheetXml.Root.Add(protection);
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void RewriteWorkbookProtection(MemoryStream packageStream, Action<XElement> mutate)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml");
            workbookXml.Root!.Element(WorksheetNs + "workbookProtection")?.Remove();
            var protection = new XElement(WorksheetNs + "workbookProtection");
            mutate(protection);
            // workbookProtection must precede bookViews/sheets per the ECMA-376 sequence.
            var bookViews = workbookXml.Root.Element(WorksheetNs + "bookViews");
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

    // Independent reference implementation of the ECMA-376 iterated hash used to synthesize
    // ground-truth test fixtures (kept separate from the production algorithm under test).
    private static (string SaltBase64, string HashBase64) ComputeReferenceHash(
        string password, string algorithmName, int spinCount, byte[] salt)
    {
        using HashAlgorithm algorithm = algorithmName switch
        {
            "SHA-512" => SHA512.Create(),
            "SHA-1" => SHA1.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithmName))
        };

        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var buffer = new byte[salt.Length + passwordBytes.Length];
        salt.CopyTo(buffer, 0);
        passwordBytes.CopyTo(buffer, salt.Length);
        var digest = algorithm.ComputeHash(buffer);

        for (var i = 0; i < spinCount; i++)
        {
            var iterationBuffer = new byte[digest.Length + 4];
            digest.CopyTo(iterationBuffer, 0);
            BitConverter.GetBytes(i).CopyTo(iterationBuffer, digest.Length);
            digest = algorithm.ComputeHash(iterationBuffer);
        }

        return (Convert.ToBase64String(salt), Convert.ToBase64String(digest));
    }
}
