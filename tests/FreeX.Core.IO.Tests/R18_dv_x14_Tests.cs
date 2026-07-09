using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round 18 findings in the x14 data-validation reader/writer:
/// <list type="bullet">
///   <item>R18-dv-extlst-x14-io-1: deleting the only x14 DV rule on a sheet must strip the stale
///     x14:dataValidations ext block from the preserved worksheet XML, not leave it in place
///     (which would resurrect the deleted rule on reopen).</item>
///   <item>R18-dv-extlst-x14-io-2: unmodeled x14-only attributes (e.g. imeMode) must round-trip
///     through load and save instead of being silently dropped.</item>
/// </list>
/// </summary>
public sealed class R18_dv_x14_Tests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace XmNs = "http://schemas.microsoft.com/office/excel/2006/main";
    private const string X14DvUri = "{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}";
    private const string WorksheetPath = "xl/worksheets/sheet1.xml";

    /// <summary>
    /// R18-dv-extlst-x14-io-1: delete the only x14 DV on a sheet (the source package had one) and
    /// re-run the x14 writer directly over that same package. The stale x14 ext block must be gone
    /// afterwards — before the fix, the writer skipped the sheet entirely (because the CURRENT
    /// model has zero x14 rules) and left the previously-saved ext block in place, resurrecting the
    /// deleted rule on reopen.
    /// </summary>
    [Fact]
    public void Save_AfterDeletingOnlyX14DvOnSheet_RemovesStaleX14Ext()
    {
        // Arrange: save a workbook whose sheet has one x14 DV rule, producing a package whose
        // worksheet XML has a real x14:dataValidations ext block (mirrors what a previously-saved
        // file looks like before the user deletes the rule).
        var wb = new Workbook("R18DvDeleteTest");
        var sheet = wb.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(1));

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheetId, 1, 2), new CellAddress(sheetId, 1, 2)),
            Type = DvType.List,
            Formula1 = "Sheet2!$A$1:$A$5",
            IsX14 = true,
        };
        sheet.DataValidations.Add(dv);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);

        // Sanity check: the stale ext block really is present in the freshly-saved package.
        AssertHasX14DvExt(stream, expected: true);

        // Act: simulate the rule being deleted from the model (e.g. via a delete-validation
        // command), then re-run the x14 writer directly against the SAME package stream (which
        // still carries the previously-saved worksheet XML with the now-stale ext block).
        sheet.DataValidations.Clear();
        stream.Position = 0;
        XlsxX14DataValidationWriter.Save(stream, wb);

        // Assert: the stale ext must be gone — the deleted rule must not resurrect on reopen.
        AssertHasX14DvExt(stream, expected: false);
    }

    /// <summary>
    /// R18-dv-extlst-x14-io-2: an x14:dataValidation with an unmodeled attribute (imeMode) must
    /// have that attribute captured into the DataValidation model on load, and re-emitted verbatim
    /// on save. Before the fix, TryReadX14DataValidation only read modeled attributes and dropped
    /// everything else.
    /// </summary>
    [Fact]
    public void ReadApplySave_X14DataValidationWithImeModeAttribute_RoundTripsImeMode()
    {
        var worksheetXml = XDocument.Parse("""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData/>
              <extLst>
                <ext uri="{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}"
                     xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                     xmlns:xm="http://schemas.microsoft.com/office/excel/2006/main">
                  <x14:dataValidations count="1">
                    <x14:dataValidation type="list" imeMode="hiragana">
                      <x14:formula1><xm:f>Sheet2!$A$1:$A$5</xm:f></x14:formula1>
                      <xm:sqref>B2</xm:sqref>
                    </x14:dataValidation>
                  </x14:dataValidations>
                </ext>
              </extLst>
            </worksheet>
            """);

        var metadata = XlsxX14DataValidationReader.Read(worksheetXml);
        metadata.Should().HaveCount(1);

        var wb = new Workbook("R18ImeModeTest");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        // No legacy rule exists for B2 → Apply creates a new rule from the x14 attributes.
        XlsxX14DataValidationReader.Apply(sheet, metadata);

        sheet.DataValidations.Should().HaveCount(1);
        var loadedDv = sheet.DataValidations[0];
        loadedDv.NativeAttributes.Should().NotBeNull(
            "the unmodeled imeMode attribute must be captured, not silently dropped");
        loadedDv.NativeAttributes!.Should().ContainKey("imeMode");
        loadedDv.NativeAttributes!["imeMode"].Should().Be("hiragana");

        // Act: save the loaded workbook and verify imeMode is re-emitted on the x14 element.
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;

        var x14DvElement = ReadX14DataValidationElement(stream);
        x14DvElement.Should().NotBeNull("the x14 DV element must be re-written on save");
        ((string?)x14DvElement!.Attribute("imeMode")).Should().Be(
            "hiragana",
            "the x14-only imeMode attribute must be re-emitted so it round-trips through save");
    }

    private static void AssertHasX14DvExt(MemoryStream stream, bool expected)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = archive.GetEntry(WorksheetPath)!;
        XDocument doc;
        using (var xmlStream = worksheetEntry.Open())
            doc = XDocument.Load(xmlStream);

        var hasExt = doc.Root!
            .Elements(WorksheetNs + "extLst")
            .SelectMany(extLst => extLst.Elements(WorksheetNs + "ext"))
            .Any(e => (string?)e.Attribute("uri") == X14DvUri);

        hasExt.Should().Be(expected);
    }

    private static XElement? ReadX14DataValidationElement(MemoryStream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = archive.GetEntry(WorksheetPath)!;
        XDocument doc;
        using (var xmlStream = worksheetEntry.Open())
            doc = XDocument.Load(xmlStream);

        return doc.Root!
            .Elements(WorksheetNs + "extLst")
            .SelectMany(extLst => extLst.Elements(WorksheetNs + "ext"))
            .Where(e => (string?)e.Attribute("uri") == X14DvUri)
            .SelectMany(e => e.Elements(X14Ns + "dataValidations"))
            .SelectMany(e => e.Elements(X14Ns + "dataValidation"))
            .FirstOrDefault();
    }
}
