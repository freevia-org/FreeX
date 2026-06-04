using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableCommandTests
{
    [Fact]
    public void AddPivotChartCommand_AddsBoundChartFromPivotOutputAndUndoRemovesIt()
    {
        var workbook = new Workbook("PivotChartCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
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
        var ctx = new TestCommandContext(workbook);

        var command = new AddPivotChartCommand(sheet.Id, "MissingPivot", ChartType.Column);

        command.Apply(ctx).Success.Should().BeFalse();
        sheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddPivotChartCommand_RejectsDeferredAdvancedFamiliesBeforeRefresh()
    {
        var workbook = new Workbook("PivotChartCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        sheet.PivotTables.Add(CreateCategoryAmountPivot(sheet));
        var ctx = new TestCommandContext(workbook);

        var outcome = new AddPivotChartCommand(sheet.Id, "PivotTable1", ChartType.Map).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("recognized for XLSX preservation");
        sheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddPivotChartCommand_RejectsProtectedSheetWithoutUsePivotReportsPermission()
    {
        var workbook = new Workbook("PivotChartProtectionTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        sheet.PivotTables.Add(CreateCategoryAmountPivot(sheet));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        var ctx = new TestCommandContext(workbook);

        var outcome = new AddPivotChartCommand(sheet.Id, "PivotTable1", ChartType.Column).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddPivotChartCommand_AllowsProtectedSheetWithObjectAndPivotReportPermissions()
    {
        var workbook = new Workbook("PivotChartProtectionTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        sheet.PivotTables.Add(CreateCategoryAmountPivot(sheet));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);
        var ctx = new TestCommandContext(workbook);

        var outcome = new AddPivotChartCommand(sheet.Id, "PivotTable1", ChartType.Column).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Charts.Should().ContainSingle().Which.IsPivotChart.Should().BeTrue();
    }

    [Fact]
    public void ChangePivotChartTypeCommand_ChangesTypeAndPreservesPivotBindingAndUndoRestores()
    {
        var workbook = new Workbook("PivotChartTypeCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
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
        var ctx = new TestCommandContext(workbook);
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
    public void ChangePivotChartTypeCommand_RejectsDeferredAdvancedFamiliesBeforeMutation()
    {
        var workbook = new Workbook("PivotChartTypeCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = Range(sheet, "D3", "E5"),
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            PivotCacheId = 7
        };
        sheet.Charts.Add(chart);

        var outcome = new ChangePivotChartTypeCommand(sheet.Id, chart.Id, ChartType.Map).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("recognized for XLSX preservation");
        chart.Type.Should().Be(ChartType.Column);
    }

    [Fact]
    public void ChangePivotChartTypeCommand_RejectsProtectedSheetWithoutUsePivotReportsPermission()
    {
        var workbook = new Workbook("PivotChartTypeProtectionTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = Range(sheet, "D3", "E5"),
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            PivotCacheId = 7
        };
        sheet.Charts.Add(chart);
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        var ctx = new TestCommandContext(workbook);

        var outcome = new ChangePivotChartTypeCommand(sheet.Id, chart.Id, ChartType.Line).Apply(ctx);

        outcome.Success.Should().BeFalse();
        chart.Type.Should().Be(ChartType.Column);
    }
}
