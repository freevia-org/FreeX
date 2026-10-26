using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R62-io-defined-name-print-6-2: <c>_xlnm._FilterDatabase</c> was never
/// written when a sheet's AutoFilter is the ONLY defined-name-worthy content in the workbook.
///
/// Root cause: <c>XlsxFileAdapter.ApplyPackagePostProcessing</c> (the real
/// <see cref="XlsxFileAdapter.Save"/> entry point, unlike the existing
/// <c>R49_AutoFilterFilterDatabaseDefinedNameTests</c> which calls
/// <c>XlsxNamedRangeMapper.SaveToPackage</c> directly and so bypasses this gate) only invoked
/// <c>XlsxNamedRangeMapper.SaveToPackage</c> -- the sole code path that emits the synthetic
/// <c>_xlnm._FilterDatabase</c> entry via <c>CreateDefinedNameEntries</c>'s per-sheet AutoFilter
/// scan -- when <c>workbook.NamedRanges.Count > 0 || workbook.NamedFormulas.Count > 0 ||
/// workbook.ScopedNamedRanges.Count > 0 || workbook.ScopedNamedFormulas.Count > 0</c>. That
/// condition never checked whether any sheet had an AutoFilter, so a workbook whose only
/// defined-name-worthy state was an AutoFilter (e.g. every first save of a brand-new workbook with
/// only an AutoFilter applied) skipped SaveToPackage entirely, leaving workbook.xml with no
/// &lt;definedNames&gt; element at all despite the worksheet XML correctly carrying
/// &lt;autoFilter ref=...&gt;.
/// </summary>
public sealed class R62_AutoFilterOnlyFilterDatabaseGateTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Save_AutoFilterIsTheOnlyDefinedNameWorthyContent_StillEmitsFilterDatabase()
    {
        // Arrange: a brand-new workbook whose ONLY defined-name-worthy content is an AutoFilter --
        // no NamedRanges/NamedFormulas/ScopedNamedRanges/ScopedNamedFormulas anywhere.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:A2", null);

        workbook.NamedRanges.Should().BeEmpty();
        workbook.NamedFormulas.Should().BeEmpty();
        workbook.ScopedNamedRanges.Should().BeEmpty();
        workbook.ScopedNamedFormulas.Should().BeEmpty();

        // Act: save through the REAL entry point (XlsxFileAdapter.Save -> ApplyPackagePostProcessing),
        // not XlsxNamedRangeMapper.SaveToPackage directly.
        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        // Assert: _xlnm._FilterDatabase must exist, matching the sheet's live AutoFilter range.
        var filterDatabase = ReadFilterDatabase(package, localSheetId: 0);
        filterDatabase.Should().NotBeNull(
            "Excel always creates the built-in _xlnm._FilterDatabase name whenever an AutoFilter is " +
            "applied, even when it is the only defined-name-worthy content in the workbook " +
            "(R62-io-defined-name-print-6-2)");
        filterDatabase!.Value.Should().Be("Sheet1!$A$1:$A$2");
        filterDatabase.Attribute("hidden")!.Value.Should().Be("1");
    }

    [Fact]
    public void Save_NoAutoFilterAndNoNamedRanges_EmitsNoDefinedNamesElement()
    {
        // Sibling no-regression case: a workbook with NEITHER an AutoFilter NOR any named ranges
        // must still skip SaveToPackage (and so emit no <definedNames> element at all), exactly as
        // before the fix -- the new AutoFilter check must not cause SaveToPackage to run
        // unconditionally for every workbook.
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root!;
        root.Element(WorkbookNs + "definedNames").Should().BeNull(
            "a workbook with no AutoFilter and no named ranges must still emit no <definedNames> " +
            "element, unaffected by the AutoFilter-aware gate");
        package.Position = 0;
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
}
