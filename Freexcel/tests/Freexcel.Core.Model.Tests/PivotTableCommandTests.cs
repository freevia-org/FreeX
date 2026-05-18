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
    public void AddPivotTableCommand_RejectsSourceRangeOnDifferentSheet()
    {
        var workbook = new Workbook("PivotCommandTest");
        var sheet1 = workbook.AddSheet("Data");
        var sheet2 = workbook.AddSheet("Other");
        SeedData(sheet2);
        var ctx = new SimpleCtx(workbook);

        var command = new AddPivotTableCommand(
            sheet1.Id,
            Range(sheet2, "A1", "B3"),
            Range(sheet1, "D3", "E5"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);

        command.Apply(ctx).Success.Should().BeFalse();
        sheet1.PivotTables.Should().BeEmpty();
        workbook.PivotCaches.Should().BeEmpty();
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

    private static void SeedData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
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
