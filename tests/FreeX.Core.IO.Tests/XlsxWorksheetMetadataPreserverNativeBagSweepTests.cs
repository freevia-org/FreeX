using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-40 native-bag-resurrection-sweep regression tests. All three findings share the same root
/// cause: XlsxWorksheetMetadataPreserver.MergeWorksheetSheetProperties (sheetPr, incl. its
/// pageSetUpPr/outlinePr children) and MergeWorksheetSheetFormatProperties (sheetFormatPr) copied
/// stale pre-edit source attributes/children back onto a freshly-rebuilt worksheet whenever the
/// modeled writer legitimately omitted them (because the user cleared/reset a modeled value to its
/// Excel default). These tests exercise XlsxWorksheetMetadataPreserver.Preserve directly against a
/// hand-built "source" (stale, pre-edit) and "target" (freshly rebuilt, already reflecting the
/// cleared model) worksheet pair, mirroring XlsxWorksheetMetadataPreserverTests.cs's approach.
/// </summary>
public sealed class XlsxWorksheetMetadataPreserverNativeBagSweepTests
{
    private static MemoryStream CreateWorkbookPackage(string worksheetXml)
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Sheet1" sheetId="1" r:id="rId1" />
                  </sheets>
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                                Target="worksheets/sheet1.xml" />
                </Relationships>
                """);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        package.Position = 0;
        return package;
    }

    private static void WriteEntry(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(text);
    }

    private static XElement RunPreserveAndGetTargetRoot(string sourceWorksheetXml, string targetWorksheetXml)
    {
        using var sourcePackage = CreateWorkbookPackage(sourceWorksheetXml);
        using var targetPackage = CreateWorkbookPackage(targetWorksheetXml);
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);
        var workbook = new Workbook("Native bag sweep");
        workbook.AddSheet("Sheet1");

        XlsxWorksheetMetadataPreserver.Preserve(sourceArchive, targetArchive, workbook);

        var resultEntry = targetArchive.GetEntry("xl/worksheets/sheet1.xml")!;
        using var resultStream = resultEntry.Open();
        var document = XDocument.Load(resultStream);
        return document.Root!;
    }

    // Stale source: codeName (modeled -> Sheet.CodeName) + filterMode (native-only, unmodeled) both
    // present on sheetPr, plus pageSetUpPr/outlinePr (modeled -> Sheet.FitToPage/AutoPageBreaks/
    // OutlineSummaryBelow/etc.) children. A cell carries native cm="1" metadata purely to force the
    // preflight to treat this worksheet as non-plain so the full merge path runs.
    private const string StaleSourceWorksheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetPr codeName="Feuil1" filterMode="1">
            <tabColor rgb="FFFF0000" />
            <pageSetUpPr fitToPage="1" />
            <outlinePr summaryBelow="0" summaryRight="0" />
          </sheetPr>
          <dimension ref="A1:B1" />
          <sheetViews>
            <sheetView workbookViewId="0" />
          </sheetViews>
          <sheetFormatPr baseColWidth="12" defaultColWidth="12.5" defaultRowHeight="20" />
          <sheetData>
            <row r="1">
              <c r="A1" cm="1"><v>1</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    // Freshly-rebuilt target: the user cleared Sheet.CodeName, Sheet.FitToPage/AutoPageBreaks, and
    // Sheet.OutlineSummaryBelow/etc. (so the modeled writers omitted codeName/pageSetUpPr/outlinePr
    // entirely), and reset the default column width/row height back to Excel's defaults (so
    // XlsxWorksheetDimensionDefaultsWriter omitted defaultColWidth/defaultRowHeight too). A bare
    // sheetPr with just the (untouched) tabColor still exists, matching the real-world scenario that
    // routes the merge through the per-attribute/per-child path rather than the wholesale-AddFirst path.
    private const string ClearedTargetWorksheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetPr>
            <tabColor rgb="FFFF0000" />
          </sheetPr>
          <dimension ref="A1:B1" />
          <sheetViews>
            <sheetView workbookViewId="0" />
          </sheetViews>
          <sheetFormatPr defaultRowHeight="15" />
          <sheetData>
            <row r="1">
              <c r="A1"><v>1</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    [Fact]
    public void Preserve_ClearedCodeName_IsNotResurrectedFromStaleSource()
    {
        // R40-native-bag-resurrection-sweep-1
        var targetRoot = RunPreserveAndGetTargetRoot(StaleSourceWorksheetXml, ClearedTargetWorksheetXml);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var sheetPr = targetRoot.Element(ns + "sheetPr");
        sheetPr.Should().NotBeNull();
        sheetPr!.Attribute("codeName")
            .Should()
            .BeNull("the user cleared Sheet.CodeName, so the stale source codeName must not be resurrected");
    }

    [Fact]
    public void Preserve_UnrelatedNativeOnlySheetPropertiesAttribute_StillRoundTrips()
    {
        // Sibling no-regression for R40-native-bag-resurrection-sweep-1: a genuinely native-only
        // (unmodeled) sheetPr attribute missing from the target must still be carried forward.
        var targetRoot = RunPreserveAndGetTargetRoot(StaleSourceWorksheetXml, ClearedTargetWorksheetXml);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        targetRoot.Element(ns + "sheetPr")!.Attribute("filterMode")?.Value
            .Should()
            .Be("1", "filterMode is native-only (unmodeled) and must still be preserved from the source");
    }

    [Fact]
    public void Preserve_ClearedPageSetUpPrAndOutlinePr_AreNotResurrectedFromStaleSource()
    {
        // R40-native-bag-resurrection-sweep-3
        var targetRoot = RunPreserveAndGetTargetRoot(StaleSourceWorksheetXml, ClearedTargetWorksheetXml);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var sheetPr = targetRoot.Element(ns + "sheetPr");
        sheetPr.Should().NotBeNull();
        sheetPr!.Element(ns + "pageSetUpPr")
            .Should()
            .BeNull("the user turned Fit to Page back off, so the stale pageSetUpPr must not be resurrected");
        sheetPr.Element(ns + "outlinePr")
            .Should()
            .BeNull("the user reset the outline summary direction, so the stale outlinePr must not be resurrected");
        sheetPr.Element(ns + "tabColor")
            .Should()
            .NotBeNull("the untouched tabColor sibling must be unaffected by the pageSetUpPr/outlinePr exclusion");
    }

    [Fact]
    public void Preserve_LiveWriterPageSetUpPr_IsNotOverwrittenByStaleSourceValue()
    {
        // Sibling no-regression for R40-native-bag-resurrection-sweep-3: when the live writer already
        // wrote its own (current) pageSetUpPr, the stale source's pageSetUpPr must not be merged in on
        // top of / alongside it.
        const string targetWithLivePageSetUpPr = """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetPr>
                <tabColor rgb="FFFF0000" />
                <pageSetUpPr fitToPage="0" autoPageBreaks="1" />
              </sheetPr>
              <dimension ref="A1:B1" />
              <sheetViews>
                <sheetView workbookViewId="0" />
              </sheetViews>
              <sheetFormatPr defaultRowHeight="15" />
              <sheetData>
                <row r="1">
                  <c r="A1"><v>1</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;

