using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// sheet-lifecycle F1 (R151): <c>Sheet.Clone.ClonePivotTable</c> copied <see cref="PivotTableModel.Name"/>
/// verbatim from the source, unlike the sibling structured-table path
/// (<c>DuplicateSheetCommand.UniquifyClonedTables</c>, R17 "two tables sharing an identity -> corrupt
/// XLSX") which mints a fresh, workbook-unique name for a cloned table. After Duplicate Sheet on a
/// sheet hosting a PivotTable with a connected Slicer, the workbook ended up with two
/// <see cref="PivotTableModel"/> instances -- one per sheet -- sharing the identical Name. Both
/// <c>XlsxSlicerTimelineWriter.ResolvePivotHostTabId</c> and its patch-save twin
/// <c>XlsxSlicerTimelineStateRewriter.ResolvePivotHostTabId</c> resolve "which sheet hosts pivot table
/// X" purely by NAME, scanning <c>workbook.Sheets</c> in order for the first match -- so because the
/// source sheet always precedes its own copy, the lookup for the COPY's own cloned slicer always
/// resolved back to the SOURCE sheet's tabId instead of the copy's own tabId, corrupting the saved
/// xl/slicerCaches/slicerCacheN.xml for the duplicate's own slicer.
/// </summary>
public sealed class R151_DuplicateSheetPivotTableNameUniquenessTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace SlicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    [Fact]
    public void DuplicateSheet_ClonedPivotTable_GetsWorkbookUniqueName()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 0, 0), new CellAddress(sheet.Id, 10, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 0, 5), new CellAddress(sheet.Id, 5, 7))
        });

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];

        // Sibling no-regression: the source sheet's own pivot table identity must be untouched.
        sheet.PivotTables.Should().ContainSingle().Which.Name.Should().Be("PivotTable1");

        // The fix: the copy's pivot table must NOT share the source's exact name.
        copy.PivotTables.Should().ContainSingle().Which.Name.Should().NotBe("PivotTable1");
    }

    /// <summary>
    /// The end-to-end proof: with a slicer connected to the pivot table on the sheet being
    /// duplicated, the CLONED slicer's own slicerCache must resolve its <c>pivotTable/@tabId</c> to
    /// the COPY sheet's own sheetId (where its pivot table and the slicer itself both actually live),
    /// not the source sheet's -- while the ORIGINAL slicer (sibling, untouched by the fix) must keep
    /// resolving to the source sheet's tabId exactly as before. This exercises both the model-level
    /// rename AND the writer's name-keyed tabId resolution together, so the two agree, rather than
    /// only asserting a raw string was produced somewhere in the saved package.
    /// </summary>
    [Fact]
    public void DuplicateSheet_WithConnectedSlicer_ClonedSlicerCacheResolvesToCopySheetTabId()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 0, 0), new CellAddress(sheet.Id, 10, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 0, 5), new CellAddress(sheet.Id, 5, 7))
        });

        var slicer = new SlicerModel
        {
            Name = "Slicer_Region",
            CacheName = "Slicer_Region_Cache",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            SourceSheetName = "Sheet1"
        };
        wb.Slicers.Add(slicer);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        var clonedSlicer = wb.Slicers.Single(s => !ReferenceEquals(s, slicer));
        var copyPivotName = copy.PivotTables.Should().ContainSingle().Subject.Name;

        // Model-level agreement: the clone's slicer must name the COPY's own (renamed) pivot table.
        clonedSlicer.SourcePivotTableName.Should().Be(copyPivotName);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(wb, saved);
        saved.Position = 0;

        var workbookRoot = ReadRoot(saved, "xl/workbook.xml");
        var sheetsElement = workbookRoot.Element(WorkbookNs + "sheets")!;
        var sourceTabId = sheetsElement.Elements(WorkbookNs + "sheet")
            .Single(e => e.Attribute("name")?.Value == "Sheet1").Attribute("sheetId")!.Value;
        var copyTabId = sheetsElement.Elements(WorkbookNs + "sheet")
            .Single(e => e.Attribute("name")?.Value == copy.Name).Attribute("sheetId")!.Value;
        sourceTabId.Should().NotBe(copyTabId, "sanity: the two sheets must have distinct sheetIds");

        // The ORIGINAL slicer's cache (sibling, untouched by this fix) must still resolve to the
        // source sheet's own tabId.
        var originalCacheEntry = ReadRoot(saved, "xl/slicerCaches/slicerCache1.xml")
            .Descendants(SlicerNs + "pivotTable").Single();
        originalCacheEntry.Attribute("name")!.Value.Should().Be("PivotTable1");
        originalCacheEntry.Attribute("tabId")!.Value.Should().Be(sourceTabId);

        // The fix: the CLONED slicer's cache must resolve to the COPY sheet's own tabId, naming the
        // copy's own (renamed) pivot table -- not the source's "PivotTable1" / source tabId.
        var cloneCacheEntry = ReadRoot(saved, "xl/slicerCaches/slicerCache2.xml")
            .Descendants(SlicerNs + "pivotTable").Single();
        cloneCacheEntry.Attribute("name")!.Value.Should().Be(copyPivotName);
        cloneCacheEntry.Attribute("name")!.Value.Should().NotBe("PivotTable1");
        cloneCacheEntry.Attribute("tabId")!.Value.Should().Be(copyTabId);
    }

    private static XElement ReadRoot(Stream stream, string entryName)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull(entryName);
        using var entryStream = entry!.Open();
        return XDocument.Load(entryStream).Root!;
    }
}
