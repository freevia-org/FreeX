using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for G4: XlsxAdvancedConditionalFormatWriter must reuse the existing
/// worksheet-root extLst instead of appending a new one. When the worksheet already has an
/// extLst (e.g. from x14 data-validations), appending a second extLst caused the normalizer
/// to delete the second one (keeping only the first), which silently dropped the x14 data-bar
/// styling extension.
/// </summary>
public sealed class XlsxX14ConditionalFormatExtLstTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private const string X14CfUri = "{78C0D931-6437-407d-A8EE-F0AAD7539E65}";
    private const string X14DvUri = "{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}";
    private const string WorksheetPath = "xl/worksheets/sheet1.xml";

    /// <summary>
    /// A worksheet that has BOTH an x14 data-validation rule and an x14 data-bar CF must
    /// round-trip with both exts present inside a single extLst after save.
    /// </summary>
    [Fact]
    public void Save_WorksheetWithX14DvAndX14DataBarCf_BothExtsPreservedInSingleExtLst()
    {
        // Arrange: build a workbook with an x14 data-bar CF and an x14 data-validation.
        var wb = new Workbook("G4Test");
        var sheet = wb.AddSheet("Sheet1");
        var sheetId = sheet.Id;

        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheetId, row, 1), new NumberValue(row));

        // x14 data-bar CF (gradient=false triggers the x14 ext block in the writer).
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(0x63, 0xBE, 0x7B),
            DataBarGradient = false
        };
        sheet.ConditionalFormats.Add(cf);

        // x14 data-validation rule (cross-sheet formula forces x14 storage).
        var dv = new DataValidation
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 2),
                new CellAddress(sheetId, 5, 2)),
            Type = DvType.List,
            Formula1 = "Sheet2!$A$1:$A$5",
            IsX14 = true
        };
        sheet.DataValidations.Add(dv);

        // Act: save to XLSX.
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;

        // Assert: the saved worksheet must have exactly ONE root-level extLst that contains
        // both the x14 CF ext and the x14 DV ext.
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = archive.GetEntry(WorksheetPath)!;
        XDocument doc;
        using (var xmlStream = worksheetEntry.Open())
            doc = XDocument.Load(xmlStream);

        var root = doc.Root!;
        var extLsts = root.Elements(WorksheetNs + "extLst").ToList();
        extLsts.Should().HaveCount(1, "the writer must reuse the existing extLst, not append a new one");

        var exts = extLsts[0].Elements(WorksheetNs + "ext").ToList();
        exts.Should().Contain(e => (string?)e.Attribute("uri") == X14CfUri,
            "the x14 conditional-formatting ext must be present");
        exts.Should().Contain(e => (string?)e.Attribute("uri") == X14DvUri,
            "the x14 data-validation ext must be present");
    }

    /// <summary>
    /// The x14 conditionalFormattings ext must survive a full load → save → reload round-trip
    /// when a pre-existing x14 DV extLst is also present.
    /// </summary>
    [Fact]
    public void RoundTrip_X14DataBarWithExistingX14DvExtLst_DataBarExtSurvivesReload()
    {
        // Build and save.
        var wb = new Workbook("G4RoundTripTest");
        var sheet = wb.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheetId, row, 1), new NumberValue(row));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(0xFF, 0x0, 0x0),
            DataBarGradient = false
        };
        sheet.ConditionalFormats.Add(cf);

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 2),
                new CellAddress(sheetId, 3, 2)),
            Type = DvType.List,
            Formula1 = "OtherSheet!$A$1:$A$3",
            IsX14 = true
        };
        sheet.DataValidations.Add(dv);

        using var firstStream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, firstStream);
        firstStream.Position = 0;

        // Reload and save again (exercises the source-package preservation + re-apply path).
        var reloaded = new XlsxFileAdapter().Load(firstStream);
        using var secondStream = new MemoryStream();
        new XlsxFileAdapter().Save(reloaded, secondStream);
        secondStream.Position = 0;

        // After round-trip the x14 CF ext must still be present.
        using var archive = new ZipArchive(secondStream, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = archive.GetEntry(WorksheetPath)!;
        XDocument doc;
        using (var xmlStream = worksheetEntry.Open())
            doc = XDocument.Load(xmlStream);

        var allExts = doc.Root!
            .Elements(WorksheetNs + "extLst")
            .SelectMany(lst => lst.Elements(WorksheetNs + "ext"))
            .ToList();

        allExts.Should().Contain(e => (string?)e.Attribute("uri") == X14CfUri,
            "x14 data-bar styling ext must survive a full round-trip alongside an x14 DV extLst");
    }
}
