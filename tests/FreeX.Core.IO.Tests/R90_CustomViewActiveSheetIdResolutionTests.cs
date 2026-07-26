using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round 90 finding R90-io-sheet-view-custom-views-5-1:
/// customWorkbookView/@activeSheetId is a reference to a &lt;sheet sheetId="N"&gt; element's
/// <c>sheetId</c> attribute -- NOT a 1-based sheet position. Excel never reuses/renumbers sheetId
/// when sheets are deleted/reordered, so a workbook whose &lt;sheets&gt; sheetId values have
/// drifted from position (extremely common: e.g. Sheet1 keeps sheetId=1 and a later-added Sheet3
/// keeps sheetId=3 even after an in-between sheet is deleted) must resolve activeSheetId by
/// matching sheetId, not by treating the raw value as (position + 1).
///
/// Covers both directions of the bug:
/// - READ: XlsxWorkbookMetadataMapper.ToCustomView / BuildSheetIdToIndexMap, exercised through the
///   real product entry point XlsxFileAdapter.Load.
/// - WRITE: XlsxCustomViewMapper.GetActiveSheetId. This is exercised by calling
///   XlsxCustomViewMapper.Save directly (the exact production method -- not a re-implementation)
///   rather than through the public XlsxFileAdapter.Save entry point: a full ClosedXML rebuild
///   (which is what XlsxFileAdapter.Save falls back to whenever a workbook's CustomViews changed,
///   since that "delta" isn't patch-savable) ALWAYS renumbers &lt;sheets&gt;/@sheetId sequentially
///   to match position, which destroys the very divergent-sheetId precondition this test needs
///   before XlsxCustomViewMapper.Save ever runs -- there is no way to reach this code with a
///   divergent sheetId intact via the public Save() entry point. Calling XlsxCustomViewMapper.Save
///   directly against a hand-built package (mirroring the state a patch-save preserves verbatim
///   from a real Excel-authored file) is the correct-altitude seam for this specific defect.
/// </summary>
public sealed class R90_CustomViewActiveSheetIdResolutionTests
{
    private const string WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static MemoryStream BuildTwoSheetPackage(
        string sheet1Id, string sheet2Id, string? customWorkbookViewsXml, string? sheet2CustomViewGuid = null)
    {
        var worksheetXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData/>
            </worksheet>
            """;

        // The custom-view state list on the workbook model is only populated when a matching
        // customSheetView (by guid) exists on the sheet the view is active for -- see
        // XlsxFileAdapter.cs's customViewStatesById gate. Sheet3 (sheet2.xml) is where these tests'
        // custom views are active.
        var sheet2Xml = sheet2CustomViewGuid is null
            ? worksheetXml
            : $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData/>
              <customSheetViews>
                <customSheetView guid="{sheet2CustomViewGuid}" state="visible"/>
              </customSheetViews>
            </worksheet>
            """;

        var workbookXml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="{sheet1Id}" r:id="rId1"/>
                <sheet name="Sheet3" sheetId="{sheet2Id}" r:id="rId2"/>
              </sheets>
              {customWorkbookViewsXml}
            </workbook>
            """;

        var workbookRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                Target="worksheets/sheet2.xml"/>
            </Relationships>
            """;

        var packageRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
                Target="xl/workbook.xml"/>
            </Relationships>
            """;

        var contentTypes = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/worksheets/sheet2.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """;

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", contentTypes);
            Write(archive, "_rels/.rels", packageRels);
            Write(archive, "xl/workbook.xml", workbookXml);
            Write(archive, "xl/_rels/workbook.xml.rels", workbookRels);
            Write(archive, "xl/worksheets/sheet1.xml", worksheetXml);
            Write(archive, "xl/worksheets/sheet2.xml", sheet2Xml);
        }

        ms.Position = 0;
        return ms;

        static void Write(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }

    private static XElement ReadActiveSheetIdAttribute(Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var entryStream = entry.Open();
        var doc = XDocument.Load(entryStream);
        XNamespace ns = WorkbookNs;
        return doc.Root!
            .Element(ns + "customWorkbookViews")!
            .Element(ns + "customWorkbookView")!;
    }

    // ── READ side: activeSheetId must be resolved via sheetId, not (position + 1) ──

