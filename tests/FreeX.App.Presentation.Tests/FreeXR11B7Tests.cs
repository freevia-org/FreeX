using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Regression tests for round-11 fix bucket R7:
/// R11-protection-security-1 (re-protecting a modern-hash sheet with a NEW password left the
/// stale ISO 29500 verifier in place, so the OLD password kept unlocking the sheet and the new
/// password was silently dropped on save), and R11-xlsx-core-io-1 (a dimension-only patch-save --
/// row height/hidden change with no cell edits -- skipped the r-less-row pre-flight guard, letting
/// ApplyDimensionChanges append a duplicate &lt;row&gt; for an r-less row).
/// </summary>
public sealed class FreeXR11B7Tests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── R11-protection-security-1: sheet reprotect-with-new-password must drop the stale hash ──

    [Fact]
    public void ProtectSheetCommand_AfterUnprotectingModernHashSheet_DropsStaleVerifierForOldPassword()
    {
        // Arrange: a sheet protected the way Excel 2013+ protects it -- only the modern ISO 29500
        // hash quartet (algorithmName/hashValue/saltValue/spinCount), no legacy password attribute.
        var workbook = new Workbook("SheetReprotectRoundTrip");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));

        var adapter = new XlsxFileAdapter();
        var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        var (saltBase64, hashBase64) = ComputeReferenceHash("old password", "SHA-512", 1000,
            [1, 2, 3, 4, 5, 6, 7, 8]);
        RewriteSheetProtection(source, protection =>
        {
            protection.SetAttributeValue("sheet", "1");
            protection.SetAttributeValue("algorithmName", "SHA-512");
            protection.SetAttributeValue("hashValue", hashBase64);
            protection.SetAttributeValue("saltValue", saltBase64);
            protection.SetAttributeValue("spinCount", "1000");
        });
        source.Position = 0;

        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.IsProtected.Should().BeTrue();
        // Sanity: the loaded model carries the preserved modern-hash bag that the writer would
        // otherwise blindly re-apply.
        loadedSheet.ProtectionMetadata.Should().NotBeNull();

        // Act: exactly what the Protect/Unprotect Sheet dialogs do -- unprotect with the
        // (verified) old password, then protect again with a brand-new password.
        var ctx = new FakeCommandContext(loaded);
        new UnprotectSheetCommand(loadedSheet.Id, "old password").Apply(ctx).Success.Should().BeTrue();
        new ProtectSheetCommand(loadedSheet.Id, "beef").Apply(ctx).Success.Should().BeTrue();

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        // Assert on the raw saved XML: only ONE verifier scheme may be present, and it must be
        // for the NEW password -- not a leftover modern hash for the revoked old one.
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            using var entryStream = entry.Open();
            var worksheetXml = XDocument.Load(entryStream);
            var savedProtection = worksheetXml.Root!.Element(WorksheetNs + "sheetProtection");

            savedProtection.Should().NotBeNull();
            savedProtection!.Attribute("hashValue").Should().BeNull(
                "the stale modern-hash verifier for the revoked old password must not survive a reprotect with a new password");
            savedProtection.Attribute("algorithmName").Should().BeNull();
            savedProtection.Attribute("password").Should().NotBeNull(
                "the new password must be the only verifier written back");
        }

        // And a fresh reload agrees with FreeX's own reader: the NEW password unlocks, the OLD
        // one (previously authoritative for Excel) no longer does.
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        ProtectionPasswordHelper.VerifyStoredPassword(reloadedSheet.ProtectionPassword, "beef")
            .Should().BeTrue();
        ProtectionPasswordHelper.VerifyStoredPassword(reloadedSheet.ProtectionPassword, "old password")
            .Should().BeFalse("the revoked old password must no longer unlock the sheet in either FreeX or Excel");
    }

    // ── R11-xlsx-core-io-1: dimension-only patch-save must not duplicate an r-less row ──────────

    [Fact]
    public void LoadedWorkbookPatchSave_DimensionOnlyChangeOnRLessRows_FallsBackToFullSaveWithoutDuplicateRows()
    {
        // Build a source package whose first worksheet contains r-less <row> elements (schema-valid;
        // produced by streaming writers).
        using var source = CreateRLessRowSourcePackage();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        // Change ONLY a row's height -- no cell edits -- so the only patch-save input is a
        // dimensionChange, never a cell change. Before the fix, the r-less pre-flight guard only
        // scanned worksheets with cell changes, so this path skipped the guard entirely and went
        // straight to ApplyDimensionChanges -> ApplyRowDimension -> FindOrCreateRow, which cannot
        // match the existing r-less row and appends a brand new duplicate <row> instead.
        var sheet = workbook.GetSheetAt(0);
        sheet.RowHeights[1] = 30.0;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        // Verify the saved file never gained a duplicate <row> for row 1.
        saved.Position = 0;
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            using var entryStream = entry.Open();
            var worksheetXml = XDocument.Load(entryStream);
            var rowElements = worksheetXml.Root!.Element(WorksheetNs + "sheetData")!.Elements(WorksheetNs + "row").ToList();

            // Exactly 3 logical rows must exist -- never a duplicate for row 1.
            rowElements.Should().HaveCount(3, "a dimension-only patch must not duplicate any r-less row");
            var rowNumbers = rowElements
                .Select(row => row.Attribute("r")?.Value)
                .Where(r => r is not null)
                .ToList();
            rowNumbers.Should().OnlyHaveUniqueItems("there must be at most one <row> element per logical row number");
        }

        // Reload and verify no data was lost/duplicated across the round trip.
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.GetCell(1, 1)!.Value.Should().Be(new NumberValue(1));
        reloadedSheet.GetCell(2, 1)!.Value.Should().Be(new NumberValue(2));
        reloadedSheet.GetCell(3, 1)!.Value.Should().Be(new NumberValue(3));
    }

    // ── Test helpers ─────────────────────────────────────────────────────────────────────────

    private sealed class FakeCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static void RewriteSheetProtection(MemoryStream packageStream, Action<XElement> mutate)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument worksheetXml;
            using (var entryStream = entry.Open())
                worksheetXml = XDocument.Load(entryStream);

            worksheetXml.Root!.Element(WorksheetNs + "sheetProtection")?.Remove();
            var protection = new XElement(WorksheetNs + "sheetProtection");
            mutate(protection);
            worksheetXml.Root.Add(protection);

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using var writeStream = newEntry.Open();
            worksheetXml.Save(writeStream);
        }

        packageStream.Position = 0;
    }

    private static MemoryStream CreateRLessRowSourcePackage()
    {
        // Start with a normal single-sheet workbook, then replace the worksheet XML with a
        // version whose <row> elements have no r attribute (schema-valid; document-order implies
        // position -- the shape a streaming writer can produce).
        var workbook = new Workbook("RLessRowsDimensionOnly");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));

        var adapter = new XlsxFileAdapter();
        var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        ReplaceWorksheetWithRLessRows(stream);
        stream.Position = 0;
        return stream;
    }

    private static void ReplaceWorksheetWithRLessRows(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);

        // Hand-craft worksheet XML with r-less rows -- schema-valid per ECMA-376.
        // The three rows contain cells A1=1, A2=2, A3=3 but omit the row r attribute.
        var rLessWorksheetXml = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                WorksheetNs + "worksheet",
                new XElement(
                    WorksheetNs + "sheetData",
                    new XElement(
                        WorksheetNs + "row",
                        new XElement(WorksheetNs + "c",
                            new XAttribute("r", "A1"),
                            new XAttribute("t", "n"),
                            new XElement(WorksheetNs + "v", "1"))),
                    new XElement(
                        WorksheetNs + "row",
                        new XElement(WorksheetNs + "c",
                            new XAttribute("r", "A2"),
                            new XAttribute("t", "n"),
                            new XElement(WorksheetNs + "v", "2"))),
                    new XElement(
                        WorksheetNs + "row",
                        new XElement(WorksheetNs + "c",
                            new XAttribute("r", "A3"),
                            new XAttribute("t", "n"),
                            new XElement(WorksheetNs + "v", "3"))))));

        var existingEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        existingEntry?.Delete();
        var newEntry = archive.CreateEntry("xl/worksheets/sheet1.xml");
        using var writeStream = newEntry.Open();
        rLessWorksheetXml.Save(writeStream);
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
