using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R114-commands-pivot-sharedItems: a slicer inserted against a pivot created in the SAME editing
/// session (no intervening save+reload) must show real filter items immediately -- both the pivot
/// cache field's SharedItems and the slicer's own CacheItems must be populated by the real commands,
/// not left empty until a round-trip through a file backfills them.
/// </summary>
public sealed class R114_PivotSlicerSharedItemsTests
{
    private static void SeedData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
    }

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    /// <summary>
    /// THE anchor test: create a pivot with AddPivotTableCommand, add a slicer against it with
    /// AddSlicerCommand -- all in one session, no file round-trip -- then resolve the slicer's items
    /// through the same SlicerItemResolver the live UI/render path uses. Before the fix this returned
    /// an empty list (field.SharedItems was null AND slicer.CacheItems was empty); after the fix it
    /// must return the distinct source-column values with all of them selected.
    /// </summary>
    [Fact]
    public void FreshlyCreatedPivotAndSlicer_ResolvesRealFilterItemsWithoutFileRoundTrip()
    {
        var workbook = new Workbook("R114FreshPivotSlicer");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);

        var addPivot = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "B4"),
            Range(sheet, "D3", "E6"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);
        addPivot.Apply(ctx).Success.Should().BeTrue();

        var addSlicer = new AddSlicerCommand("Category Slicer", "PivotTable1", "Category");
        addSlicer.Apply(ctx).Success.Should().BeTrue();

        var slicer = workbook.Slicers.Should().ContainSingle().Subject;

        // The cache field itself must carry real shared items now (not just the summary flags).
        var cache = workbook.PivotCaches.Should().ContainSingle().Subject;
        var categoryField = cache.Fields.Should().ContainSingle(field => field.Name == "Category").Subject;
        categoryField.SharedItems.Should().NotBeNull();
        categoryField.SharedItems!.Should().Equal("A", "B");

        // The slicer's own CacheItems must be seeded too (SlicerItemResolver requires both).
        slicer.CacheItems.Should().HaveCount(2);
        slicer.CacheItems.Should().OnlyContain(item => item.IsSelected);

        // This is the real live-UI/render entry point: SlicerItemResolver.ResolveAvailableItems.
        var resolved = SlicerItemResolver.ResolveAvailableItems(slicer, workbook);

        resolved.Should().Equal("A", "B");
    }

    /// <summary>
    /// Sibling coverage for "Change Data Source": redirecting an existing pivot's source (same
    /// SourceType, in-place cache mutation branch) must also populate SharedItems from the NEW data so
    /// a slicer resolved afterward sees the redirected source's values, not an empty list.
    /// </summary>
    [Fact]
    public void ChangePivotTableSourceCommand_PopulatesSharedItemsFromNewSource()
    {
        var workbook = new Workbook("R114ChangeSource");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B5"), new NumberValue(40));
        var ctx = new TestCommandContext(workbook);

        var addPivot = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "B4"),
            Range(sheet, "D3", "E6"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);
        addPivot.Apply(ctx).Success.Should().BeTrue();

        var changeSource = new ChangePivotTableSourceCommand(sheet.Id, "PivotTable1", Range(sheet, "A1", "B5"));
        changeSource.Apply(ctx).Success.Should().BeTrue();

        var cache = workbook.PivotCaches.Should().ContainSingle().Subject;
        var categoryField = cache.Fields.Should().ContainSingle(field => field.Name == "Category").Subject;
        categoryField.SharedItems.Should().NotBeNull();
        categoryField.SharedItems!.Should().Equal("A", "B", "C");
    }

    /// <summary>
    /// No-regression sibling: a slicer added when the cache/field can't be resolved (the existing
    /// hand-built-model test shape used throughout PivotTableCommandTests.Filters.cs, where a
    /// PivotTableModel is added directly to the sheet without ever registering a matching
    /// PivotCacheModel in workbook.PivotCaches) must still succeed with an EMPTY CacheItems list, not
    /// throw -- the new lookup must degrade gracefully exactly like SlicerItemResolver already does.
    /// </summary>
    [Fact]
    public void AddSlicerCommand_NoMatchingCache_LeavesCacheItemsEmptyAndDoesNotThrow()
    {
        var workbook = new Workbook("R114NoCache");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B4"),
            TargetRange = Range(sheet, "D3", "F7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        var ctx = new TestCommandContext(workbook);

        var command = new AddSlicerCommand("Category Slicer", "PivotTable1", "Category");

        command.Apply(ctx).Success.Should().BeTrue();

        var slicer = workbook.Slicers.Should().ContainSingle().Subject;
        slicer.CacheItems.Should().BeEmpty();
    }
}