    [Fact]
    public void Load_ActiveSheetIdReferencesSheetIdOfLaterSheet_ResolvesToCorrectPosition()
    {
        // Sheet1 has sheetId=1 (position 0), Sheet3 has sheetId=3 (position 1) -- as would remain
        // after an in-between sheet (former sheetId=2) was deleted. The saved custom view was
        // active on Sheet3 (sheetId=3), so activeSheetId="3" must resolve to position 1.
        //
        // Before the fix: ReadIntAttribute("activeSheetId") - 1 == 3 - 1 == 2, out of range for a
        // 2-sheet workbook, so XlsxFileAdapter.cs's bounds guard drops it to null entirely.
        var customWorkbookViews = """
              <customWorkbookViews>
                <customWorkbookView name="V1" guid="{11111111-1111-1111-1111-111111111111}" activeSheetId="3" maximized="0" xWindow="0" yWindow="0" windowWidth="1000" windowHeight="600"/>
              </customWorkbookViews>
            """;
        using var pkg = BuildTwoSheetPackage("1", "3", customWorkbookViews, sheet2CustomViewGuid: "{11111111-1111-1111-1111-111111111111}");

        var workbook = new XlsxFileAdapter().Load(pkg);

        var view = workbook.CustomViews.Should().ContainSingle().Subject;
        view.ActiveSheetIndex.Should().Be(1, "activeSheetId=3 refers to Sheet3's sheetId, which sits at position 1, not position (3-1)=2");
    }

    [Fact]
    public void Load_ActiveSheetIdContiguousWithPosition_StillResolvesCorrectly()
    {
        // No-regression sibling: sheetId happens to equal position+1 (the common/simple case Excel
        // produces for a workbook that has never had a sheet deleted/reordered). activeSheetId="2"
        // must still resolve to position 1 both before and after the fix.
        var customWorkbookViews = """
              <customWorkbookViews>
                <customWorkbookView name="V1" guid="{22222222-2222-2222-2222-222222222222}" activeSheetId="2" maximized="0" xWindow="0" yWindow="0" windowWidth="1000" windowHeight="600"/>
              </customWorkbookViews>
            """;
        using var pkg = BuildTwoSheetPackage("1", "2", customWorkbookViews, sheet2CustomViewGuid: "{22222222-2222-2222-2222-222222222222}");

        var workbook = new XlsxFileAdapter().Load(pkg);

        var view = workbook.CustomViews.Should().ContainSingle().Subject;
        view.ActiveSheetIndex.Should().Be(1);
    }

    // ── WRITE side: GetActiveSheetId must emit the real sheetId, not (position + 1) ──
    //
    // XlsxCustomViewMapper.Save is called directly here (the exact production method) against a
    // hand-built package whose <sheets> already carries divergent sheetIds -- see the class-level
    // doc comment for why the public XlsxFileAdapter.Save entry point cannot preserve that
    // precondition for a CustomViews change (ClosedXML's full rebuild always renumbers sheetId
    // sequentially).

    [Fact]
    public void Save_NewCustomViewActiveOnLaterSheetWithDivergentSheetId_WritesRealSheetId()
    {
        // <sheets> already has divergent sheetIds (Sheet1=sheetId 1 at position 0, Sheet3=sheetId 3
        // at position 1 -- as a workbook with sheet-deletion history, or a patch-saved workbook,
        // would carry). A custom view active on position 1 (Sheet3, real sheetId=3) must write
        // activeSheetId="3", not "2" (position+1).
        using var pkg = BuildTwoSheetPackage("1", "3", customWorkbookViewsXml: null);
        var workbook = new Workbook("DivergentSheetIdWrite");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet3");
        workbook.CustomViews.Add(new WorkbookCustomView(
            "NewView",
            [new WorksheetCustomViewState("Sheet3", WorksheetViewMode.Normal, FrozenRows: 0, FrozenCols: 0, SplitRow: null, SplitColumn: null)],
            Id: "{33333333-3333-3333-3333-333333333333}",
            ActiveSheetIndex: 1));

        XlsxCustomViewMapper.Save(pkg, workbook);

        var customWorkbookView = ReadActiveSheetIdAttribute(pkg);
        customWorkbookView.Attribute("activeSheetId")!.Value.Should().Be("3",
            "position 1 corresponds to Sheet3, whose real sheetId is 3, not position+1=2");
    }

    [Fact]
    public void Save_NewCustomViewActiveOnLaterSheetWithContiguousSheetId_WritesPositionPlusOne()
    {
        // No-regression sibling: contiguous sheetIds (1, 2) -- the common case -- must still write
        // activeSheetId="2" for position 1, matching both the old and new code's output.
        using var pkg = BuildTwoSheetPackage("1", "2", customWorkbookViewsXml: null);
        var workbook = new Workbook("ContiguousSheetIdWrite");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet3");
        workbook.CustomViews.Add(new WorkbookCustomView(
            "NewView",
            [new WorksheetCustomViewState("Sheet3", WorksheetViewMode.Normal, FrozenRows: 0, FrozenCols: 0, SplitRow: null, SplitColumn: null)],
            Id: "{44444444-4444-4444-4444-444444444444}",
            ActiveSheetIndex: 1));

        XlsxCustomViewMapper.Save(pkg, workbook);

        var customWorkbookView = ReadActiveSheetIdAttribute(pkg);
        customWorkbookView.Attribute("activeSheetId")!.Value.Should().Be("2");
    }
}
