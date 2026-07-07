using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-12 fix bucket Q12 regression tests.
///   - R12-xlsx-data-validation-2: DataValidation.IsX14 must survive a native .fxl round-trip so a
///     cross-sheet List validation still gets written to the worksheet extLst x14 block (rather than
///     the legacy &lt;dataValidation&gt; element, which Excel rejects for cross-sheet formulas) on a
///     subsequent XLSX export.
///   - R12-xlsx-defined-names-1: a workbook-scoped defined name's refers-to formula must be written
///     with absolute ($-anchored) cell references so its meaning does not shift depending on which
///     cell is active when it is used.
/// </summary>
public sealed class FreeXR12Q12Tests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── R12-xlsx-data-validation-2 ──────────────────────────────────────────────────────────────

    [Fact]
    public void NativeJsonAdapter_RoundTrips_DataValidation_IsX14Flag()
    {
        var workbook = new Workbook("X14FlagRoundTripR12Q12");
        var sheet = workbook.AddSheet("Data");

        // Simulate a validation that XlsxX14DataValidationReader would have produced for a
        // cross-sheet List source: IsX14 = true, real formula living in the "x14" slot.
        var x14Validation = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 2)),
            Type = DvType.List,
            Formula1 = "Sheet2!$A$1:$A$5",
            IsX14 = true
        };
        sheet.DataValidations.Add(x14Validation);

        // A plain (non-cross-sheet) legacy validation must remain IsX14 = false.
        var legacyValidation = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 4, 2), new CellAddress(sheet.Id, 4, 2)),
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            Formula1 = "0",
            IsX14 = false
        };
        sheet.DataValidations.Add(legacyValidation);

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedX14 = reloadedSheet.DataValidations.Single(dv => dv.Formula1 == "Sheet2!$A$1:$A$5");
        var reloadedLegacy = reloadedSheet.DataValidations.Single(dv => dv.Type == DvType.WholeNumber);

        reloadedX14.IsX14.Should().BeTrue(
            "a cross-sheet List validation must keep IsX14 across a native .fxl round-trip so it is re-emitted into the x14 extLst block on XLSX export");
        reloadedLegacy.IsX14.Should().BeFalse(
            "a plain legacy validation must not be promoted to x14 on round-trip");
    }

    [Fact]
    public void NativeJsonRoundTrippedX14Validation_ExportsX14ExtLstBlock_OnXlsxSave()
    {
        // End-to-end: an .fxl round-trip must preserve enough information (IsX14) that a subsequent
        // XLSX export still emits the worksheet extLst x14 block for the cross-sheet List rule.
        var workbook = new Workbook("X14ExportAfterFxlRoundTripR12Q12");
        var sheet = workbook.AddSheet("Data");
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 2)),
            Type = DvType.List,
            Formula1 = "Sheet2!$A$1:$A$5",
            IsX14 = true
        });
        workbook.AddSheet("Sheet2");

        using var fxlStream = new MemoryStream();
        var jsonAdapter = new NativeJsonAdapter();
        jsonAdapter.Save(workbook, fxlStream);
        fxlStream.Position = 0;
        var reloaded = jsonAdapter.Load(fxlStream);

        using var xlsxStream = new MemoryStream();
        new XlsxFileAdapter().Save(reloaded, xlsxStream);

        xlsxStream.Position = 0;
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        // "Data" is the first sheet added to the workbook, so it maps to xl/worksheets/sheet1.xml.
        // Do not glob-select archive.Entries.First(...): ZIP central-directory entry order does not
        // necessarily match sheet order (e.g. "Sheet2" can be written before "Data"/sheet1.xml), so a
        // loose First() can silently grab the wrong worksheet part and assert on the wrong sheet.
        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        worksheetEntry.Should().NotBeNull();
        using var worksheetStream = worksheetEntry!.Open();
        var worksheetXml = XDocument.Load(worksheetStream);

        var hasX14Block = worksheetXml.Descendants()
            .Any(el => el.Name.LocalName == "dataValidations" && el.Name.NamespaceName.Contains("2009/9/main"));

        hasX14Block.Should().BeTrue(
            "the cross-sheet List validation's IsX14 flag must survive the .fxl round-trip so the XLSX export still writes the x14 extLst block instead of an Excel-rejected legacy formula1");
    }

    // ── R12-xlsx-defined-names-1 ────────────────────────────────────────────────────────────────

    [Fact]
    public void SavedWorkbookDefinedName_UsesAbsoluteCellReferences()
    {
        var workbook = new Workbook("DefinedNameAbsoluteRefR12Q12");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.DefineNamedRange("Sales", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 3)));

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        workbookEntry.Should().NotBeNull();

        using var workbookStream = workbookEntry!.Open();
        var workbookXml = XDocument.Load(workbookStream);
        var definedName = workbookXml.Root!
            .Element(WorkbookNs + "definedNames")!
            .Elements(WorkbookNs + "definedName")
            .Single(el => el.Attribute("name")?.Value == "Sales");

        definedName.Value.Should().Be("Sheet1!$A$1:$C$5",
            "a defined name's refers-to formula must be absolute ($-anchored) so its meaning does not shift depending on the active cell when it is used");
    }

    [Fact]
    public void SavedSheetScopedDefinedName_UsesAbsoluteCellReferences()
    {
        var workbook = new Workbook("ScopedDefinedNameAbsoluteRefR12Q12");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.DefineNamedRange(
            "LocalSales",
            new GridRange(new CellAddress(sheet.Id, 7, 2), new CellAddress(sheet.Id, 7, 2)),
            metadata: null,
            scopeSheetId: sheet.Id);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        workbookEntry.Should().NotBeNull();

        using var workbookStream = workbookEntry!.Open();
        var workbookXml = XDocument.Load(workbookStream);
        var definedName = workbookXml.Root!
            .Element(WorkbookNs + "definedNames")!
            .Elements(WorkbookNs + "definedName")
            .Single(el => el.Attribute("name")?.Value == "LocalSales");

        definedName.Value.Should().Be("Sheet1!$B$7:$B$7",
            "a sheet-scoped defined name's refers-to formula must also be absolute so it resolves to the same cell regardless of which cell is active");
    }
}
