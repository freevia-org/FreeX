using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R62-io-defined-name-print-6-1: clearing a print area or print titles on
/// a previously-loaded workbook used to resurrect the stale <c>_xlnm.Print_Area</c>/
/// <c>_xlnm.Print_Titles</c> defined name on save.
///
/// Root cause: <c>XlsxFileAdapter.SourcePackageSnapshot.RestorePatchWorkbookDefinedNames</c> (run
/// after both the patch-success save path and every full-ClosedXML-rebuild save path that still has
/// a tracked source package) gated resurrection with
/// <c>if (isModelRepresentable &amp;&amp; !liveModelDefinedNameKeys.Contains(key) &amp;&amp;
/// !IsExcelReservedDefinedName(sourceNameAttr)) continue;</c>. Because <c>_xlnm.Print_Area</c>/
/// <c>_xlnm.Print_Titles</c> ARE Excel-reserved names, <c>!IsExcelReservedDefinedName(...)</c> is
/// always false there, making the whole AND always false -- so the gate never skipped a reserved
/// name's resurrection regardless of the sheet's actual current print-area/print-titles state. This
/// mirrors the R45 fix already applied to the OTHER preserver
/// (<c>XlsxWorkbookMetadataPreserver.MergeDefinedNames</c>, which runs earlier in the same save call)
/// but that liveness check never covered this resurrection path, so it ran again afterward and
/// blindly re-added the name the sibling preserver had just correctly dropped.
///
/// These tests exercise the real end-to-end entry point (<see cref="XlsxFileAdapter.Save"/> over a
/// workbook with a live, tracked source package from a prior <see cref="XlsxFileAdapter.Load"/>) --
/// exactly the reproduction path described in the finding -- rather than unit-testing the merge
/// helper directly, since the bug lives specifically in the resurrection path that only runs when a
/// source package is present.
/// </summary>
public sealed class R62_PatchSaveClearedPrintSettingResurrectionTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void ClearedPrintAreaAndTitles_AreNotResurrectedAfterReload()
    {
        // Arrange: build + save a workbook whose Sheet1 has a print area and print titles set, then
        // load it back so a tracked source package snapshot exists (mirrors opening a real .xlsx).
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetPrintAreas([new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 3))]);
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);

        using var firstSave = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var adapter = new XlsxFileAdapter();
        firstSave.Position = 0;
        var loaded = adapter.Load(firstSave);
        var loadedSheet = loaded.Sheets.Single();
        loadedSheet.PrintAreas.Should().NotBeEmpty("premise: the print area round-tripped from the first save");
        loadedSheet.PrintTitleRows.Should().NotBeNull("premise: print titles round-tripped from the first save");

        // Act: the user clears both, then saves again over the SAME adapter (so the tracked source
        // package snapshot from Load() is used, exercising RestorePatchWorkbookDefinedNames).
        loadedSheet.SetPrintAreas([]);
        loadedSheet.PrintTitleRows = null;

        using var secondSave = new MemoryStream();
        adapter.Save(loaded, secondSave);

        // Assert: reloading must NOT show the print area/titles coming back.
        secondSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(secondSave);
        var reloadedSheet = reloaded.Sheets.Single();
        reloadedSheet.PrintAreas.Should().BeEmpty(
            "clearing the print area must not be silently undone by the save/reload round-trip " +
            "(R62-io-defined-name-print-6-1)");
        reloadedSheet.PrintTitleRows.Should().BeNull(
            "clearing print titles must not be silently undone by the save/reload round-trip " +
            "(R62-io-defined-name-print-6-1)");

        var definedNames = ReadDefinedNamesRoot(secondSave);
        var printNames = definedNames?
            .Elements(WorkbookNs + "definedName")
            .Where(element => IsPrintAreaOrTitlesName(element.Attribute("name")?.Value))
            .ToList() ?? [];
        printNames.Should().BeEmpty(
            "the stale _xlnm.Print_Area/_xlnm.Print_Titles defined names must not be resurrected " +
            "into the saved workbook.xml once the sheet's print state has been cleared");
    }

    [Fact]
    public void StillLivePrintArea_IsPreservedAcrossAnUnrelatedEditAndSave()
    {
        // Sibling no-regression case: a print area that is genuinely still set must survive an
        // unrelated edit + save through the exact same source-package resurrection path, so the fix
        // doesn't turn Print_Area into something that's never preserved.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetPrintAreas([new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 3))]);

        using var firstSave = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var adapter = new XlsxFileAdapter();
        firstSave.Position = 0;
        var loaded = adapter.Load(firstSave);
        var loadedSheet = loaded.Sheets.Single();
        loadedSheet.PrintAreas.Should().NotBeEmpty();

        // Unrelated edit: touch a cell, leave the print area alone.
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 10, 10), new NumberValue(42));

        using var secondSave = new MemoryStream();
        adapter.Save(loaded, secondSave);

        secondSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(secondSave);
        var reloadedSheet = reloaded.Sheets.Single();
        reloadedSheet.PrintAreas.Should().NotBeEmpty(
            "a print area that is still genuinely live must keep surviving an unrelated edit + save, " +
            "exactly as before the fix");
    }

    private static bool IsPrintAreaOrTitlesName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmed = name.Trim();
        var unprefixed = trimmed.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase)
            ? trimmed["_xlnm.".Length..]
            : trimmed;
        return string.Equals(unprefixed, "Print_Area", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(unprefixed, "Print_Titles", StringComparison.OrdinalIgnoreCase);
    }

    private static XElement? ReadDefinedNamesRoot(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root!;
        var result = root.Element(WorkbookNs + "definedNames");
        package.Position = 0;
        return result;
    }
}
