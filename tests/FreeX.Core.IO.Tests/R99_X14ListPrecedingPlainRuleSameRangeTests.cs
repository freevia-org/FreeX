using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round 99 finding io-dv-x14-wrong-rule-merge: <see
/// cref="XlsxX14DataValidationReader"/>.Apply locates the legacy rule an x14 block belongs to via
/// FindExisting, which used to match purely on primary-range row/col equality (ignoring type and
/// content), exactly like the sibling bug already fixed in
/// <see cref="XlsxDataValidationNativeMetadataMapper"/> (see R99_DvSharedRangeNativeMetadataMismatchTests).
///
/// If a worksheet has a plain single-area Custom rule on sqref="A1:A10" that appears BEFORE a
/// separate multi-area List rule that also starts at "A1:A10" (sqref="A1:A10 C1:C10") and carries
/// its cross-sheet source via the x14 extension, the old range-only match attached the x14
/// cross-sheet formula (and IsX14=true) to the unrelated Custom rule instead of the real List
/// rule, leaving the List rule's dropdown permanently inert.
///
/// ClosedXML's own fluent API cannot author two overlapping validations on the same worksheet, so
/// the fixture below builds a valid package via the real Save() path and then swaps in
/// hand-authored worksheet XML with the exact shape described by the finding, mirroring the
/// technique used by R99_DvSharedRangeNativeMetadataMismatchTests /
/// R99_HyperlinkRelationshipRebindTests. The file is then round-tripped through the real
/// XlsxFileAdapter.Load() entry point.
/// </summary>
public sealed class R99_X14ListPrecedingPlainRuleSameRangeTests
{
    private const string X14DvUri = "{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}";

    private const string WorksheetXmlCustomFirst =
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
              <formula1></formula1>
            </dataValidation>
          </dataValidations>
          <pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3" />
          <extLst>
            <ext uri="{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}"
                 xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                 xmlns:xm="http://schemas.microsoft.com/office/excel/2006/main">
              <x14:dataValidations count="1">
                <x14:dataValidation type="list" allowBlank="1" showInputMessage="1" showErrorMessage="1">
                  <x14:formula1><xm:f>Sheet2!$A$1:$A$5</xm:f></x14:formula1>
                  <xm:sqref>A1:A10 C1:C10</xm:sqref>
                </x14:dataValidation>
              </x14:dataValidations>
            </ext>
          </extLst>
        </worksheet>
        """;

    private const string WorksheetXmlListFirst =
        """
        <?xml version="1.0" encoding="utf-8" standalone="yes"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <dimension ref="A1:C10" />
          <sheetViews><sheetView workbookViewId="0" /></sheetViews>
          <sheetFormatPr defaultRowHeight="15" />
          <sheetData />
          <dataValidations count="2">
            <dataValidation type="list" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="A1:A10 C1:C10">
              <formula1></formula1>
            </dataValidation>
            <dataValidation type="custom" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="A1:A10">
              <formula1>ISNUMBER(A1)</formula1>
            </dataValidation>
          </dataValidations>
          <pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3" />
          <extLst>
            <ext uri="{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}"
                 xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                 xmlns:xm="http://schemas.microsoft.com/office/excel/2006/main">
              <x14:dataValidations count="1">
                <x14:dataValidation type="list" allowBlank="1" showInputMessage="1" showErrorMessage="1">
                  <x14:formula1><xm:f>Sheet2!$A$1:$A$5</xm:f></x14:formula1>
                  <xm:sqref>A1:A10 C1:C10</xm:sqref>
                </x14:dataValidation>
              </x14:dataValidations>
            </ext>
          </extLst>
        </worksheet>
        """;

    /// <summary>
    /// R99: the plain Custom rule appears BEFORE the List rule in worksheet XML document order --
    /// the exact ordering the finding describes. Before the fix, FindExisting matched purely on
    /// primary-range row/col and returned the first DataValidation in document order (the Custom
    /// rule), so the Custom rule's Formula1 got overwritten with the cross-sheet List source and
    /// IsX14 was wrongly set to true, while the real List rule was left with its empty inert
    /// formula1 and IsX14=false -- an unrelated rule corrupted and the real dropdown broken.
    /// </summary>
    [Fact]
    public void Load_PlainCustomRulePrecedingX14ListRuleOnSameRange_MergesFormulaIntoListRuleNotCustomRule()
    {
        using var stream = CreateSourcePackage(WorksheetXmlCustomFirst);

        var loaded = new XlsxFileAdapter().Load(stream);
        var loadedSheet = loaded.Sheets.Single(s => s.Name == "Sheet1");

        loadedSheet.DataValidations.Should().HaveCount(2,
            "the x14 merge must not create a spurious third rule, nor collapse the two real rules into one");

        var customRule = loadedSheet.DataValidations.Should().ContainSingle(dv => dv.Type == DvType.Custom).Subject;
        customRule.Formula1.Should().Be("ISNUMBER(A1)",
            "the unrelated Custom rule's own formula must survive untouched by the x14 merge");
        customRule.IsX14.Should().BeFalse("the Custom rule was never an x14-backed rule and must not be flagged as one");

        var listRule = loadedSheet.DataValidations.Should().ContainSingle(dv => dv.Type == DvType.List).Subject;
        listRule.IsX14.Should().BeTrue("the List rule is the one actually backed by the x14 extension block");
        listRule.Formula1.Should().Be("=Sheet2!$A$1:$A$5",
            "the cross-sheet x14 formula must merge into the List rule (its true owner), not the Custom rule, " +
            "so the dropdown is not left permanently inert");
    }

    /// <summary>
    /// No-regression sibling / order independence: the same fixture with the two
    /// &lt;dataValidation&gt; elements in the OPPOSITE document order (List rule first, Custom rule
    /// second -- the order that happened to already work before the fix) must still produce the
    /// identical, correct result. This guards against a fix that only works for one specific
    /// document order.
    /// </summary>
    [Fact]
    public void Load_X14ListRulePrecedingPlainCustomRuleOnSameRange_StillMergesFormulaIntoListRuleOnly()
    {
        using var stream = CreateSourcePackage(WorksheetXmlListFirst);

        var loaded = new XlsxFileAdapter().Load(stream);
        var loadedSheet = loaded.Sheets.Single(s => s.Name == "Sheet1");

        loadedSheet.DataValidations.Should().HaveCount(2);

        var customRule = loadedSheet.DataValidations.Should().ContainSingle(dv => dv.Type == DvType.Custom).Subject;
        customRule.Formula1.Should().Be("ISNUMBER(A1)");
        customRule.IsX14.Should().BeFalse();

        var listRule = loadedSheet.DataValidations.Should().ContainSingle(dv => dv.Type == DvType.List).Subject;
        listRule.IsX14.Should().BeTrue();
        listRule.Formula1.Should().Be("=Sheet2!$A$1:$A$5");
    }

    // Builds a fully valid single-sheet .xlsx package (via a real adapter save, so every required
    // package part is already correct) and then swaps in hand-authored worksheet XML for
    // xl/worksheets/sheet1.xml, mirroring the technique used by
    // R99_DvSharedRangeNativeMetadataMismatchTests.CreateSourcePackage.
    private static MemoryStream CreateSourcePackage(string worksheetXml)
    {
        var workbook = new Workbook("R99-X14ListPrecedingPlainRule");
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
