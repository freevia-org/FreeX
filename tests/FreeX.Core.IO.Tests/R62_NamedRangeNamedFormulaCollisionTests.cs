using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R62-commands-name-box-6-2: a name can end up present in BOTH
/// <see cref="Workbook.NamedRanges"/> and <see cref="Workbook.NamedFormulas"/> at the same time --
/// e.g. a multi-area (union) name loaded into NamedFormulas (because a single <see cref="GridRange"/>
/// can't represent a union), followed by the Name Box's create-on-unknown fallback defining a
/// colliding single-area NamedRanges entry with the same text (since name-box lookup never checks
/// NamedFormulas). Before the fix, <c>XlsxNamedRangeMapper.CreateDefinedNameEntries</c> yielded TWO
/// <c>DefinedNameEntry</c> records for the identical (name, scope) key, and SaveToPackage's
/// key-based dedup merge silently picked whichever one was enumerated LAST (the NamedFormulas
/// entry, since that loop runs after NamedRanges) -- so the "new name" the user apparently just
/// created via the Name Box was discarded on save with no error, and the whole mechanism relied on
/// enumeration order rather than an explicit collision guard. The fix makes NamedFormulas
/// authoritative for a colliding name and skips the NamedRanges entry entirely.
/// </summary>
public sealed class R62_NamedRangeNamedFormulaCollisionTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Save_NameCollidingInNamedRangesAndNamedFormulas_ProducesNoSpuriousSkippedWarning()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        // Pre-existing multi-area (union) name, loaded into NamedFormulas because GridRange cannot
        // represent it (mirrors XlsxNamedRangeMapper.LoadDefinedNames's union handling).
        workbook.NamedFormulas["SalesCells"] = "Sheet1!$A$1,Sheet1!$C$1";

        // The Name Box create-on-unknown bug: a brand-new single-area NamedRanges entry with the
        // SAME name text, pointing at whatever was selected when the user typed the existing name.
        var singleCell = new CellAddress(sheet.Id, 1, 1);
        workbook.NamedRanges["SalesCells"] = new GridRange(singleCell, singleCell);

        // Act: save through the REAL full pipeline (XlsxNamedRangeMapper.Save's ClosedXML
        // xlWorkbook.DefinedNames.Add call, then ApplyPackagePostProcessing's SaveToPackage pass).
        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        var result = adapter.SaveWithWarnings(workbook, stream);

        // Assert: before the fix, ClosedXML's xlWorkbook.DefinedNames.Add("SalesCells", ...) was
        // called TWICE for the same name (once from the NamedRanges loop, once from the
        // NamedFormulas loop) -- Excel/ClosedXML disallow a duplicate defined name, so the SECOND
        // call throws, and SaveWorkbookDefinedName's catch-all silently reports this as a
        // "[named-formula] Named formula 'SalesCells' could not be saved and was skipped." warning
        // -- a false-positive data-loss warning shown to the user for their real, authoritative
        // name, even though the name is NOT actually lost from the final saved file (the later
        // SaveToPackage pass still corrects the text). The fix removes the collision entirely by
        // never attempting the redundant NamedRanges-side Add, so no such warning is produced.
        result.Warnings.Should().NotContain(
            warning => warning.Contains("SalesCells", StringComparison.Ordinal),
            "the colliding single-area NamedRanges shadow entry must never surface a spurious " +
            "\"could not be saved and was skipped\" warning for the user's real, authoritative " +
            "multi-area name (R62-commands-name-box-6-2)");

        var salesCellsEntries = ReadDefinedNames(stream, "SalesCells");
        salesCellsEntries.Should().ContainSingle(
            "a name colliding between NamedRanges and NamedFormulas must produce exactly one " +
            "<definedName> element in the final saved file, not two competing entries resolved by " +
            "enumeration order");
        salesCellsEntries[0].Value.Should().Be(
            "Sheet1!$A$1,Sheet1!$C$1",
            "NamedFormulas is authoritative for a colliding name (it is the pre-existing, real " +
            "multi-area name); the colliding single-area NamedRanges entry must be dropped");
    }

    [Fact]
    public void SaveToPackage_NameCollidingInNamedRangesAndNamedFormulas_EmitsOnlyTheNamedFormulasEntry()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        // Pre-existing multi-area (union) name, loaded into NamedFormulas because GridRange cannot
        // represent it (mirrors XlsxNamedRangeMapper.LoadDefinedNames's union handling).
        workbook.NamedFormulas["SalesCells"] = "Sheet1!$A$1,Sheet1!$C$1";

        // The Name Box create-on-unknown bug: a brand-new single-area NamedRanges entry with the
        // SAME name text, pointing at whatever was selected when the user typed the existing name.
        var singleCell = new CellAddress(sheet.Id, 1, 1);
        workbook.NamedRanges["SalesCells"] = new GridRange(singleCell, singleCell);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var salesCellsEntries = ReadDefinedNames(package, "SalesCells");

        salesCellsEntries.Should().ContainSingle(
            "a name colliding between NamedRanges and NamedFormulas must produce exactly one " +
            "<definedName> element, not two competing entries resolved by enumeration order");
        salesCellsEntries[0].Value.Should().Be(
            "Sheet1!$A$1,Sheet1!$C$1",
            "NamedFormulas is authoritative for a colliding name (it is the pre-existing, real " +
            "multi-area name); the colliding single-area NamedRanges entry must be dropped, not " +
            "silently overwrite or be overwritten by enumeration order");
    }

    [Fact]
    public void SaveToPackage_NoCollision_OrdinaryNamedRangeStillEmitted()
    {
        // Sibling no-regression case: an ordinary (non-colliding) NamedRanges entry must still be
        // emitted normally when there is no same-named entry in NamedFormulas.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        workbook.DefineNamedRange("Region", range);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var regionEntries = ReadDefinedNames(package, "Region");
        regionEntries.Should().ContainSingle(
            "an ordinary named range with no NamedFormulas collision must still round-trip normally");
        regionEntries[0].Value.Should().Be("Sheet1!$A$1:$A$5");
    }

    private static List<XElement> ReadDefinedNames(MemoryStream package, string name)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root!;
        var result = root.Element(WorkbookNs + "definedNames")?
            .Elements(WorkbookNs + "definedName")
            .Where(element => element.Attribute("name")?.Value == name)
            .ToList() ?? [];
        package.Position = 0;
        return result;
    }
}
