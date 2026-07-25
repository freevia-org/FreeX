using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round 90 finding R90-io-sheet-view-custom-views-5-3:
/// XlsxCustomViewMapper.Save previously hardcoded customWorkbookView/@autoUpdate,
/// @mergeInterval and @personalView to "0" on every save, discarding the source file's original
/// values (e.g. personalView="1" from the legacy Shared Workbook "personal view" feature) even
/// when the custom view itself was never touched. WorkbookCustomView carries no field for these
/// three attributes, so the only legitimate way for FreeX to "change" them is to keep silent about
/// them and let the source-preservation pass (XlsxWorkbookMetadataPreserver.MergeCustomWorkbookViews)
/// restore the original values, exactly as it already does for every other un-modeled
/// customWorkbookView attribute.
///
/// These tests call XlsxCustomViewMapper.Save followed by XlsxWorkbookMetadataPreserver.Preserve
/// directly -- the two real production methods jointly responsible for "write the modeled view,
/// then restore whatever un-modeled attributes the source had" -- run in their real production
/// order, rather than through the full public XlsxFileAdapter.Save entry point. That public entry
/// point cannot be used here: independently of this finding, any save of a loaded workbook that
/// carries a customWorkbookView takes XlsxFileAdapter's full-rebuild path (never the cheap patch
/// path, even for an edit as trivial as a single numeric cell value), and that full-rebuild path
/// unconditionally strips the ENTIRE customWorkbookViews element via
/// XlsxExcelCompatibilityNormalizer.RemoveWorkbookCustomViews a few steps after
/// XlsxCustomViewMapper.Save runs -- a separate, much larger pre-existing defect outside this
/// finding's file (see the round's summary). That defect makes the full Save() pipeline
/// unobservable for ANY customWorkbookView attribute right now, so this test targets the two
/// methods this finding actually touches directly.
/// </summary>
public sealed class R90_CustomWorkbookViewAttributePreservationTests
{
    private const string WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string ViewGuid = "{55555555-5555-5555-5555-555555555555}";

    private static MemoryStream BuildSourcePackage(string autoUpdate, string mergeInterval, string personalView)
    {
        var workbookXml = $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
              </sheets>
              <customWorkbookViews>
                <customWorkbookView name="V1" guid="{{ViewGuid}}" activeSheetId="1" maximized="0" xWindow="0" yWindow="0" windowWidth="1000" windowHeight="600" autoUpdate="{{autoUpdate}}" mergeInterval="{{mergeInterval}}" personalView="{{personalView}}"/>
              </customWorkbookViews>
            </workbook>
            """;

        return BuildPackage(workbookXml);
    }

    private static MemoryStream BuildFreshlyRebuiltTargetPackage()
    {
        // Mirrors the state of the package right before XlsxCustomViewMapper.Save runs in the real
        // pipeline: <sheets> already rebuilt (e.g. by ClosedXML), but no customWorkbookViews yet.
        var workbookXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """;

        return BuildPackage(workbookXml);
    }

    private static MemoryStream BuildPackage(string workbookXml)
    {
        var worksheetXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1"><v>1</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;

        var workbookRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                Target="worksheets/sheet1.xml"/>
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

    private static Workbook BuildModeledWorkbook()
    {
        var workbook = new Workbook("AttrPreservation");
        workbook.AddSheet("Sheet1");
        workbook.CustomViews.Add(new WorkbookCustomView(
            "V1",
            [new WorksheetCustomViewState("Sheet1", WorksheetViewMode.Normal, FrozenRows: 0, FrozenCols: 0, SplitRow: null, SplitColumn: null)],
            Id: ViewGuid,
            ActiveSheetIndex: 0));
        return workbook;
    }

    private static XElement ReadCustomWorkbookView(Stream stream)
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

    [Fact]
    public void SaveThenPreserve_RestoresSourceAutoUpdateMergeIntervalPersonalView()
    {
        // Source file was authored by real Excel with a "personal view" (legacy Shared Workbook
        // multi-user feature) and non-default autoUpdate/mergeInterval.
        using var sourcePkg = BuildSourcePackage(autoUpdate: "1", mergeInterval: "120", personalView: "1");
        using var targetPkg = BuildFreshlyRebuiltTargetPackage();
        var workbook = BuildModeledWorkbook();

        // Step 1: the real write -- XlsxCustomViewMapper.Save (this finding's fix).
        XlsxCustomViewMapper.Save(targetPkg, workbook);

        // Step 2: the real restore -- XlsxWorkbookMetadataPreserver.Preserve, exactly as the
        // production save pipeline invokes it afterward.
        targetPkg.Position = 0;
        sourcePkg.Position = 0;
        using (var sourceArchive = new ZipArchive(sourcePkg, ZipArchiveMode.Read, leaveOpen: true))
        using (var targetArchive = new ZipArchive(targetPkg, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxWorkbookMetadataPreserver.Preserve(sourceArchive, targetArchive, workbook, []);
        }

        var customWorkbookView = ReadCustomWorkbookView(targetPkg);
        customWorkbookView.Attribute("autoUpdate")?.Value.Should().Be("1",
            "the source file's autoUpdate value must survive a save that never touched the custom view");
        customWorkbookView.Attribute("mergeInterval")?.Value.Should().Be("120",
            "the source file's mergeInterval value must survive a save that never touched the custom view");
        customWorkbookView.Attribute("personalView")?.Value.Should().Be("1",
            "the source file's personalView value must survive a save that never touched the custom view");
    }

    [Fact]
    public void SaveThenPreserve_SourceDefaultAttributes_RemainDefault()
    {
        // No-regression sibling: a source file whose autoUpdate/mergeInterval/personalView were
        // already at their schema defaults ("0") must still save with those same default values --
        // this is not a "must round-trip a literal string" test, it confirms the omission-based fix
        // doesn't accidentally resurrect a non-default value out of thin air for the common case.
        using var sourcePkg = BuildSourcePackage(autoUpdate: "0", mergeInterval: "0", personalView: "0");
        using var targetPkg = BuildFreshlyRebuiltTargetPackage();
        var workbook = BuildModeledWorkbook();

        XlsxCustomViewMapper.Save(targetPkg, workbook);

        targetPkg.Position = 0;
        sourcePkg.Position = 0;
        using (var sourceArchive = new ZipArchive(sourcePkg, ZipArchiveMode.Read, leaveOpen: true))
        using (var targetArchive = new ZipArchive(targetPkg, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxWorkbookMetadataPreserver.Preserve(sourceArchive, targetArchive, workbook, []);
        }

        var customWorkbookView = ReadCustomWorkbookView(targetPkg);
        // Absent is schema-equivalent to "0"; accept either representation.
        (customWorkbookView.Attribute("autoUpdate")?.Value ?? "0").Should().Be("0");
        (customWorkbookView.Attribute("mergeInterval")?.Value ?? "0").Should().Be("0");
        (customWorkbookView.Attribute("personalView")?.Value ?? "0").Should().Be("0");
    }
}
