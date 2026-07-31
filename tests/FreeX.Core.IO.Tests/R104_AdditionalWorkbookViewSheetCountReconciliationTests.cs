using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for the round 104 finding
/// "Additional (multi-window) bookViews/workbookView activeTab/firstSheet never reconciled
/// against sheet-count changes" (XlsxWorkbookAdditionalViewMapper.cs).
///
/// Excel's "View &gt; New Window" writes a SECOND (or further) &lt;workbookView&gt; under
/// &lt;bookViews&gt; for each extra window, each carrying its own activeTab/firstSheet index into
/// the workbook's sheet-tab order. FreeX only reconciles the PRIMARY workbookView's activeTab/
/// firstSheet against the current sheet count on save (XlsxWorkbookMetadataXmlHelper.
/// ClampToVisibleSheetIndex, invoked from XlsxWorkbookMetadataWriter). Every additional
/// workbookView was preserved as an opaque blob and re-emitted verbatim by
/// XlsxWorkbookAdditionalViewMapper.ApplyToWorkbookXml with no equivalent clamping, so once the
/// user deleted/reordered sheets in FreeX, the secondary window's activeTab could point past the
/// end of the (now smaller) sheet-tab order or at a completely different sheet.
///
/// These tests drive the real product entry point: XlsxFileAdapter.Load followed by
/// Workbook.RemoveSheet and XlsxFileAdapter.Save, exactly as the app's own open/edit/save
/// pipeline would for a user deleting a sheet from a workbook that carries multi-window bookViews.
/// </summary>
public sealed class R104_AdditionalWorkbookViewSheetCountReconciliationTests
{
    private const string WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static MemoryStream BuildThreeSheetWorkbookWithSecondWindow()
    {
        // Build a real 3-sheet workbook via ClosedXML (a genuine xlsx package), then hand-inject a
        // second <workbookView> the way Excel would after "View > New Window" with the 2nd window
        // focused on Sheet3 (index 2 -- a valid index for 3 sheets at the time it was saved).
        using var xl = new XLWorkbook();
        xl.Worksheets.Add("Sheet1");
        xl.Worksheets.Add("Sheet2");
        xl.Worksheets.Add("Sheet3");

        using var built = new MemoryStream();
        xl.SaveAs(built);
        built.Position = 0;

        var ms = new MemoryStream();
        built.CopyTo(ms);
        ms.Position = 0;

        using (var archive = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument doc;
            using (var entryStream = entry.Open())
                doc = XDocument.Load(entryStream);

            XNamespace ns = WorkbookNs;
            var bookViews = doc.Root!.Element(ns + "bookViews")!;
            bookViews.Add(new XElement(ns + "workbookView",
                new XAttribute("activeTab", "2"),
                new XAttribute("windowWidth", "5000"),
                new XAttribute("windowHeight", "5000")));

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/workbook.xml");
            using var writer = new StreamWriter(newEntry.Open());
            doc.Save(writer);
        }

        ms.Position = 0;
        return ms;
    }

    private static XElement[] ReadWorkbookViews(Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var entryStream = entry.Open();
        var doc = XDocument.Load(entryStream);
        XNamespace ns = WorkbookNs;
        return doc.Root!.Element(ns + "bookViews")!.Elements(ns + "workbookView").ToArray();
    }

    [Fact]
    public void Save_AfterDeletingSheet_ClampsAdditionalWorkbookViewActiveTabIntoRange()
    {
        using var source = BuildThreeSheetWorkbookWithSecondWindow();

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        // Delete "Sheet2" -- only Sheet1 (index 0) and Sheet3 (index 1) remain afterwards, so the
        // additional view's stale activeTab="2" from before the delete is now out of range.
        var sheet2 = workbook.GetSheetAt(1);
        sheet2.Name.Should().Be("Sheet2");
        workbook.RemoveSheet(sheet2.Id).Should().BeTrue();
        workbook.Sheets.Count.Should().Be(2);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var views = ReadWorkbookViews(saved);
        views.Should().HaveCount(2, "the primary view plus the one additional (multi-window) view must both survive the save");

        var primaryActiveTab = int.Parse(views[0].Attribute("activeTab")?.Value ?? "0");
        primaryActiveTab.Should().BeInRange(0, 1, "the primary view was already reconciled before this fix");

        var additionalActiveTabAttribute = views[1].Attribute("activeTab");
        additionalActiveTabAttribute.Should().NotBeNull("the additional view must still carry an activeTab attribute");
        var additionalActiveTab = int.Parse(additionalActiveTabAttribute!.Value);
        additionalActiveTab.Should().BeInRange(0, 1,
            "the additional (multi-window) view's activeTab must be reconciled against the new (smaller) sheet count, " +
            "exactly like the primary view already is -- an out-of-range index here is not something real Excel would ever " +
            "produce after a sheet-count change");
    }

    [Fact]
    public void Save_WithoutSheetCountChange_PreservesAdditionalWorkbookViewActiveTabUnchanged()
    {
        // No-regression sibling: when the sheet count/order is untouched, the additional view's
        // already-valid activeTab must round-trip unchanged (the fix must not blindly rewrite every
        // additional view -- only clamp when the existing value is actually out of range).
        using var source = BuildThreeSheetWorkbookWithSecondWindow();

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.Sheets.Count.Should().Be(3);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var views = ReadWorkbookViews(saved);
        views.Should().HaveCount(2);

        var additionalActiveTab = int.Parse(views[1].Attribute("activeTab")!.Value);
        additionalActiveTab.Should().Be(2, "activeTab=2 is still a valid index into the unchanged 3-sheet workbook and must be preserved verbatim");
    }
}
