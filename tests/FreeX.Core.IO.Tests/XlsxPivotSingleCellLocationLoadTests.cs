using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for pivot tables whose &lt;location ref="..."&gt; collapses to a single
/// cell (e.g. "D6"). Excel emits a single-cell location ref for freshly-anchored or empty pivot
/// tables; the loader must accept it instead of throwing <see cref="System.FormatException"/>.
/// Reproduces the total load failure of contextures workbook
/// 02_pivots-slicers_region-sales.xlsm (pivots + slicers).
/// </summary>
public sealed partial class XlsxPivotSingleCellLocationLoadTests
{
    [Fact]
    public void Load_PivotTableWithSingleCellLocationRef_LoadsSuccessfully()
    {
        using var package = CreatePivotPackageWithSingleCellLocation();

        var workbook = new XlsxFileAdapter().Load(package);

        workbook.SheetCount.Should().BeGreaterThan(0);
        var pivots = workbook.Sheets.SelectMany(sheet => sheet.PivotTables).ToArray();
        pivots.Should().ContainSingle("the single-cell-anchored pivot table must survive load");

        var pivot = pivots[0];
        pivot.TargetRange.Start.ToA1().Should().Be("D6");
        pivot.TargetRange.End.ToA1().Should().Be("D6", "a single-cell location collapses to a degenerate 1x1 range");
    }

    private static MemoryStream CreatePivotPackageWithSingleCellLocation()
    {
        var workbook = new Workbook("PivotSingleCellLocation");
        var sheet = workbook.AddSheet("PivotData");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
            CreatedVersion = 8,
            MinRefreshableVersion = 4,
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", 4));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(
                new CellAddress(sheet.Id, 6, 4),
                new CellAddress(sheet.Id, 9, 5)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 4));
        sheet.PivotTables.Add(pivot);

        var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);
        CollapsePivotLocationToSingleCell(saved, "xl/pivotTables/pivotTable1.xml", "D6");
        saved.Position = 0;
        return saved;
    }

    private static void CollapsePivotLocationToSingleCell(MemoryStream stream, string entryName, string singleCellRef)
    {
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var entry = archive.GetEntry(entryName)
                ?? throw new InvalidOperationException($"Missing package part '{entryName}'.");

            XDocument document;
            using (var read = entry.Open())
            {
                document = XDocument.Load(read);
            }

            var location = document.Root!.Element(workbookNs + "location")
                ?? throw new InvalidOperationException("Pivot table is missing its <location> element.");
            location.SetAttributeValue("ref", singleCellRef);

            entry.Delete();
            var replacement = archive.CreateEntry(entryName);
            using var write = replacement.Open();
            document.Save(write);
        }

        stream.Position = 0;
    }
}
