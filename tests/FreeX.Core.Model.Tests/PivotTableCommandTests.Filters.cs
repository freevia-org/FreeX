using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableCommandTests
{
    [Fact]
    public void ClearPivotTableViewCommand_ClearsFiltersSortsAndSelectionsAndUndoRestores()
    {
        var (sheet, ctx, pivot) = CreateBasicPivotReport("ClearPivotCommandTest");
        pivot.RowFields.Clear();
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItem: "A", SelectedItems: ["A"]));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "A"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.GreaterThan, ComparisonValue: 5, SourceFieldIndex: 0));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Descending, FieldIndex: 0));
        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivot);

        var command = new ClearPivotTableViewCommand(sheet.Id, "PivotTable1");

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.RowFields.Should().ContainSingle().Which.SelectedItems.Should().BeNull();
        pivot.LabelFilters.Should().BeEmpty();
        pivot.ValueFilters.Should().BeEmpty();
        pivot.Sorts.Should().BeEmpty();
        sheet.GetCell(Addr(sheet, "D5"))!.Value.Should().Be(new TextValue("B"));

        command.Revert(ctx);

        pivot.RowFields.Should().ContainSingle().Which.SelectedItems.Should().Equal("A");
        pivot.LabelFilters.Should().ContainSingle();
        pivot.ValueFilters.Should().ContainSingle();
        pivot.Sorts.Should().ContainSingle();
        sheet.GetCell(Addr(sheet, "D5"))!.Value.Should().Be(new TextValue("Grand Total"));
    }

    [Fact]
    public void ConfigurePivotTableViewCommand_ReplacesSortsAndFiltersRefreshesAndUndoRestores()
    {
        var workbook = new Workbook("PivotViewCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F6")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ConfigurePivotTableViewCommand(
            sheet.Id,
            "PivotTable1",
            labelFilters: [new PivotLabelFilterModel(0, PivotLabelFilterKind.Equals, "B")],
            valueFilters: [],
            sorts: [new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Descending, FieldIndex: 0)]);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.LabelFilters.Should().ContainSingle().Which.Value.Should().Be("B");
        pivot.Sorts.Should().ContainSingle().Which.Direction.Should().Be(PivotSortDirection.Descending);
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new TextValue("B"));
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new NumberValue(20));
        sheet.GetCell(Addr(sheet, "D5"))!.Value.Should().Be(new TextValue("Grand Total"));

        command.Revert(ctx);

        pivot.LabelFilters.Should().BeEmpty();
        pivot.Sorts.Should().BeEmpty();
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new TextValue("A"));
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void ConfigurePivotTableViewCommand_RejectsProtectedSheetWithoutUsePivotReportsPermission()
    {
        var (sheet, ctx, pivot) = CreateBasicPivotReport("ProtectedPivotViewCommandTest");
        sheet.IsProtected = true;

        var outcome = new ConfigurePivotTableViewCommand(
            sheet.Id,
            pivot.Name,
            labelFilters: [new PivotLabelFilterModel(0, PivotLabelFilterKind.Equals, "B")],
            valueFilters: [],
            sorts: []).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        pivot.LabelFilters.Should().BeEmpty();
    }

    [Fact]
    public void ConfigurePivotTableViewCommand_AllowsProtectedSheetWithUsePivotReportsPermission()
    {
        var (sheet, ctx, pivot) = CreateBasicPivotReport("ProtectedPivotViewCommandTest");
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);

        var outcome = new ConfigurePivotTableViewCommand(
            sheet.Id,
            pivot.Name,
            labelFilters: [new PivotLabelFilterModel(0, PivotLabelFilterKind.Equals, "B")],
            valueFilters: [],
            sorts: []).Apply(ctx);

        outcome.Success.Should().BeTrue();
        pivot.LabelFilters.Should().ContainSingle().Which.Value.Should().Be("B");
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new TextValue("B"));
    }

    [Fact]
    public void SetSlicerSelectionCommand_FiltersConnectedPivotTableAndUndoRestores()
    {
        var workbook = new Workbook("SlicerSelectionCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Category"
        });
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new SetSlicerSelectionCommand("Category Slicer", ["B"]);

        command.Apply(ctx).Success.Should().BeTrue();

        workbook.Slicers[0].SelectedItems.Should().Equal("B");
        pivot.RowFields.Should().ContainSingle().Which.SelectedItems.Should().Equal("B");
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new TextValue("B"));
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new NumberValue(20));
        sheet.GetCell(Addr(sheet, "D5"))!.Value.Should().Be(new TextValue("Grand Total"));

        command.Revert(ctx);

        workbook.Slicers[0].SelectedItems.Should().BeEmpty();
        pivot.RowFields.Should().ContainSingle().Which.SelectedItems.Should().BeNull();
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new TextValue("A"));
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new NumberValue(10));
        sheet.GetCell(Addr(sheet, "D6"))!.Value.Should().Be(new TextValue("Grand Total"));
    }

    [Fact]
    public void AddSlicerCommand_CreatesConnectedSlicerAndUndoRemovesIt()
    {
        var workbook = new Workbook("AddSlicerCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        var ctx = new SimpleCtx(workbook);
        var command = new AddSlicerCommand("Category Slicer", "PivotTable1", "Category");

        command.Apply(ctx).Success.Should().BeTrue();

        var slicer = workbook.Slicers.Should().ContainSingle().Which;
        slicer.Should().Match<SlicerModel>(slicer =>
            slicer.Name == "Category Slicer" &&
            slicer.CacheName == "Slicer_Category_Slicer" &&
            slicer.SourcePivotTableName == "PivotTable1" &&
            slicer.SourceFieldName == "Category");
        slicer.DrawingAnchor.Should().Be(new DrawingAnchorRange(
            new DrawingAnchorPoint(6, 0, 2, 0),
            new DrawingAnchorPoint(9, 0, 10, 0)));

        command.Revert(ctx);

        workbook.Slicers.Should().BeEmpty();
    }

    [Fact]
    public void AddSlicerCommand_RejectsProtectedPivotSheetWithoutUsePivotReportsPermission()
    {
        var workbook = new Workbook("AddSlicerProtectionTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        sheet.PivotTables.Add(CreateCategoryAmountPivot(sheet));
        sheet.IsProtected = true;
        var ctx = new SimpleCtx(workbook);

        var outcome = new AddSlicerCommand("Category Slicer", "PivotTable1", "Category").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        workbook.Slicers.Should().BeEmpty();
    }

    [Fact]
    public void AddSlicerCommand_AllowsProtectedPivotSheetWithUsePivotReportsPermission()
    {
        var workbook = new Workbook("AddSlicerProtectionTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        sheet.PivotTables.Add(CreateCategoryAmountPivot(sheet));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        var ctx = new SimpleCtx(workbook);

        var outcome = new AddSlicerCommand("Category Slicer", "PivotTable1", "Category").Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.Slicers.Should().ContainSingle();
    }

    [Fact]
    public void AddSlicerCommand_RejectsProtectedPivotSheetWithoutEditObjectsPermission()
    {
        var workbook = new Workbook("AddSlicerObjectProtectionTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        sheet.PivotTables.Add(CreateCategoryAmountPivot(sheet));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);
        var ctx = new SimpleCtx(workbook);

        var outcome = new AddSlicerCommand("Category Slicer", "PivotTable1", "Category").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        workbook.Slicers.Should().BeEmpty();
    }

    [Fact]
    public void AddSlicerCommand_UsesSourceSheetHeadersWhenPivotIsOnAnotherSheet()
    {
        var workbook = new Workbook("AddCrossSheetSlicerCommandTest");
        var sourceSheet = workbook.AddSheet("Data");
        var pivotSheet = workbook.AddSheet("Pivot");
        SeedData(sourceSheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sourceSheet, "A1", "B3"),
            TargetRange = Range(pivotSheet, "D3", "F7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        pivotSheet.PivotTables.Add(pivot);
        var ctx = new SimpleCtx(workbook);
        var command = new AddSlicerCommand("Category Slicer", "PivotTable1", "Category");

        command.Apply(ctx).Success.Should().BeTrue();

        workbook.Slicers.Should().ContainSingle().Which.SourceFieldName.Should().Be("Category");
    }

    [Fact]
    public void SetSlicerSelectionCommand_FiltersCrossSheetPivotTable()
    {
        var workbook = new Workbook("CrossSheetSlicerSelectionCommandTest");
        var sourceSheet = workbook.AddSheet("Data");
        var pivotSheet = workbook.AddSheet("Pivot");
        SeedData(sourceSheet);
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Category"
        });
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sourceSheet, "A1", "B3"),
            TargetRange = Range(pivotSheet, "D3", "F7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        pivotSheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, pivotSheet, pivot);

        var command = new SetSlicerSelectionCommand("Category Slicer", ["B"]);

        command.Apply(ctx).Success.Should().BeTrue();

        workbook.Slicers[0].SelectedItems.Should().Equal("B");
        pivot.RowFields.Should().ContainSingle().Which.SelectedItems.Should().Equal("B");
        pivotSheet.GetCell(Addr(pivotSheet, "D4"))!.Value.Should().Be(new TextValue("B"));
        pivotSheet.GetCell(Addr(pivotSheet, "E4"))!.Value.Should().Be(new NumberValue(20));
    }

    [Fact]
    public void SetTimelineRangeCommand_FiltersConnectedPivotTableAndUndoRestores()
    {
        var workbook = new Workbook("TimelineRangeCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Date"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 5)));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 20)));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date"
        });
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B4"),
            TargetRange = Range(sheet, "D3", "F9")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.Day));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new SetTimelineRangeCommand("Date Timeline", "2026-01-01", "2026-01-31");

        command.Apply(ctx).Success.Should().BeTrue();

        workbook.Timelines[0].SelectedStartDate.Should().Be("2026-01-01");
        workbook.Timelines[0].SelectedEndDate.Should().Be("2026-01-31");
        pivot.RowFields.Should().ContainSingle().Which.SelectedItems.Should().Equal("2026-01-05", "2026-01-20");
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new TextValue("2026-01-05"));
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new NumberValue(10));
        sheet.GetCell(Addr(sheet, "D5"))!.Value.Should().Be(new TextValue("2026-01-20"));
        sheet.GetCell(Addr(sheet, "E5"))!.Value.Should().Be(new NumberValue(20));
        sheet.GetCell(Addr(sheet, "E6"))!.Value.Should().Be(new NumberValue(30));

        command.Revert(ctx);

        workbook.Timelines[0].SelectedStartDate.Should().BeNull();
        workbook.Timelines[0].SelectedEndDate.Should().BeNull();
        pivot.RowFields.Should().ContainSingle().Which.SelectedItems.Should().BeNull();
        sheet.GetCell(Addr(sheet, "D6"))!.Value.Should().Be(new TextValue("2026-02-02"));
        sheet.GetCell(Addr(sheet, "E7"))!.Value.Should().Be(new NumberValue(60));
    }

    [Fact]
    public void AddTimelineCommand_CreatesConnectedTimelineWithDateBoundsAndUndoRemovesIt()
    {
        var workbook = new Workbook("AddTimelineCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Date"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 5)));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(30));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.Day));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        var ctx = new SimpleCtx(workbook);
        var command = new AddTimelineCommand("Date Timeline", "PivotTable1", "Date");

        command.Apply(ctx).Success.Should().BeTrue();

        var timeline = workbook.Timelines.Should().ContainSingle().Which;
        timeline.Should().Match<TimelineModel>(timeline =>
            timeline.Name == "Date Timeline" &&
            timeline.CacheName == "Timeline_Date_Timeline" &&
            timeline.SourcePivotTableName == "PivotTable1" &&
            timeline.SourceFieldName == "Date" &&
            timeline.StartDate == "2026-01-05" &&
            timeline.EndDate == "2026-02-02");
        timeline.DrawingAnchor.Should().Be(new DrawingAnchorRange(
            new DrawingAnchorPoint(6, 0, 2, 0),
            new DrawingAnchorPoint(9, 0, 10, 0)));

        command.Revert(ctx);

        workbook.Timelines.Should().BeEmpty();
    }

    [Fact]
    public void AddTimelineCommand_RejectsNonDateSourceField()
    {
        var workbook = new Workbook("AddTimelineNonDateFieldTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("Hardware"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("Services"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(30));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        var ctx = new SimpleCtx(workbook);

        var outcome = new AddTimelineCommand("Category Timeline", "PivotTable1", "Category").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Timeline source field must contain dates.");
        workbook.Timelines.Should().BeEmpty();

        outcome = new AddTimelineCommand("Amount Timeline", "PivotTable1", "Amount").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Timeline source field must contain dates.");
        workbook.Timelines.Should().BeEmpty();
    }

    [Fact]
    public void AddTimelineCommand_RejectsProtectedPivotSheetWithoutUsePivotReportsPermission()
    {
        var workbook = new Workbook("AddTimelineProtectionTest");
        var sheet = workbook.AddSheet("Data");
        SeedTimelineData(sheet);
        sheet.PivotTables.Add(CreateDateAmountPivot(sheet));
        sheet.IsProtected = true;
        var ctx = new SimpleCtx(workbook);

        var outcome = new AddTimelineCommand("Date Timeline", "PivotTable1", "Date").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        workbook.Timelines.Should().BeEmpty();
    }

    [Fact]
    public void AddTimelineCommand_AllowsProtectedPivotSheetWithUsePivotReportsPermission()
    {
        var workbook = new Workbook("AddTimelineProtectionTest");
        var sheet = workbook.AddSheet("Data");
        SeedTimelineData(sheet);
        sheet.PivotTables.Add(CreateDateAmountPivot(sheet));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        var ctx = new SimpleCtx(workbook);

        var outcome = new AddTimelineCommand("Date Timeline", "PivotTable1", "Date").Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.Timelines.Should().ContainSingle();
    }

    [Fact]
    public void AddTimelineCommand_RejectsProtectedPivotSheetWithoutEditObjectsPermission()
    {
        var workbook = new Workbook("AddTimelineObjectProtectionTest");
        var sheet = workbook.AddSheet("Data");
        SeedTimelineData(sheet);
        sheet.PivotTables.Add(CreateDateAmountPivot(sheet));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);
        var ctx = new SimpleCtx(workbook);

        var outcome = new AddTimelineCommand("Date Timeline", "PivotTable1", "Date").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        workbook.Timelines.Should().BeEmpty();
    }

    [Fact]
    public void AddTimelineCommand_UsesSourceSheetDatesWhenPivotIsOnAnotherSheet()
    {
        var workbook = new Workbook("AddCrossSheetTimelineCommandTest");
        var sourceSheet = workbook.AddSheet("Data");
        var pivotSheet = workbook.AddSheet("Pivot");
        SeedTimelineData(sourceSheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sourceSheet, "A1", "B3"),
            TargetRange = Range(pivotSheet, "D3", "F8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.Day));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        pivotSheet.PivotTables.Add(pivot);
        var ctx = new SimpleCtx(workbook);
        var command = new AddTimelineCommand("Date Timeline", "PivotTable1", "Date");

        command.Apply(ctx).Success.Should().BeTrue();

        workbook.Timelines.Should().ContainSingle().Which.Should().Match<TimelineModel>(timeline =>
            timeline.SourceFieldName == "Date" &&
            timeline.StartDate == "2026-01-05" &&
            timeline.EndDate == "2026-02-02");
    }

    [Fact]
    public void SetTimelineRangeCommand_FiltersCrossSheetPivotTable()
    {
        var workbook = new Workbook("CrossSheetTimelineRangeCommandTest");
        var sourceSheet = workbook.AddSheet("Data");
        var pivotSheet = workbook.AddSheet("Pivot");
        SeedTimelineData(sourceSheet);
        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date"
        });
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sourceSheet, "A1", "B3"),
            TargetRange = Range(pivotSheet, "D3", "F8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.Day));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        pivotSheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, pivotSheet, pivot);

        var command = new SetTimelineRangeCommand("Date Timeline", "2026-01-01", "2026-01-31");

        command.Apply(ctx).Success.Should().BeTrue();

        workbook.Timelines[0].SelectedStartDate.Should().Be("2026-01-01");
        workbook.Timelines[0].SelectedEndDate.Should().Be("2026-01-31");
        pivot.RowFields.Should().ContainSingle().Which.SelectedItems.Should().Equal("2026-01-05");
        pivotSheet.GetCell(Addr(pivotSheet, "D4"))!.Value.Should().Be(new TextValue("2026-01-05"));
        pivotSheet.GetCell(Addr(pivotSheet, "E4"))!.Value.Should().Be(new NumberValue(10));
    }
}
