using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for the finding: DuplicateSheetCommand.Apply cloned every other floating/drawing
/// object type on the duplicated sheet (Charts, TextBoxes, DrawingShapes, Pictures, Sparklines,
/// FormControls via DuplicateSheetDrawingCloner.CopyDrawingCollections, PivotTables via
/// Sheet.Clone), but Slicers and Timelines -- workbook-level collections keyed to a host sheet only
/// indirectly via SlicerModel.SourceSheetName / TimelineModel.SourceSheetName -- were never touched
/// anywhere in the duplicate-sheet path, so a slicer/timeline filtering a pivot table on the
/// duplicated sheet silently vanished from the copy even though the pivot table itself is
/// faithfully cloned. Real Excel's Duplicate Sheet / Move-or-Copy carries the slicer/timeline over
/// along with the pivot table it filters.
/// </summary>
public sealed class R103_DuplicateSheetSlicerTimelineTests
{
    [Fact]
    public void DuplicateSheet_ClonesSlicerAnchoredOnDuplicatedSheet()
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
            SourceSheetName = "Sheet1",
            PackagePart = "xl/slicers/slicer1.xml",
            DrawingAnchor = new DrawingAnchorRange(
                new DrawingAnchorPoint(3, 0, 1, 0),
                new DrawingAnchorPoint(6, 0, 10, 0))
        };
        slicer.SelectedItems.Add("East");
        wb.Slicers.Add(slicer);

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        copy.Name.Should().Be("Sheet1 (2)");

        // The source's own slicer must survive untouched.
        wb.Slicers.Should().Contain(slicer);
        slicer.SourceSheetName.Should().Be("Sheet1");

        // A clone must now exist, anchored on the copy sheet.
        wb.Slicers.Should().HaveCount(2, because: "Duplicate Sheet must clone the slicer onto the copy, not drop it");
        var clone = wb.Slicers.Single(s => !ReferenceEquals(s, slicer));

        clone.SourceSheetName.Should().Be(copy.Name);

        // R151-model-pivot-clone-identity: the copy's own pivot table must get a workbook-unique
        // Name (mirroring the pre-existing structured-table uniquify contract below), and the
        // cloned slicer -- which already correctly followed its pivot table onto the copy sheet via
        // SourceSheetName -- must follow the RENAMED name too, not keep pointing at the source
        // sheet's still-"PivotTable1" identity (which XlsxSlicerTimelineWriter's name-keyed
        // ResolvePivotHostTabId would otherwise resolve back to the source sheet's tabId).
        copy.PivotTables.Should().ContainSingle().Which.Name.Should().NotBe("PivotTable1");
        clone.SourcePivotTableName.Should().Be(copy.PivotTables[0].Name);
        clone.SourceFieldName.Should().Be("Region");
        clone.SelectedItems.Should().ContainSingle().Which.Should().Be("East");
        clone.DrawingAnchor.Should().Be(slicer.DrawingAnchor);

        // Name/CacheName must be workbook-unique -- not a duplicate of the source's identity, or a
        // save would either collide the two <slicer> definitions or alias their cache parts.
        clone.Name.Should().NotBe(slicer.Name);
        clone.CacheName.Should().NotBe(slicer.CacheName);

        // PackagePart must be blank so the writer allocates the clone a fresh package part instead
        // of aliasing the source's on-disk slicer part.
        clone.PackagePart.Should().BeEmpty();
    }

    [Fact]
    public void DuplicateSheet_ClonesTimelineAnchoredOnDuplicatedSheet()
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

        var timeline = new TimelineModel
        {
            Name = "Timeline_Date",
            CacheName = "Timeline_Date_Cache",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "OrderDate",
            SourceSheetName = "Sheet1",
            PackagePart = "xl/timelines/timeline1.xml",
            SelectedStartDate = "2024-01-01T00:00:00",
            SelectedEndDate = "2024-03-31T00:00:00",
            Level = 2
        };
        wb.Timelines.Add(timeline);

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];

        wb.Timelines.Should().Contain(timeline);
        timeline.SourceSheetName.Should().Be("Sheet1");

        wb.Timelines.Should().HaveCount(2, because: "Duplicate Sheet must clone the timeline onto the copy, not drop it");
        var clone = wb.Timelines.Single(t => !ReferenceEquals(t, timeline));

        clone.SourceSheetName.Should().Be(copy.Name);

        // R151-model-pivot-clone-identity: see the matching comment in the slicer test above -- the
        // cloned timeline must follow the copy's renamed pivot table too.
        copy.PivotTables.Should().ContainSingle().Which.Name.Should().NotBe("PivotTable1");
        clone.SourcePivotTableName.Should().Be(copy.PivotTables[0].Name);
        clone.SelectedStartDate.Should().Be("2024-01-01T00:00:00");
        clone.SelectedEndDate.Should().Be("2024-03-31T00:00:00");
        clone.Level.Should().Be(2);
        clone.Name.Should().NotBe(timeline.Name);
        clone.CacheName.Should().NotBe(timeline.CacheName);
        clone.PackagePart.Should().BeEmpty();
    }

    [Fact]
    public void DuplicateSheet_CrossSheetSlicer_IsNotClonedOntoUnrelatedDuplicate()
    {
        // A slicer anchored on 'Other' must not be pulled onto a duplicate of an unrelated sheet.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        wb.AddSheet("Other");
        var ctx = new TestCommandContext(wb);

        var slicer = new SlicerModel { Name = "Slicer1", CacheName = "Slicer1_Cache", SourceSheetName = "Other" };
        wb.Slicers.Add(slicer);

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        wb.Slicers.Should().ContainSingle(because: "the copy of Sheet1 has no relationship to a slicer anchored on Other");
        slicer.SourceSheetName.Should().Be("Other");
    }

    [Fact]
    public void DuplicateSheetRevert_RemovesClonedSlicerAndTimeline()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var slicer = new SlicerModel { Name = "Slicer1", CacheName = "Slicer1_Cache", SourceSheetName = "Sheet1" };
        wb.Slicers.Add(slicer);
        var timeline = new TimelineModel { Name = "Timeline1", CacheName = "Timeline1_Cache", SourceSheetName = "Sheet1" };
        wb.Timelines.Add(timeline);

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        wb.Slicers.Should().HaveCount(2);
        wb.Timelines.Should().HaveCount(2);

        command.Revert(ctx);

        // Undo must remove exactly the clones it added -- the source's own slicer/timeline (and
        // the sheet itself) must be restored to their pre-duplicate state.
        wb.Sheets.Should().ContainSingle();
        wb.Slicers.Should().ContainSingle().Which.Should().BeSameAs(slicer);
        wb.Timelines.Should().ContainSingle().Which.Should().BeSameAs(timeline);
        slicer.SourceSheetName.Should().Be("Sheet1");
        timeline.SourceSheetName.Should().Be("Sheet1");
    }
}
