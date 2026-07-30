using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round 100 finding io-dv-x14-wholecol-sqref: Excel stores a cross-sheet
/// List validation applied to an entire column/row (Data &gt; Data Validation on a selected
/// column/row header) with its real source only in the worksheet extLst x14 block, writing the
/// collapsed <c>&lt;xm:sqref&gt;A:A&lt;/xm:sqref&gt;</c> (or "1:1" for a row) notation -- neither
/// side of which carries both a column letter and a row number. <see
/// cref="XlsxX14DataValidationReader"/>.Apply used to parse that sqref via a helper that always
/// called <see cref="CellAddress.Parse"/>/<see cref="GridRange.Parse"/>, both of which throw a
/// <see cref="FormatException"/> for a bare column letter or a bare row digit with no partner.
/// That exception was swallowed by a bare "catch { continue; }", discarding the entire x14
/// metadata entry silently -- so the matching legacy &lt;dataValidation&gt; element (which
/// intentionally carries an empty &lt;formula1/&gt; per this reader's own doc comment, because the
/// real formula lives only in the x14 block) was left with Formula1 = null and IsX14 = false,
/// permanently losing the rule's real cross-sheet list source.
///
/// ClosedXML's fluent API cannot author a whole-column/row x14-backed List validation directly, so
/// the fixture below builds a valid package via the real Save() path and then swaps in
/// hand-authored worksheet XML with the exact shape Excel writes, mirroring the technique used by
/// R99_X14ListPrecedingPlainRuleSameRangeTests. The file is round-tripped through the real
/// <see cref="XlsxFileAdapter"/>.Load() entry point -- not a hand-built model or XML fragment.
/// </summary>
public sealed class R100_X14WholeColumnRowSqrefTests
{
    // NOTE on the two sqref forms below: Excel's LEGACY <dataValidation sqref=...> attribute always
    // uses an explicit bounded range even for a whole-column/row rule (e.g. "A1:A1048576"), while
    // only the newer x14/xm: extension's <xm:sqref> uses the compact collapsed notation ("A:A" /
    // "1:1") that omits the row/column number entirely. Both representations were captured from
    // real Excel-authored whole-column x14 List validations. This is exactly what makes the legacy
    // element resolvable by ClosedXML's loader while the x14 sqref alone hits the parser bug fixed
    // in XlsxX14DataValidationReader/XlsxDataValidationNativeMetadataMapper.
    private const string WorksheetXmlWholeColumn =
        """
        <?xml version="1.0" encoding="utf-8" standalone="yes"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <dimension ref="A1:C10" />
          <sheetViews><sheetView workbookViewId="0" /></sheetViews>
          <sheetFormatPr defaultRowHeight="15" />
          <sheetData />
          <dataValidations count="1">
            <dataValidation type="list" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="A1:A1048576">
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
                  <xm:sqref>A:A</xm:sqref>
                </x14:dataValidation>
              </x14:dataValidations>
            </ext>
          </extLst>
        </worksheet>
        """;

    private const string WorksheetXmlWholeRow =
        """
        <?xml version="1.0" encoding="utf-8" standalone="yes"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <dimension ref="A1:C10" />
          <sheetViews><sheetView workbookViewId="0" /></sheetViews>
          <sheetFormatPr defaultRowHeight="15" />
          <sheetData />
          <dataValidations count="1">
            <dataValidation type="list" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="A1:XFD1">
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
                  <xm:sqref>1:1</xm:sqref>
                </x14:dataValidation>
              </x14:dataValidations>
            </ext>
          </extLst>
        </worksheet>
        """;

    /// <summary>
    /// R100: whole-COLUMN cross-sheet List validation. Before the fix, ParseSqrefRanges threw
    /// FormatException on the bare-column token "A" (no row digits), the caller's
    /// "catch { continue; }" swallowed it, and the x14 merge never ran -- so the loaded rule kept
    /// the legacy element's empty Formula1 and IsX14 stayed false, silently losing the cross-sheet
    /// source. After the fix the whole-column sqref resolves to A1:A1048576 and the real formula
    /// merges in.
    /// </summary>
    [Fact]
    public void Load_WholeColumnCrossSheetListValidation_MergesX14FormulaInsteadOfLosingIt()
    {
        using var stream = CreateSourcePackage(WorksheetXmlWholeColumn);

        var loaded = new XlsxFileAdapter().Load(stream);
        var loadedSheet = loaded.Sheets.Single(s => s.Name == "Sheet1");

        var rule = loadedSheet.DataValidations.Should().ContainSingle().Subject;
        rule.Type.Should().Be(DvType.List);
        rule.IsX14.Should().BeTrue(
            "the x14 block is the rule's real source and must be recognized as merged, not discarded");
        rule.Formula1.Should().Be("=Sheet2!$A$1:$A$5",
            "the cross-sheet x14 formula must survive the whole-column sqref, not be silently lost");
        rule.AppliesTo.Start.Row.Should().Be(1);
        rule.AppliesTo.Start.Col.Should().Be(1);
        rule.AppliesTo.End.Row.Should().Be(CellAddress.MaxRow);
        rule.AppliesTo.End.Col.Should().Be(1);
    }

    /// <summary>
    /// No-regression sibling: the whole-ROW form ("1:1") must resolve identically -- this is the
    /// other collapsed-sqref shape the same x14 parser must handle, and a fix that only special-
    /// cased bare column letters (and not bare row digits, or vice-versa) would leave this half of
    /// the defect silently broken.
    /// </summary>
    [Fact]
    public void Load_WholeRowCrossSheetListValidation_MergesX14FormulaInsteadOfLosingIt()
    {
        using var stream = CreateSourcePackage(WorksheetXmlWholeRow);

        var loaded = new XlsxFileAdapter().Load(stream);
        var loadedSheet = loaded.Sheets.Single(s => s.Name == "Sheet1");

        var rule = loadedSheet.DataValidations.Should().ContainSingle().Subject;
        rule.Type.Should().Be(DvType.List);
        rule.IsX14.Should().BeTrue();
        rule.Formula1.Should().Be("=Sheet2!$A$1:$A$5");
        rule.AppliesTo.Start.Row.Should().Be(1);
        rule.AppliesTo.Start.Col.Should().Be(1);
        rule.AppliesTo.End.Row.Should().Be(1);
        rule.AppliesTo.End.Col.Should().Be(CellAddress.MaxCol);
    }

    // Builds a fully valid single-sheet .xlsx package (via a real adapter save, so every required
    // package part is already correct) and then swaps in hand-authored worksheet XML for
    // xl/worksheets/sheet1.xml, mirroring the technique used by
    // R99_X14ListPrecedingPlainRuleSameRangeTests.CreateSourcePackage.
    private static MemoryStream CreateSourcePackage(string worksheetXml)
    {
        var workbook = new Workbook("R100-X14WholeColumnRowSqref");
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
