using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-43 regression coverage for two Allow-Edit-Range ("protectedRange") bugs beyond the r35
/// multi-area fix:
///   - R43-io-allow-edit-ranges-2-1: editing/removing a per-range password must not be silently
///     reverted to the stale pre-edit password on save (XlsxWorksheetMetadataPreserver's
///     MergeProtectedRangeMetadata used to blindly copy the password quartet back from the source
///     element).
///   - R43-io-allow-edit-ranges-2-2: a whole-column/whole-row sqref (e.g. "A:A") used to fail to
///     parse and was silently dropped from the model entirely (XlsxAllowEditRangeMapper's
///     TryParseSqrefToken only handled tokens with both a column letter and a row number).
/// </summary>
public partial class FileAdapterSmokeTests
{
    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_RemovingAllowEditRangePassword_DoesNotResurrectStalePassword()
    {
        var workbook = new Workbook("ProtectedRangePasswordRemovalTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddProtectedRangeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.AllowEditRanges.Should().ContainSingle();
        var range = loadedSheet.AllowEditRanges[0];
        loadedSheet.AllowEditRangePasswords.Should().ContainKey(range).WhoseValue.Should().NotBeNull();

        // Remove the range's password (equivalent to SetAllowEditRangePasswordCommand(sheetId, range, null)
        // being applied), then edit an unrelated cell so the save exercises the real merge/preserve
        // pipeline rather than the unchanged-model source-copy fast path.
        loadedSheet.AllowEditRangePasswords.Remove(range);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 4, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var protectedRange = worksheetXml.Root!
            .Element(worksheetNs + "protectedRanges")!
            .Element(worksheetNs + "protectedRange");

        protectedRange.Should().NotBeNull();
        protectedRange!.Attribute("password").Should().BeNull(
            "removing the range password must not be silently reverted by re-copying the stale pre-edit password back onto the freshly-rebuilt element");
        protectedRange.Attribute("algorithmName").Should().BeNull();
        protectedRange.Attribute("hashValue").Should().BeNull();
        protectedRange.Attribute("saltValue").Should().BeNull();
        protectedRange.Attribute("spinCount").Should().BeNull();

        // Sibling no-regression: native-only metadata that genuinely has no modeled equivalent
        // (securityDescriptor, the custom "name") must still round-trip via the same merge step.
        protectedRange.Attribute("securityDescriptor")?.Value.Should().Be("D:PAI");
        protectedRange.Attribute("name")?.Value.Should().Be("NativeEditableRange");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_UnchangedAllowEditRangePassword_StillRoundTrips()
    {
        // Sibling no-regression case: when the password is left untouched, it must still survive a
        // save/merge pass (the exclusion added for the removal case must not also suppress a
        // legitimately-unchanged password).
        var workbook = new Workbook("ProtectedRangePasswordUnchangedTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddProtectedRangeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 4, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var protectedRange = worksheetXml.Root!
            .Element(worksheetNs + "protectedRanges")!
            .Element(worksheetNs + "protectedRange");

        protectedRange.Should().NotBeNull();
        protectedRange!.Attribute("password")!.Value.Should().Be("ABCD");
    }

    [Fact]
    public void XlsxAdapter_Load_WholeColumnAllowEditRange_IsModeledAndEnforced()
    {
        var workbook = new Workbook("ProtectedRangeWholeColumnTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWholeColumnProtectedRangeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);

        loadedSheet.AllowEditRanges.Should().ContainSingle();
        var range = loadedSheet.AllowEditRanges[0];
        range.Start.Col.Should().Be(1u);
        range.End.Col.Should().Be(1u);
        range.Start.Row.Should().Be(1u);
        range.End.Row.Should().Be(CellAddress.MaxRow);

        // A cell anywhere in column A is covered by the modeled whole-column range.
        var cellInColumnA = new CellAddress(loadedSheet.Id, 500, 1);
        range.Contains(cellInColumnA).Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_Load_WholeRowAllowEditRange_IsModeledAndEnforced()
    {
        // Sibling no-regression case for the row-oriented form of the same fix ("1:1").
        var workbook = new Workbook("ProtectedRangeWholeRowTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWholeRowProtectedRangeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);

        loadedSheet.AllowEditRanges.Should().ContainSingle();
        var range = loadedSheet.AllowEditRanges[0];
        range.Start.Row.Should().Be(1u);
        range.End.Row.Should().Be(1u);
        range.Start.Col.Should().Be(1u);
        range.End.Col.Should().Be(CellAddress.MaxCol);

        var cellInRow1 = new CellAddress(loadedSheet.Id, 1, 200);
        range.Contains(cellInRow1).Should().BeTrue();
    }

    private static void AddWholeColumnProtectedRangeMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "protectedRanges",
                new XElement(
                    worksheetNs + "protectedRange",
                    new XAttribute("name", "ColumnA"),
                    new XAttribute("sqref", "A:A"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWholeRowProtectedRangeMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "protectedRanges",
                new XElement(
                    worksheetNs + "protectedRange",
                    new XAttribute("name", "Row1"),
                    new XAttribute("sqref", "1:1"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }
}
