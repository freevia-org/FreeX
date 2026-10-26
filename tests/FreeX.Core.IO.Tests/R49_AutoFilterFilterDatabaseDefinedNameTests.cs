using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R49-io-defined-name-scope-3-2: FreeX never created or updated the
/// built-in <c>_xlnm._FilterDatabase</c> sheet-scoped defined name when a sheet's AutoFilter was
/// applied, changed, or cleared -- <see cref="XlsxNamedRangeMapper"/> treated it as a pure
/// passthrough reserved name (never regenerated from the live model), leaving it absent or stale
/// relative to the sheet's own &lt;autoFilter ref=...&gt; element written by
/// <see cref="XlsxWorksheetAutoFilterXmlMapper"/>.
/// </summary>
public sealed class R49_AutoFilterFilterDatabaseDefinedNameTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void SaveToPackage_AutoFilterAppliedChangedThenCleared_CreatesUpdatesThenRemovesFilterDatabase()
    {
        // Arrange: a plain workbook with no AutoFilter yet, saved once (no _FilterDatabase name can
        // exist yet -- mirrors "open a workbook with NO AutoFilter" in the failure scenario).
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        ReadFilterDatabase(package, localSheetId: 0).Should().BeNull(
            "no AutoFilter has been applied yet, so no _xlnm._FilterDatabase name should exist");

        // Act 1: apply AutoFilter over A1:C10 and save (patch-save path calls SaveToPackage directly
        // against the existing on-disk package, exactly like XlsxFileAdapter.Save.cs:82).
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:C10", null);
        XlsxNamedRangeMapper.SaveToPackage(workbook, package);

        var created = ReadFilterDatabase(package, localSheetId: 0);
        created.Should().NotBeNull("applying AutoFilter must create the built-in _FilterDatabase name");
        created!.Value.Should().Be("Sheet1!$A$1:$C$10");
        created.Attribute("hidden")!.Value.Should().Be("1", "Excel always writes _FilterDatabase as hidden");

        // Act 2: widen the filtered range and save again -- the name must track the new range, not
        // the stale original one.
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:D20", null);
        XlsxNamedRangeMapper.SaveToPackage(workbook, package);

        var updated = ReadFilterDatabase(package, localSheetId: 0);
        updated.Should().NotBeNull();
        updated!.Value.Should().Be(
            "Sheet1!$A$1:$D$20", "changing the AutoFilter range must update _FilterDatabase's refersTo, not leave it stale");

        // Act 3: clear the AutoFilter entirely and save -- the now-orphaned built-in name must be
        // removed, not left behind as stale passthrough.
        sheet.AutoFilter = null;
        XlsxNamedRangeMapper.SaveToPackage(workbook, package);

        ReadFilterDatabase(package, localSheetId: 0).Should().BeNull(
            "clearing the AutoFilter must remove the _xlnm._FilterDatabase name entirely");
    }

    [Fact]
    public void SaveToPackage_ExistingFilterDatabaseUntouchedByAutoFilterEdit_SurvivesUnrelatedNamedRangeSave()
    {
        // Sibling/no-regression case: a sheet already has a matching AutoFilter + _FilterDatabase
        // name (created by an earlier save) alongside an ordinary user-defined named range. Editing
        // ONLY the unrelated named range's metadata and saving again must leave the AutoFilter's
        // _FilterDatabase name completely untouched -- it must not be spuriously dropped or
        // corrupted by a save that never touches the AutoFilter itself.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:C10", null);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 10, 5));
        workbook.DefineNamedRange("SalesRegion", range);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var initialFilterDatabase = ReadFilterDatabase(package, localSheetId: 0);
        initialFilterDatabase.Should().NotBeNull();
        initialFilterDatabase!.Value.Should().Be("Sheet1!$A$1:$C$10");

        // Act: edit only the unrelated named range's comment; the AutoFilter model is untouched.
        workbook.DefineNamedRange("SalesRegion", range, new NamedRangeMetadata("Workbook", "Q3 territory list"));
        XlsxNamedRangeMapper.SaveToPackage(workbook, package);

        // Assert: the unrelated edit landed...
        var salesRegion = ReadDefinedName(package, "SalesRegion");
        salesRegion.Should().NotBeNull();
        salesRegion!.Attribute("comment")!.Value.Should().Be("Q3 territory list");

        // ...and the untouched AutoFilter's _FilterDatabase name is still exactly as it was.
        var filterDatabase = ReadFilterDatabase(package, localSheetId: 0);
        filterDatabase.Should().NotBeNull(
            "an unrelated named-range-only save must not remove an untouched, still-valid _FilterDatabase name");
        filterDatabase!.Value.Should().Be("Sheet1!$A$1:$C$10");
        filterDatabase.Attribute("hidden")!.Value.Should().Be("1");
    }

    private static XElement? ReadFilterDatabase(MemoryStream package, int localSheetId)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root!;
        var result = root.Element(WorkbookNs + "definedNames")?
            .Elements(WorkbookNs + "definedName")
            .FirstOrDefault(element =>
                element.Attribute("name")?.Value == "_xlnm._FilterDatabase" &&
                element.Attribute("localSheetId")?.Value == localSheetId.ToString());
        package.Position = 0;
        return result;
    }

    private static XElement? ReadDefinedName(MemoryStream package, string name)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root!;
        var result = root.Element(WorkbookNs + "definedNames")?
            .Elements(WorkbookNs + "definedName")
            .FirstOrDefault(element => element.Attribute("name")?.Value == name);
        package.Position = 0;
        return result;
    }
}
