using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for R33-io-worksheet-props-deep-2: XlsxWorksheetDimensionDefaultsWriter.Save wrote a
/// live customHeight="1" for a genuinely new DefaultRowHeight, then immediately clobbered it back to the
/// stale bag value captured from the source file's sheetFormatPr by its own ApplyNativeSheetFormatMetadata
/// call, because customHeight was not excluded from the metadata-bag reapply.
/// </summary>
public sealed class R33_WorksheetCustomHeightRoundTripTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

    private static XDocument LoadPackageXml(ZipArchive archive, string path) =>
        XlsxPackageTestFixtures.LoadPackageXml(archive, path);

    private static void ReplacePackageXml(ZipArchive archive, string path, XDocument document)
    {
        archive.GetEntry(path)?.Delete();
        var replacement = archive.CreateEntry(path);
        using var replacementStream = replacement.Open();
        document.Save(replacementStream, SaveOptions.DisableFormatting);
    }

    // Injects a sheetFormatPr with an explicit customHeight and defaultRowHeight onto sheet1.xml, mimicking
    // a source file (or a prior round-trip through this same writer) that legally carries both attributes.
    private static void SetSourceSheetFormatPr(MemoryStream packageStream, string defaultRowHeightPoints, string customHeight)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            var sheetFormat = worksheetXml.Root!.Element(WorksheetNs + "sheetFormatPr");
            if (sheetFormat is null)
            {
                sheetFormat = new XElement(WorksheetNs + "sheetFormatPr");
                worksheetXml.Root!.AddFirst(sheetFormat);
            }

            sheetFormat.SetAttributeValue("defaultRowHeight", defaultRowHeightPoints);
            sheetFormat.SetAttributeValue("customHeight", customHeight);
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static MemoryStream CreateBaselinePackage()
    {
        var workbook = new Workbook("CustomHeightRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("data"));

        var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
        source.Position = 0;
        return source;
    }

    [Fact]
    public void PatchSave_NewDefaultRowHeight_SetsCustomHeightAndDoesNotRevertToStaleBagValue()
    {
        var adapter = new XlsxFileAdapter();
        var source = CreateBaselinePackage();

        // Source carries a valid but stale customHeight="0" alongside an explicit defaultRowHeight, as a
        // non-Excel writer (or a prior round-trip) might.
        SetSourceSheetFormatPr(source, "15", "0");

        source.Position = 0;
        var loaded = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(loaded);
        var loadedSheet = loaded.GetSheetAt(0);

        // 15pt == the standard 20px default row height, so this is a normal "unmodified default" load.
        loadedSheet.DefaultRowHeight.Should().BeApproximately(20.0, 0.01);

        // The user now sets a genuinely new, non-default default row height and edits a cell.
        loadedSheet.DefaultRowHeight = 30.0;
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var sheetFormat = worksheetXml.Root!.Element(WorksheetNs + "sheetFormatPr");
        sheetFormat.Should().NotBeNull();

        // New row height in points (30px * 72/96 = 22.5pt).
        sheetFormat!.Attribute("defaultRowHeight")!.Value.Should().Be("22.5");
        sheetFormat.Attribute("customHeight")!.Value.Should().Be("1");
    }

    [Fact]
    public void PatchSave_UnchangedDefaultRowHeight_PreservesBagCustomHeightUnchanged()
    {
        var adapter = new XlsxFileAdapter();
        var source = CreateBaselinePackage();

        // Source explicitly flags customHeight="1" even though the row height itself is the standard default;
        // this is a legitimate "user set this exact height on purpose" flag that must survive an unrelated
        // resave that never touches DefaultRowHeight.
        SetSourceSheetFormatPr(source, "15", "1");

        source.Position = 0;
        var loaded = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(loaded);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.DefaultRowHeight.Should().BeApproximately(20.0, 0.01);
        loadedSheet.SheetFormatMetadata.Should().NotBeNull();

        // Do not touch DefaultRowHeight; edit something unrelated so the sheet still round-trips.
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var sheetFormat = worksheetXml.Root!.Element(WorksheetNs + "sheetFormatPr");
        sheetFormat.Should().NotBeNull();
        sheetFormat!.Attribute("customHeight")!.Value.Should().Be("1");
    }
}
