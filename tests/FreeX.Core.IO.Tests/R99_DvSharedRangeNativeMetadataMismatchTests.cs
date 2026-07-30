using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round 99 finding io-dv-shared-range: when two independent
/// &lt;dataValidation&gt; elements on the same worksheet happen to share the same PRIMARY range
/// but only one of them is multi-area (e.g. a List rule with sqref="A1:A10 C1:C10" and a
/// completely separate Custom rule with sqref="A1:A10" only), <see
/// cref="XlsxDataValidationNativeMetadataMapper"/>.Apply used to match native metadata entries
/// purely by primary range via FindNativeMetadata (ignoring which element the caller's
/// DataValidation actually came from), so it handed the multi-area rule's AdditionalRanges
/// (C1:C10) to the unrelated single-area Custom rule too. On save that Custom rule's sqref would
/// then read "A1:A10 C1:C10", silently making it also govern cells it was never meant to
/// validate.
///
/// ClosedXML's own fluent API cannot author two overlapping validations on the same worksheet
/// (creating a second rule on an already-validated range steals that portion from the first), so
/// the fixture below builds a valid package via the real Save() path and then swaps in
/// hand-authored worksheet XML with the exact overlapping &lt;dataValidation&gt; shape described
/// by the finding, mirroring the technique used by R99_HyperlinkRelationshipRebindTests. The file
/// is then round-tripped through the real XlsxFileAdapter Load() entry point.
/// </summary>
public sealed class R99_DvSharedRangeNativeMetadataMismatchTests
{
    private const string WorksheetXmlWithSharedRangeRules =
        """
        <?xml version="1.0" encoding="utf-8" standalone="yes"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <dimension ref="A1:C10" />
          <sheetViews><sheetView workbookViewId="0" /></sheetViews>
          <sheetFormatPr defaultRowHeight="15" />
          <sheetData />
          <dataValidations count="2">
            <dataValidation type="list" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="A1:A10 C1:C10">
              <formula1>"Yes,No,Maybe"</formula1>
            </dataValidation>
            <dataValidation type="custom" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="A1:A10">
              <formula1>ISNUMBER(A1)</formula1>
            </dataValidation>
          </dataValidations>
          <pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3" />
        </worksheet>
        """;

    private const string WorksheetXmlWithSharedRangeRulesReversedOrder =
        """
        <?xml version="1.0" encoding="utf-8" standalone="yes"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <dimension ref="A1:C10" />
          <sheetViews><sheetView workbookViewId="0" /></sheetViews>
          <sheetFormatPr defaultRowHeight="15" />
          <sheetData />
          <dataValidations count="2">
            <dataValidation type="custom" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="A1:A10">
              <formula1>ISNUMBER(A1)</formula1>
            </dataValidation>
            <dataValidation type="list" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="A1:A10 C1:C10">
              <formula1>"Yes,No,Maybe"</formula1>
            </dataValidation>
          </dataValidations>
          <pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3" />
        </worksheet>
        """;

    /// <summary>
    /// R99: loads a worksheet where a multi-area List rule ("A1:A10 C1:C10") and a co-located
    /// single-area Custom rule ("A1:A10") share the same primary range, via the real
    /// XlsxFileAdapter.Load entry point. Before the fix, the Custom rule was loaded back with a
    /// bogus AdditionalRanges = [C1:C10] spliced onto it by the native-metadata reconciliation
    /// pass (XlsxDataValidationNativeMetadataMapper.Apply / FindNativeMetadata matching purely on
    /// the primary range).
    /// </summary>
    [Fact]
    public void Load_CustomRuleSharingPrimaryRangeWithMultiAreaListRule_DoesNotInheritAdditionalRanges()
    {
        using var stream = CreateSourcePackage(WorksheetXmlWithSharedRangeRules);

        var loaded = new XlsxFileAdapter().Load(stream);
        var loadedSheet = loaded.Sheets.Single(s => s.Name == "Sheet1");

        var loadedCustom = loadedSheet.DataValidations.Should().ContainSingle(dv => dv.Type == DvType.Custom).Subject;
        loadedCustom.AdditionalRanges.Should().BeEmpty(
            "the Custom rule was only ever authored on A1:A10 and must not inherit the sibling List rule's C1:C10 area");
        loadedCustom.AppliesTo.Start.Row.Should().Be(1);
        loadedCustom.AppliesTo.Start.Col.Should().Be(1);
        loadedCustom.AppliesTo.End.Row.Should().Be(10);
        loadedCustom.AppliesTo.End.Col.Should().Be(1);

        var loadedList = loadedSheet.DataValidations.Should().ContainSingle(dv => dv.Type == DvType.List).Subject;
        loadedList.AdditionalRanges.Should().ContainSingle();
        var additional = loadedList.AdditionalRanges[0];
        additional.Start.Row.Should().Be(1);
        additional.Start.Col.Should().Be(3);
        additional.End.Row.Should().Be(10);
        additional.End.Col.Should().Be(3);
    }

