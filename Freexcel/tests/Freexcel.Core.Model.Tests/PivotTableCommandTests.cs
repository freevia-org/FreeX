using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class PivotTableCommandTests
{
    [Fact]
    public void AddPivotTableCommand_AddsPivotCacheAndTableAndUndoRemovesThem()
    {
        var workbook = new Workbook("PivotCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);
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
    public void RefreshPivotTableCommand_RefreshesAndUndoRestoresPreviousCells()
    {
        var workbook = new Workbook("PivotCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "E5")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        sheet.SetCell(Addr(sheet, "D3"), new TextValue("old"));

        var command = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetCell(Addr(sheet, "D3"))!.Value.Should().Be(new TextValue("Category"));

        command.Revert(ctx);
        sheet.GetCell(Addr(sheet, "D3"))!.Value.Should().Be(new TextValue("old"));
        sheet.GetCell(Addr(sheet, "E3")).Should().BeNull();
    }

    [Fact]
    public void RefreshPivotTableCommand_UpdatesBoundPivotChartDataRange()
    {
        var workbook = new Workbook("PivotChartRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B4"),
            TargetRange = Range(sheet, "D3", "E9")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = Range(sheet, "D3", "E5"),
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            PivotCacheId = 1
        });
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        var command = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");

        command.Apply(ctx).Success.Should().BeTrue();

        var chart = sheet.Charts.Should().ContainSingle().Subject;
        chart.DataRange.Start.ToA1().Should().Be("D3");
        chart.DataRange.End.ToA1().Should().Be("E7");
    }

    [Fact]
    public void AddPivotTableCommand_AllowsSourceRangeOnDifferentSheet()
    {
        var workbook = new Workbook("CrossSheetPivotCommandTest");
        var sourceSheet = workbook.AddSheet("Data");
        var pivotSheet = workbook.AddSheet("Pivot");
        SeedData(sourceSheet);
        var ctx = new SimpleCtx(workbook);

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
        var ctx = new SimpleCtx(workbook);

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
        var ctx = new SimpleCtx(workbook);

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
        var ctx = new SimpleCtx(workbook);

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
    public void AddPivotChartCommand_AddsBoundChartFromPivotOutputAndUndoRemovesIt()
    {
        var workbook = new Workbook("PivotChartCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 7,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "E8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new AddPivotChartCommand(sheet.Id, "PivotTable1", ChartType.Column, "Amount by Category");

        command.Apply(ctx).Success.Should().BeTrue();

        var chart = sheet.Charts.Should().ContainSingle().Subject;
        chart.IsPivotChart.Should().BeTrue();
        chart.PivotTableName.Should().Be("PivotTable1");
        chart.PivotCacheId.Should().Be(7);
        chart.DataRange.Start.ToA1().Should().Be("D3");
        chart.DataRange.End.ToA1().Should().Be("E6");
        chart.Title.Should().Be("Amount by Category");

        command.Revert(ctx);

        sheet.Charts.Should().BeEmpty();
        sheet.PivotTables.Should().ContainSingle().Which.Name.Should().Be("PivotTable1");
    }

    [Fact]
    public void AddPivotChartCommand_RejectsMissingPivotTable()
    {
        var workbook = new Workbook("PivotChartCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);

        var command = new AddPivotChartCommand(sheet.Id, "MissingPivot", ChartType.Column);

        command.Apply(ctx).Success.Should().BeFalse();
        sheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void ChangePivotChartTypeCommand_ChangesTypeAndPreservesPivotBindingAndUndoRestores()
    {
        var workbook = new Workbook("PivotChartTypeCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 7,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "E8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        var dataRange = PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivot);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = dataRange,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            PivotCacheId = 7,
            FirstColIsCategories = true,
            Title = "Amount by Category"
        };
        sheet.Charts.Add(chart);

        var command = new ChangePivotChartTypeCommand(sheet.Id, chart.Id, ChartType.Line);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.Type.Should().Be(ChartType.Line);
        chart.IsPivotChart.Should().BeTrue();
        chart.PivotTableName.Should().Be("PivotTable1");
        chart.PivotCacheId.Should().Be(7);
        chart.DataRange.Should().Be(dataRange);
        chart.Title.Should().Be("Amount by Category");

        command.Revert(ctx);

        chart.Type.Should().Be(ChartType.Column);
        chart.IsPivotChart.Should().BeTrue();
        chart.PivotTableName.Should().Be("PivotTable1");
        chart.PivotCacheId.Should().Be(7);
        chart.DataRange.Should().Be(dataRange);
    }

    [Fact]
    public void ChangePivotChartTypeCommand_RejectsOrdinaryCharts()
    {
        var workbook = new Workbook("PivotChartTypeCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = Range(sheet, "A1", "B3")
        };
        sheet.Charts.Add(chart);

        var command = new ChangePivotChartTypeCommand(sheet.Id, chart.Id, ChartType.Line);

        command.Apply(ctx).Success.Should().BeFalse();

        chart.Type.Should().Be(ChartType.Column);
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
        var ctx = new SimpleCtx(workbook);
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

        command.Apply(ctx).Success.Should().BeTrue();

        workbook.Sheets.Should().HaveCount(2);
        var detail = workbook.GetSheetAt(1);
        detail.Name.Should().StartWith("Detail");
        detail.GetCell(1, 1)!.Value.Should().Be(new TextValue("Category"));
        detail.GetCell(2, 1)!.Value.Should().Be(new TextValue("A"));
        detail.GetCell(2, 2)!.Value.Should().Be(new TextValue("Q1"));
        detail.GetCell(2, 3)!.Value.Should().Be(new NumberValue(10));

        command.Revert(ctx);

        workbook.Sheets.Should().ContainSingle().Which.Name.Should().Be("Data");
    }

    [Fact]
    public void ConfigurePivotTableLayoutCommand_ReplacesFieldsRefreshesAndUndoRestores()
    {
        var workbook = new Workbook("PivotLayoutCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(20));
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E3", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ConfigurePivotTableLayoutCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [new PivotFieldModel(1)],
            columnFields: [],
            pageFields: [new PivotFieldModel(0, SelectedItem: "A")],
            dataFields: [new PivotDataFieldModel(2, "Count of Amount", "count")]);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.RowFields.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(1);
        pivot.PageFields.Should().ContainSingle().Which.SelectedItem.Should().Be("A");
        pivot.DataFields.Should().ContainSingle().Which.SummaryFunction.Should().Be("count");
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new TextValue("Q1"));
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new NumberValue(1));

        command.Revert(ctx);

        pivot.RowFields.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(0);
        pivot.PageFields.Should().BeEmpty();
        pivot.DataFields.Should().ContainSingle().Which.SummaryFunction.Should().Be("sum");
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new TextValue("A"));
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void ConfigurePivotTableLayoutCommand_AllowsValuesOnlyLayout()
    {
        var workbook = new Workbook("PivotValuesOnlyLayoutCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
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

        var command = new ConfigurePivotTableLayoutCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [],
            columnFields: [],
            pageFields: [],
            dataFields: [new PivotDataFieldModel(1, "Sum of Amount", "sum")]);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.RowFields.Should().BeEmpty();
        sheet.GetCell(Addr(sheet, "D3"))!.Value.Should().Be(new TextValue("Sum of Amount"));
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new NumberValue(30));

        command.Revert(ctx);

        pivot.RowFields.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(0);
        sheet.GetCell(Addr(sheet, "D3"))!.Value.Should().Be(new TextValue("Category"));
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new TextValue("A"));
    }

    [Fact]
    public void ConfigurePivotTableLayoutCommand_UpdatesBoundPivotChartDataRange()
    {
        var workbook = new Workbook("PivotLayoutChartSyncTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(20));
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E3", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        sheet.Charts.Add(new ChartModel
        {
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            PivotCacheId = 1,
            DataRange = Range(sheet, "E3", "F6")
        });

        var command = new ConfigurePivotTableLayoutCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [new PivotFieldModel(0), new PivotFieldModel(1)],
            columnFields: [],
            pageFields: [],
            dataFields: [new PivotDataFieldModel(2, "Sum of Amount", "sum")]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].DataRange.Should().Be(PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivot));
        sheet.Charts[0].PivotCacheId.Should().Be(pivot.CacheId);
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
    public void ConfigurePivotTableOptionsCommand_ReplacesLayoutOptionsRefreshesAndUndoRestores()
    {
        var workbook = new Workbook("PivotOptionsCommandTest");
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
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E3", "H8"),
            ShowSubtotals = false,
            RepeatItemLabels = true,
            BlankLineAfterItems = false,
            StyleName = "PivotStyleLight16",
            ReportLayout = PivotReportLayout.Tabular,
            ShowRowHeaders = true,
            ShowColumnHeaders = true,
            ShowRowStripes = false,
            ShowColumnStripes = false,
            AltTextTitle = "Old title",
            AltTextDescription = "Old description"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: false,
            showColumnGrandTotals: false,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Top,
            repeatItemLabels: false,
            blankLineAfterItems: true,
            styleName: "PivotStyleMedium9",
            reportLayout: PivotReportLayout.Compact,
            showRowHeaders: false,
            showColumnHeaders: false,
            showRowStripes: true,
            showColumnStripes: true,
            printTitles: true,
            printExpandCollapseButtons: true,
            altTextTitle: "Sales pivot",
            altTextDescription: "Quarterly sales summary");

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.ShowRowGrandTotals.Should().BeFalse();
        pivot.ShowColumnGrandTotals.Should().BeFalse();
        pivot.ShowSubtotals.Should().BeTrue();
        pivot.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Top);
        pivot.RepeatItemLabels.Should().BeFalse();
        pivot.BlankLineAfterItems.Should().BeTrue();
        pivot.StyleName.Should().Be("PivotStyleMedium9");
        pivot.ReportLayout.Should().Be(PivotReportLayout.Compact);
        pivot.ShowRowHeaders.Should().BeFalse();
        pivot.ShowColumnHeaders.Should().BeFalse();
        pivot.ShowRowStripes.Should().BeTrue();
        pivot.ShowColumnStripes.Should().BeTrue();
        pivot.PrintTitles.Should().BeTrue();
        pivot.PrintExpandCollapseButtons.Should().BeTrue();
        pivot.AltTextTitle.Should().Be("Sales pivot");
        pivot.AltTextDescription.Should().Be("Quarterly sales summary");
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new TextValue("A Total"));

        command.Revert(ctx);

        pivot.ShowRowGrandTotals.Should().BeTrue();
        pivot.ShowColumnGrandTotals.Should().BeTrue();
        pivot.ShowSubtotals.Should().BeFalse();
        pivot.RepeatItemLabels.Should().BeTrue();
        pivot.BlankLineAfterItems.Should().BeFalse();
        pivot.StyleName.Should().Be("PivotStyleLight16");
        pivot.ReportLayout.Should().Be(PivotReportLayout.Tabular);
        pivot.ShowRowHeaders.Should().BeTrue();
        pivot.ShowColumnHeaders.Should().BeTrue();
        pivot.ShowRowStripes.Should().BeFalse();
        pivot.ShowColumnStripes.Should().BeFalse();
        pivot.PrintTitles.Should().BeFalse();
        pivot.PrintExpandCollapseButtons.Should().BeFalse();
        pivot.AltTextTitle.Should().Be("Old title");
        pivot.AltTextDescription.Should().Be("Old description");
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new TextValue("A"));
        sheet.GetCell(Addr(sheet, "E6"))!.Value.Should().Be(new TextValue("Grand Total"));
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesEmptyValueTextRefreshesAndUndoRestores()
    {
        var workbook = new Workbook("PivotEmptyValueOptionsCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(25));
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E2", "I7"),
            StyleName = "PivotStyleLight16"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            showRowHeaders: true,
            showColumnHeaders: true,
            showRowStripes: false,
            showColumnStripes: false,
            reportLayout: PivotReportLayout.Tabular,
            emptyValueText: "N/A",
            updateEmptyValueText: true);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.EmptyValueText.Should().Be("N/A");
        sheet.GetCell(Addr(sheet, "G3"))!.Value.Should().Be(new TextValue("N/A"));
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new TextValue("N/A"));

        command.Revert(ctx);

        pivot.EmptyValueText.Should().BeNull();
        sheet.GetCell(Addr(sheet, "G3"))!.Value.Should().Be(new NumberValue(0));
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new NumberValue(0));
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_PreservesEmptyValueTextWhenCallerOmitsIt()
    {
        var workbook = new Workbook("PivotEmptyValueOptionsCompatibilityTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(25));
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E2", "I7"),
            StyleName = "PivotStyleLight16",
            EmptyValueText = "-"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: false,
            showColumnGrandTotals: true,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16");

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.EmptyValueText.Should().Be("-");
        sheet.GetCell(Addr(sheet, "G3"))!.Value.Should().Be(new TextValue("-"));
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesPivotCacheDataOptionsAndUndoRestores()
    {
        var workbook = new Workbook("PivotCacheOptionsCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        var ctx = new SimpleCtx(workbook);
        var cache = new PivotCacheModel
        {
            CacheId = 7,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B2",
            RefreshOnLoad = false,
            SaveData = true,
            EnableRefresh = true
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount"));
        workbook.PivotCaches.Add(cache);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 7,
            SourceRange = Range(sheet, "A1", "B2"),
            TargetRange = Range(sheet, "D2", "F5"),
            StyleName = "PivotStyleLight16"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            refreshOnOpen: true,
            saveSourceData: false);

        command.Apply(ctx).Success.Should().BeTrue();

        cache.RefreshOnLoad.Should().BeTrue();
        cache.SaveData.Should().BeFalse();
        cache.EnableRefresh.Should().BeTrue();

        command.Revert(ctx);

        cache.RefreshOnLoad.Should().BeFalse();
        cache.SaveData.Should().BeTrue();
        cache.EnableRefresh.Should().BeTrue();
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
        var ctx = new SimpleCtx(workbook);
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
        var ctx = new SimpleCtx(workbook);
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

        workbook.Slicers.Should().ContainSingle().Which.Should().Match<SlicerModel>(slicer =>
            slicer.Name == "Category Slicer" &&
            slicer.CacheName == "Slicer_Category_Slicer" &&
            slicer.SourcePivotTableName == "PivotTable1" &&
            slicer.SourceFieldName == "Category");

        command.Revert(ctx);

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

        workbook.Timelines.Should().ContainSingle().Which.Should().Match<TimelineModel>(timeline =>
            timeline.Name == "Date Timeline" &&
            timeline.CacheName == "Timeline_Date_Timeline" &&
            timeline.SourcePivotTableName == "PivotTable1" &&
            timeline.SourceFieldName == "Date" &&
            timeline.StartDate == "2026-01-05" &&
            timeline.EndDate == "2026-02-02");

        command.Revert(ctx);

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
    public void ConfigurePivotTableCalculatedItemsCommand_ReplacesGroupingAndCalculatedDefinitions()
    {
        var workbook = new Workbook("PivotCalculatedItemsCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Units"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(2));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(3));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E3", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Units", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        var ctx = new SimpleCtx(workbook);

        var command = new ConfigurePivotTableCalculatedItemsCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [new PivotFieldModel(1, Grouping: PivotFieldGrouping.NumberRange, GroupStart: 0, GroupInterval: 10)],
            columnFields: [],
            pageFields: [],
            calculatedFields: [new PivotCalculatedFieldModel("Revenue", "Amount*Units")],
            calculatedItems: [new PivotCalculatedItemModel(1, "Small + Large", "10+20")]);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.RowFields.Should().ContainSingle().Which.Should().Be(
            new PivotFieldModel(1, Grouping: PivotFieldGrouping.NumberRange, GroupStart: 0, GroupInterval: 10));
        pivot.CalculatedFields.Should().ContainSingle().Which.Should().Be(new PivotCalculatedFieldModel("Revenue", "Amount*Units"));
        pivot.CalculatedItems.Should().ContainSingle().Which.Should().Be(new PivotCalculatedItemModel(1, "Small + Large", "10+20"));

        command.Revert(ctx);

        pivot.RowFields.Should().ContainSingle().Which.Should().Be(new PivotFieldModel(1));
        pivot.CalculatedFields.Should().BeEmpty();
        pivot.CalculatedItems.Should().BeEmpty();
    }

    [Fact]
    public void ConfigurePivotTableCalculatedItemsCommand_RejectsInvalidCalculatedDefinitions()
    {
        var workbook = new Workbook("PivotCalculatedItemsCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
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

        var command = new ConfigurePivotTableCalculatedItemsCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [new PivotFieldModel(2)],
            columnFields: [],
            pageFields: [],
            calculatedFields: [new PivotCalculatedFieldModel("", "Amount*2")],
            calculatedItems: []);

        command.Apply(ctx).Success.Should().BeFalse();

        pivot.RowFields.Should().ContainSingle().Which.Should().Be(new PivotFieldModel(0));
        pivot.CalculatedFields.Should().BeEmpty();
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

    private static void SeedData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
    }

    private static void SeedTimelineData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Date"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 5)));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(30));
    }

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
