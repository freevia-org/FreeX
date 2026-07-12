using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

// R34-io-pagesetup-print-parts-1: ClosedXML's IXLPageSetup.FirstPageNumber reflects only the raw
// <pageSetup firstPageNumber="..."/> attribute value and has no property for the sibling
// useFirstPageNumber="0/1" checkbox flag. Real Excel commonly leaves a stale firstPageNumber value
// in the XML after the "First page number" checkbox is unchecked (useFirstPageNumber goes to "0"
// but the numeric value is left behind), so trusting ClosedXML's value unconditionally silently
// re-enables a disabled custom first-page-number on load+save.
public sealed class XlsxFirstPageNumberUseFlagTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void XlsxAdapter_Load_TreatsFirstPageNumberAsNullWhenUseFirstPageNumberIsOff()
    {
        var workbook = new Workbook("FirstPageNumberDisabled");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("data"));
        // Save with a first page number set so the pageSetup element carries a firstPageNumber
        // attribute, then flip useFirstPageNumber off below to simulate the real Excel scenario
        // (box unchecked, stale numeric value left in the file).
        sheet.FirstPageNumber = 5;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        SetUseFirstPageNumberAttribute(ms, "0");
        ms.Position = 0;

        var loadedSheet = adapter.Load(ms).GetSheetAt(0);

        loadedSheet.FirstPageNumber.Should().BeNull(
            "useFirstPageNumber=\"0\" means the checkbox was off even though a stale firstPageNumber value remained");
    }

    [Fact]
    public void XlsxAdapter_Load_PreservesFirstPageNumberWhenUseFirstPageNumberIsOn()
    {
        var workbook = new Workbook("FirstPageNumberEnabled");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("data"));
        sheet.FirstPageNumber = 5;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        // Sibling case: explicitly confirm useFirstPageNumber="1" (the genuinely-enabled state)
        // still round-trips the numeric value, so the fix doesn't over-correct the working case.
        SetUseFirstPageNumberAttribute(ms, "1");
        ms.Position = 0;

        var loadedSheet = adapter.Load(ms).GetSheetAt(0);

        loadedSheet.FirstPageNumber.Should().Be(5);
    }

    private static void SetUseFirstPageNumberAttribute(MemoryStream ms, string value)
    {
        using var archive = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        XDocument document;
        using (var entryStream = entry.Open())
        {
            document = XDocument.Load(entryStream);
        }

        var pageSetup = document.Root!.Element(WorksheetNs + "pageSetup")!;
        pageSetup.SetAttributeValue("firstPageNumber", "5");
        pageSetup.SetAttributeValue("useFirstPageNumber", value);

        entry.Delete();
        var updated = archive.CreateEntry("xl/worksheets/sheet1.xml");
        using var updatedStream = updated.Open();
        document.Save(updatedStream);
    }
}