    /// <summary>
    /// No-regression sibling / order independence: the same fixture with the two
    /// &lt;dataValidation&gt; elements written in the OPPOSITE document order (Custom first, List
    /// second) must produce the identical, correct result. This guards against a fix that only
    /// works when the multi-area rule happens to appear first in the file.
    /// </summary>
    [Fact]
    public void Load_CustomRuleBeforeMultiAreaListRuleInDocumentOrder_StillDoesNotInheritAdditionalRanges()
    {
        using var stream = CreateSourcePackage(WorksheetXmlWithSharedRangeRulesReversedOrder);

        var loaded = new XlsxFileAdapter().Load(stream);
        var loadedSheet = loaded.Sheets.Single(s => s.Name == "Sheet1");

        var loadedCustom = loadedSheet.DataValidations.Should().ContainSingle(dv => dv.Type == DvType.Custom).Subject;
        loadedCustom.AdditionalRanges.Should().BeEmpty(
            "document order must not change which rule the C1:C10 area gets attributed to");

        var loadedList = loadedSheet.DataValidations.Should().ContainSingle(dv => dv.Type == DvType.List).Subject;
        loadedList.AdditionalRanges.Should().ContainSingle();
    }

    /// <summary>
    /// No-regression sibling: a genuine multi-area rule (no co-located sibling) must still have
    /// its own AdditionalRanges applied via the native-metadata pass on an ordinary round-trip
    /// through Save then Load, so the fix does not disable multi-area reconciliation altogether.
    /// </summary>
    [Fact]
    public void SaveThenLoad_MultiAreaListRuleAlone_KeepsOwnAdditionalRange()
    {
        var wb = new Workbook("R99MultiAreaAloneTest");
        var sheet = wb.AddSheet("Sheet1");
        var sheetId = sheet.Id;

        var listRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 10, 1)), // A1:A10
            Type = DvType.List,
            Formula1 = "Yes,No,Maybe",
        };
        listRule.AdditionalRanges.Add(new GridRange(new CellAddress(sheetId, 1, 3), new CellAddress(sheetId, 10, 3))); // C1:C10
        sheet.DataValidations.Add(listRule);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;

        var loaded = new XlsxFileAdapter().Load(stream);
        var loadedSheet = loaded.Sheets.Single(s => s.Name == "Sheet1");

        var loadedList = loadedSheet.DataValidations.Should().ContainSingle(dv => dv.Type == DvType.List).Subject;
        loadedList.AdditionalRanges.Should().ContainSingle();
        var additional = loadedList.AdditionalRanges[0];
        additional.Start.Row.Should().Be(1);
        additional.Start.Col.Should().Be(3);
        additional.End.Row.Should().Be(10);
        additional.End.Col.Should().Be(3);
    }

    // Builds a fully valid single-sheet .xlsx package (via a real adapter save, so every required
    // package part is already correct) and then swaps in hand-authored worksheet XML for
    // xl/worksheets/sheet1.xml, mirroring the technique used by
    // R99_HyperlinkRelationshipRebindTests.CreateSourcePackage (no worksheet .rels needed here
    // since these fixtures use no relationship-bearing content).
    private static MemoryStream CreateSourcePackage(string worksheetXml)
    {
        var workbook = new Workbook("R99-DvSharedRange");
        workbook.AddSheet("Sheet1");

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var existingEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
            existingEntry.Should().NotBeNull("a freshly saved single-sheet workbook must contain xl/worksheets/sheet1.xml");
            existingEntry!.Delete();

            var replacementEntry = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using var writer = new StreamWriter(replacementEntry.Open());
            writer.Write(worksheetXml);
        }

        stream.Position = 0;
        return stream;
    }
}