        var targetRoot = RunPreserveAndGetTargetRoot(StaleSourceWorksheetXml, targetWithLivePageSetUpPr);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var pageSetUpPrElements = targetRoot.Element(ns + "sheetPr")!.Elements(ns + "pageSetUpPr").ToList();
        pageSetUpPrElements.Should().HaveCount(1, "the live pageSetUpPr must not be duplicated by a stale clone");
        pageSetUpPrElements[0].Attribute("fitToPage")?.Value
            .Should()
            .Be("0", "the live writer's current value must win over the stale source's fitToPage=\"1\"");
    }

    [Fact]
    public void Preserve_ClearedDefaultColumnWidthAndRowHeight_AreNotResurrectedFromStaleSource()
    {
        // R40-native-bag-resurrection-sweep-2
        var targetRoot = RunPreserveAndGetTargetRoot(StaleSourceWorksheetXml, ClearedTargetWorksheetXml);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var sheetFormatPr = targetRoot.Element(ns + "sheetFormatPr");
        sheetFormatPr.Should().NotBeNull();
        sheetFormatPr!.Attribute("defaultColWidth")
            .Should()
            .BeNull("the user reset the default column width to Excel's default, so it must not be resurrected");
        sheetFormatPr.Attribute("defaultRowHeight")?.Value
            .Should()
            .Be("15", "the live (already-default) defaultRowHeight must win, not the stale source's 20");
    }

    [Fact]
    public void Preserve_UnrelatedNativeOnlySheetFormatAttribute_StillRoundTrips()
    {
        // Sibling no-regression for R40-native-bag-resurrection-sweep-2: baseColWidth is genuinely
        // native-only (unmodeled) and missing from the target, so it must still be carried forward.
        var targetRoot = RunPreserveAndGetTargetRoot(StaleSourceWorksheetXml, ClearedTargetWorksheetXml);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        targetRoot.Element(ns + "sheetFormatPr")!.Attribute("baseColWidth")?.Value
            .Should()
            .Be("12", "baseColWidth is native-only (unmodeled) and must still be preserved from the source");
    }
}
