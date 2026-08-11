using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R133x-io-slicer-timeline-multipivot-writer: the audit for R133 (which fixed the source-preserved
/// PATCH-save path's <c>&lt;pivotTables&gt;</c> collapse, see <see cref="FreeXR133SlicerTimelineMultiPivotTests"/>)
/// asked whether the fresh/no-source-package writer (<see cref="XlsxSlicerTimelineWriter.SaveSlicerTimelines"/>)
/// carries the same single-name collapse.
/// <para>
/// It did: the writer only ever authored ONE <c>&lt;pivotTable name=".."/&gt;</c> element per slicer/timeline
/// cache, built from <see cref="SlicerModel.SourcePivotTableName"/>/<see cref="TimelineModel.SourcePivotTableName"/>
/// alone, never consulting <see cref="SlicerModel.ConnectedPivotTableNames"/>/<see cref="TimelineModel.ConnectedPivotTableNames"/>.
/// This path only runs when the workbook carries no preserved xlsx source package
/// (<c>XlsxFileAdapter.SavePostProcessing</c>'s <c>!hasSourcePackage</c> gate) -- which is reachable
/// whenever a multi-connection slicer/timeline was populated by a NON-xlsx load (FreeX's own native JSON
/// adapter round-trips <see cref="SlicerModel.ConnectedPivotTableNames"/>/<see cref="TimelineModel.ConnectedPivotTableNames"/>
/// too, see <c>NativeJsonAdapter.SlicerTimeline</c>) and the workbook is then saved AS xlsx for the first
/// time ("Save As"). These tests build a workbook entirely in-memory (mirroring that native-load shape --
/// never routed through <c>XlsxFileAdapter.Load</c>, so no source package is registered for it) with a
/// slicer/timeline whose <c>ConnectedPivotTableNames</c> already lists two pivot tables, and assert the
/// FIRST xlsx save authors both connections.
/// </para>
/// </summary>
public sealed class R133x_SlicerTimelineMultiPivotWriterTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static (Workbook Workbook, Sheet Sheet) BuildWorkbookWithTwoPivots()
    {
        var workbook = new Workbook("R133xMultiPivotWriter");
        var sheet = workbook.AddSheet("Data1");

        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));

        var cache = new PivotCacheModel { CacheId = 1, SourceType = PivotCacheSourceType.WorksheetRange, SourceSheetName = "Data1", SourceReference = "A1:B2" };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East"]));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot1 = new PivotTableModel
        {
            Name = "PT1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B2"),
            TargetRange = Range(sheet, "D3", "E6"),
        };
        pivot1.RowFields.Add(new PivotFieldModel(0));
        pivot1.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot1);

        var pivot2 = new PivotTableModel
        {
            Name = "PT2",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B2"),
            TargetRange = Range(sheet, "D12", "E15"),
        };
        pivot2.RowFields.Add(new PivotFieldModel(0));
        pivot2.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot2);

        return (workbook, sheet);
    }

    [Fact]
    public void FreshSave_SlicerWithTwoConnectedPivots_AuthorsBothPivotTableEntries()
    {
        var (workbook, _) = BuildWorkbookWithTwoPivots();

        // Mirrors what a NATIVE (non-xlsx) load populates -- ConnectedPivotTableNames already lists both
        // connections, but this workbook has never been through XlsxFileAdapter.Load, so no source
        // package is registered for it: the first xlsx save goes through the FRESH writer
        // (XlsxSlicerTimelineWriter.SaveSlicerTimelines), not the patch-save rewriter.
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PT1",
            SourceFieldName = "Region",
            ConnectedPivotTableNames = ["PT1", "PT2"],
        });

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        ReadAllPivotTableNames(saved, "xl/slicerCaches/slicerCache1.xml").Should().Equal(["PT1", "PT2"],
            "the fresh (no-source-package) writer must author EVERY connected pivot table, not just the primary one");
    }

    [Fact]
    public void FreshSave_TimelineWithTwoConnectedPivots_AuthorsBothPivotTableEntries()
    {
        var (workbook, _) = BuildWorkbookWithTwoPivots();

        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Region Timeline",
            CacheName = "Timeline_Region",
            SourcePivotTableName = "PT1",
            SourceFieldName = "Region",
            ConnectedPivotTableNames = ["PT1", "PT2"],
        });

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        ReadAllPivotTableNames(saved, "xl/timelineCaches/timelineCache1.xml").Should().Equal(["PT1", "PT2"],
            "the fresh (no-source-package) writer must author EVERY connected pivot table, not just the primary one");
    }

    [Fact]
    public void FreshSave_SlicerWithSingleConnection_StillAuthorsOnlyThatOne()
    {
        // No-regression sibling: the common single-pivot-connection case (ConnectedPivotTableNames
        // empty -- a slicer this codebase's own AddSlicerCommand created in-session) must keep
        // producing exactly the same single <pivotTable> entry as before this fix.
        var (workbook, _) = BuildWorkbookWithTwoPivots();

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PT1",
            SourceFieldName = "Region",
        });

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        ReadAllPivotTableNames(saved, "xl/slicerCaches/slicerCache1.xml").Should().Equal(["PT1"]);
    }

    private static string[] ReadAllPivotTableNames(MemoryStream package, string entryPath)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(entryPath);
        entry.Should().NotBeNull(entryPath);
        using var entryStream = entry!.Open();
        var xml = XDocument.Load(entryStream);
        var names = xml.Descendants()
            .Where(element => element.Name.LocalName == "pivotTable")
            .Select(element => element.Attribute("name")?.Value ?? "")
            .ToArray();
        package.Position = 0;
        return names;
    }
}
