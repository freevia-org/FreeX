using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R42-io-sheet-tab-order-activetab-3-1: workbookView/@activeTab (and @firstSheet) are indices
/// into the workbook's FULL &lt;sheets&gt; order (worksheets AND chartsheets, interspersed, exactly
/// like <see cref="FreeX.Core.IO"/>'s internal XlsxChartsheet.WorkbookSheetIndex). Previously,
/// XlsxFileAdapter assigned workbook.ActiveSheetIndex/FirstVisibleSheetIndex clamped against
/// ClosedXML's worksheet-only count BEFORE chartsheets were spliced into workbook.Sheets at their
/// original interspersed position by InsertChartsheets -- so once a chartsheet was spliced in
/// before the activeTab position, the stored index silently pointed at the chartsheet (or an
/// unrelated worksheet) instead of the worksheet that was actually active when the file was saved.
/// </summary>
public sealed class R42_ChartsheetActiveTabTests
{
    private static string ChartsheetCorpusPath() =>
        TestWorkspaceFiles.FindWorkspaceFile(
            "test-corpus", "public", "tealeg-xlsx", "testchartsheet.xlsx");

    // The fixture's <sheets> order is [Chart1 (chartsheet, index 0), Sheet1 (worksheet, index 1)]
    // with no workbookView/@activeTab attribute; this helper injects one so the load path exercises
    // a real interspersed chartsheet+worksheet tab order with a specific saved active tab, exactly
    // as a real Excel-saved file would carry it.
    private static byte[] WithActiveTab(int activeTab)
    {
        var bytes = TestWorkspaceFiles.ReadWorkspaceBytes(
            "test-corpus", "public", "tealeg-xlsx", "testchartsheet.xlsx");
        using var stream = new MemoryStream();
        stream.Write(bytes, 0, bytes.Length);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument doc;
            using (var entryStream = entry.Open())
                doc = XDocument.Load(entryStream);

            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookView = doc.Root!.Element(ns + "bookViews")!.Element(ns + "workbookView")!;
            workbookView.SetAttributeValue("activeTab", activeTab.ToString());

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/workbook.xml");
            using var writer = newEntry.Open();
            doc.Save(writer);
        }

        return stream.ToArray();
    }

    [Fact]
    public void Load_ActiveTabPointsPastAnInterspersedChartsheet_ResolvesToTheOriginallyActiveWorksheet()
    {
        // <sheets> order is [Chart1 (chartsheet), Sheet1 (worksheet)]; activeTab=1 means Sheet1
        // (the worksheet) was active when the file was last saved in Excel.
        var bytes = WithActiveTab(activeTab: 1);

        using var stream = new MemoryStream(bytes, writable: false);
        var workbook = new XlsxFileAdapter().Load(stream);

        workbook.Sheets.Select(s => s.Name).Should().BeEquivalentTo(["Chart1", "Sheet1"]);
        workbook.ActiveSheetIndex.Should().NotBeNull();
        workbook.Sheets[workbook.ActiveSheetIndex!.Value].Name.Should().Be(
            "Sheet1",
            "activeTab=1 refers to the worksheet's position in the FULL (chartsheet-inclusive) tab " +
            "order, so the loaded active sheet must be the worksheet, not the chartsheet spliced in " +
            "ahead of it");
    }

    [Fact]
    public void Load_ActiveTabPointsAtTheChartsheetItself_ResolvesToTheChartsheet()
    {
        // Sibling/no-regression case: activeTab=0 (Chart1, the chartsheet) must still resolve to
        // the chartsheet itself -- confirming the fix does not just always skip past chartsheets.
        var bytes = WithActiveTab(activeTab: 0);

        using var stream = new MemoryStream(bytes, writable: false);
        var workbook = new XlsxFileAdapter().Load(stream);

        workbook.ActiveSheetIndex.Should().NotBeNull();
        workbook.Sheets[workbook.ActiveSheetIndex!.Value].Name.Should().Be("Chart1");
    }
}
