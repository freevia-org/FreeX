using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableCommandTests
{
    [Fact]
    public void AddPivotTableCommand_AddsPivotCacheAndTableAndUndoRemovesThem()
    {
        var workbook = new Workbook("PivotCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var source = Range(sheet, "A1", "B3");
        var target = Range(sheet, "D3", "E5");

        var command = new AddPivotTableCommand(
            sheet.Id,
            source,
            target,
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);

        command.Apply(ctx).Success.Should().BeTrue();

        var cache = workbook.PivotCaches.Should().ContainSingle().Subject;
        cache.CacheId.Should().Be(1);
        cache.SourceType.Should().Be(PivotCacheSourceType.WorksheetRange);
        cache.SourceSheetName.Should().Be("Data");
        cache.SourceReference.Should().Be("A1:B3");
        cache.Fields.Select(field => field.Name).Should().Equal("Category", "Amount");

        var pivot = sheet.PivotTables.Should().ContainSingle().Subject;
        pivot.Name.Should().Be("PivotTable1");
        pivot.CacheId.Should().Be(1);
        pivot.SourceRange.Should().Be(source);
        pivot.TargetRange.Should().Be(target);
        pivot.RowFields.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(0);
        pivot.DataFields.Should().ContainSingle().Which.Should().Be(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.GetCell(3, 4)!.Value.Should().Be(new TextValue("Category"));
        sheet.GetCell(4, 4)!.Value.Should().Be(new TextValue("A"));
        sheet.GetCell(4, 5)!.Value.Should().Be(new NumberValue(10));

        command.Revert(ctx);

        workbook.PivotCaches.Should().BeEmpty();
        sheet.PivotTables.Should().BeEmpty();
        sheet.GetCell(3, 4).Should().BeNull();
        sheet.GetCell(4, 4).Should().BeNull();
        sheet.GetCell(4, 5).Should().BeNull();
    }

    [Fact]
    public void AddPivotTableCommand_RejectsProtectedTargetSheetWithoutUsePivotReportsPermission()
    {
        var workbook = new Workbook("PivotProtectionTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(workbook);

        var outcome = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "B3"),
            Range(sheet, "D3", "E5"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        workbook.PivotCaches.Should().BeEmpty();
        sheet.PivotTables.Should().BeEmpty();
        sheet.GetCell(Addr(sheet, "D3")).Should().BeNull();
    }

    [Fact]
    public void AddPivotTableCommand_AllowsProtectedTargetSheetWithUsePivotReportsPermission()
    {
        var workbook = new Workbook("PivotProtectionTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);
        var ctx = new TestCommandContext(workbook);

        var outcome = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "B3"),
            Range(sheet, "D3", "E5"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]).Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.PivotCaches.Should().ContainSingle();
        sheet.PivotTables.Should().ContainSingle();
        sheet.GetCell(Addr(sheet, "D3"))!.Value.Should().Be(new TextValue("Category"));
    }

    [Fact]
    public void RenamePivotTableCommand_RenamesConnectedPivotArtifactsAndUndoRestores()
    {
        var (sheet, ctx, pivot) = CreateBasicPivotReport("RenamePivotCommandTest");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivot),
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            PivotCacheId = 1
        });
        ctx.Workbook.Slicers.Add(new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Category"
        });
        ctx.Workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date"
        });

        var command = new RenamePivotTableCommand(sheet.Id, "PivotTable1", "SalesPivot");

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.Name.Should().Be("SalesPivot");
        sheet.Charts[0].PivotTableName.Should().Be("SalesPivot");
        ctx.Workbook.Slicers[0].SourcePivotTableName.Should().Be("SalesPivot");
        ctx.Workbook.Timelines[0].SourcePivotTableName.Should().Be("SalesPivot");

        command.Revert(ctx);

        pivot.Name.Should().Be("PivotTable1");
        sheet.Charts[0].PivotTableName.Should().Be("PivotTable1");
        ctx.Workbook.Slicers[0].SourcePivotTableName.Should().Be("PivotTable1");
        ctx.Workbook.Timelines[0].SourcePivotTableName.Should().Be("PivotTable1");
    }

    [Fact]
    public void RenamePivotTableCommand_RejectsDuplicateWorkbookName()
    {
        var workbook = new Workbook("RenamePivotDuplicateTest");
        var firstSheet = workbook.AddSheet("Data");
        var secondSheet = workbook.AddSheet("Pivot");
        SeedData(firstSheet);
        var pivot = CreateCategoryAmountPivot(firstSheet);
        firstSheet.PivotTables.Add(pivot);
        secondSheet.PivotTables.Add(new PivotTableModel
        {
            Name = "ExistingPivot",
            CacheId = 2,
            SourceRange = Range(firstSheet, "A1", "B3"),
            TargetRange = Range(secondSheet, "D3", "F7")
        });
        var ctx = new TestCommandContext(workbook);

        var outcome = new RenamePivotTableCommand(firstSheet.Id, "PivotTable1", "existingpivot").Apply(ctx);

        outcome.Success.Should().BeFalse();
        pivot.Name.Should().Be("PivotTable1");
    }

    [Fact]
    public void MovePivotTableCommand_MovesRenderedRangeAndUndoRestores()
    {
        var (sheet, ctx, pivot) = CreateBasicPivotReport("MovePivotCommandTest");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivot),
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            PivotCacheId = 1
        });

        var command = new MovePivotTableCommand(sheet.Id, "PivotTable1", Addr(sheet, "H10"));

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.TargetRange.Start.ToA1().Should().Be("H10");
        sheet.GetCell(Addr(sheet, "D3")).Should().BeNull();
        sheet.GetCell(Addr(sheet, "H10"))!.Value.Should().Be(new TextValue("Category"));
        sheet.Charts[0].DataRange.Start.ToA1().Should().Be("H10");

        command.Revert(ctx);

        pivot.TargetRange.Start.ToA1().Should().Be("D3");
        sheet.GetCell(Addr(sheet, "D3"))!.Value.Should().Be(new TextValue("Category"));
        sheet.GetCell(Addr(sheet, "H10")).Should().BeNull();
        sheet.Charts[0].DataRange.Start.ToA1().Should().Be("D3");
    }

    [Fact]
    public void AddPivotTableCommand_AllowsSourceRangeOnDifferentSheet()
    {
        var workbook = new Workbook("CrossSheetPivotCommandTest");
        var sourceSheet = workbook.AddSheet("Data");
        var pivotSheet = workbook.AddSheet("Pivot");
        SeedData(sourceSheet);
        var ctx = new TestCommandContext(workbook);

        var command = new AddPivotTableCommand(
            pivotSheet.Id,
            Range(sourceSheet, "A1", "B3"),
            Range(pivotSheet, "D3", "E8"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);

        command.Apply(ctx).Success.Should().BeTrue();

        workbook.PivotCaches.Should().ContainSingle().Which.Should().Match<PivotCacheModel>(cache =>
            cache.SourceSheetName == "Data" &&
            cache.SourceReference == "A1:B3");
        var pivot = pivotSheet.PivotTables.Should().ContainSingle().Subject;
        pivot.SourceRange.Should().Be(Range(sourceSheet, "A1", "B3"));
        pivot.TargetRange.Should().Be(Range(pivotSheet, "D3", "E8"));
        pivotSheet.GetCell(Addr(pivotSheet, "D4"))!.Value.Should().Be(new TextValue("A"));
        pivotSheet.GetCell(Addr(pivotSheet, "E4"))!.Value.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void AddPivotTableToNewWorksheetCommand_CreatesPivotSheetAndUndoRemovesIt()
    {
        var workbook = new Workbook("NewWorksheetPivotCommandTest");
        var sourceSheet = workbook.AddSheet("Data");
        workbook.AddSheet("PivotTable");
        SeedData(sourceSheet);
        var ctx = new TestCommandContext(workbook);

        var command = new AddPivotTableToNewWorksheetCommand(
            Range(sourceSheet, "A1", "B3"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);

        command.Apply(ctx).Success.Should().BeTrue();

        command.CreatedSheetId.Should().NotBeNull();
        var pivotSheet = workbook.GetSheet(command.CreatedSheetId!.Value);
        pivotSheet.Should().NotBeNull();
        pivotSheet!.Name.Should().Be("PivotTable 2");
        var pivot = pivotSheet.PivotTables.Should().ContainSingle().Subject;
        pivot.Name.Should().Be("PivotTable1");
        pivot.SourceRange.Should().Be(Range(sourceSheet, "A1", "B3"));
        pivot.TargetRange.Start.ToA1().Should().Be("A3");
        pivotSheet.GetCell(Addr(pivotSheet, "A3"))!.Value.Should().Be(new TextValue("Category"));
        pivotSheet.GetCell(Addr(pivotSheet, "A4"))!.Value.Should().Be(new TextValue("A"));
        pivotSheet.GetCell(Addr(pivotSheet, "B4"))!.Value.Should().Be(new NumberValue(10));

        var createdSheetId = command.CreatedSheetId.Value;
        command.Revert(ctx);

        workbook.GetSheet(createdSheetId).Should().BeNull();
        workbook.PivotCaches.Should().BeEmpty();
    }

    [Fact]
    public void AddPivotTableToNewWorksheetCommand_RejectsWhenWorkbookStructureProtected()
    {
        var workbook = new Workbook("ProtectedNewWorksheetPivotCommandTest");
        var sourceSheet = workbook.AddSheet("Data");
        SeedData(sourceSheet);
        workbook.IsStructureProtected = true;
        var ctx = new TestCommandContext(workbook);

        var command = new AddPivotTableToNewWorksheetCommand(
            Range(sourceSheet, "A1", "B3"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        workbook.Sheets.Should().ContainSingle().Which.Should().BeSameAs(sourceSheet);
        workbook.PivotCaches.Should().BeEmpty();
        command.CreatedSheetId.Should().BeNull();
    }

    [Fact]
    public void AddPivotTableCommand_RejectsFieldIndexesOutsideSourceColumns()
    {
        var workbook = new Workbook("PivotCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);

        var command = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "B3"),
            Range(sheet, "D3", "E5"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [2]);

        command.Apply(ctx).Success.Should().BeFalse();
        sheet.PivotTables.Should().BeEmpty();
        workbook.PivotCaches.Should().BeEmpty();
    }

    [Fact]
    public void DrillDownPivotTableCommand_CreatesDetailSheetAndUndoRemovesIt()
    {
        var workbook = new Workbook("PivotDrillDownCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(20));
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E3", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new DrillDownPivotTableCommand(sheet.Id, "PivotTable1", Addr(sheet, "G4"));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.Sheets.Should().HaveCount(2);
        var detail = workbook.GetSheetAt(1);
        detail.Name.Should().StartWith("Detail");
        outcome.AffectedCells.Should().Equal(new CellAddress(detail.Id, 1, 1));
        detail.GetCell(1, 1)!.Value.Should().Be(new TextValue("Category"));
        detail.GetCell(2, 1)!.Value.Should().Be(new TextValue("A"));
        detail.GetCell(2, 2)!.Value.Should().Be(new TextValue("Q1"));
        detail.GetCell(2, 3)!.Value.Should().Be(new NumberValue(10));

        command.Revert(ctx);

        workbook.Sheets.Should().ContainSingle().Which.Name.Should().Be("Data");
    }

    [Fact]
    public void DrillDownPivotTableCommand_RejectsWhenShowDetailsDisabled()
    {
        var workbook = new Workbook("PivotDrillDownDisabledCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C2"),
            TargetRange = Range(sheet, "E3", "H8"),
            EnableDrill = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var outcome = new DrillDownPivotTableCommand(sheet.Id, "PivotTable1", Addr(sheet, "G4")).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Show Details is disabled for this PivotTable.");
        workbook.Sheets.Should().ContainSingle().Which.Name.Should().Be("Data");
    }

    [Fact]
    public void DrillDownPivotTableCommand_RejectsWhenWorkbookStructureProtected()
    {
        var workbook = new Workbook("PivotDrillDownStructureProtectedTest") { IsStructureProtected = true };
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B2"),
            TargetRange = Range(sheet, "D3", "F6")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var outcome = new DrillDownPivotTableCommand(sheet.Id, "PivotTable1", Addr(sheet, "F4")).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("The workbook structure is protected.");
        workbook.Sheets.Should().ContainSingle().Which.Name.Should().Be("Data");
    }

    [Fact]
    public void DrillDownPivotTableCommand_UsesNextDetailSheetNameWhenDetailExists()
    {
        var workbook = new Workbook("PivotDrillDownUniqueNameTest");
        workbook.AddSheet("Detail");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B2"),
            TargetRange = Range(sheet, "D3", "F6")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var outcome = new DrillDownPivotTableCommand(sheet.Id, "PivotTable1", Addr(sheet, "F4")).Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.Sheets.Select(item => item.Name).Should().ContainInOrder("Detail", "Data", "Detail2");
        outcome.AffectedCells.Should().Equal(new CellAddress(workbook.GetSheet("Detail2")!.Id, 1, 1));
    }

    [Fact]
    public void ChangePivotTableSourceCommand_RebindsWorksheetRangeRefreshesAndUndoRestores()
    {
        var workbook = new Workbook("PivotSourceCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B3"
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount"));
        workbook.PivotCaches.Add(cache);
        var ctx = new TestCommandContext(workbook);
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
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ChangePivotTableSourceCommand(sheet.Id, "PivotTable1", Range(sheet, "A1", "B4"));

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.SourceRange.Should().Be(Range(sheet, "A1", "B4"));
        cache.SourceReference.Should().Be("A1:B4");
        sheet.GetCell(Addr(sheet, "D6"))!.Value.Should().Be(new TextValue("C"));
        sheet.GetCell(Addr(sheet, "E6"))!.Value.Should().Be(new NumberValue(30));
        sheet.GetCell(Addr(sheet, "E7"))!.Value.Should().Be(new NumberValue(60));

        command.Revert(ctx);

        pivot.SourceRange.Should().Be(Range(sheet, "A1", "B3"));
        cache.SourceReference.Should().Be("A1:B3");
        sheet.GetCell(Addr(sheet, "D6"))!.Value.Should().Be(new TextValue("Grand Total"));
        sheet.GetCell(Addr(sheet, "E6"))!.Value.Should().Be(new NumberValue(30));
        sheet.GetCell(Addr(sheet, "D7")).Should().BeNull();
    }

    [Fact]
    public void ChangePivotTableSourceCommand_AllowsSourceRangeOnDifferentSheet()
    {
        var workbook = new Workbook("CrossSheetPivotSourceCommandTest");
        var originalSheet = workbook.AddSheet("Original");
        var newSourceSheet = workbook.AddSheet("NewData");
        var pivotSheet = workbook.AddSheet("Pivot");
        SeedData(originalSheet);
        SeedData(newSourceSheet);
        newSourceSheet.SetCell(Addr(newSourceSheet, "A4"), new TextValue("C"));
        newSourceSheet.SetCell(Addr(newSourceSheet, "B4"), new NumberValue(30));
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Original",
            SourceReference = "A1:B3"
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount"));
        workbook.PivotCaches.Add(cache);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(originalSheet, "A1", "B3"),
            TargetRange = Range(pivotSheet, "D3", "F8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        pivotSheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, pivotSheet, pivot);

        var command = new ChangePivotTableSourceCommand(pivotSheet.Id, "PivotTable1", Range(newSourceSheet, "A1", "B4"));

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.SourceRange.Should().Be(Range(newSourceSheet, "A1", "B4"));
        cache.SourceSheetName.Should().Be("NewData");
        cache.SourceReference.Should().Be("A1:B4");
        pivotSheet.GetCell(Addr(pivotSheet, "D6"))!.Value.Should().Be(new TextValue("C"));
        pivotSheet.GetCell(Addr(pivotSheet, "E6"))!.Value.Should().Be(new NumberValue(30));
        pivotSheet.GetCell(Addr(pivotSheet, "E7"))!.Value.Should().Be(new NumberValue(60));

        command.Revert(ctx);

        pivot.SourceRange.Should().Be(Range(originalSheet, "A1", "B3"));
        cache.SourceSheetName.Should().Be("Original");
        cache.SourceReference.Should().Be("A1:B3");
        pivotSheet.GetCell(Addr(pivotSheet, "D6"))!.Value.Should().Be(new TextValue("Grand Total"));
    }
}
