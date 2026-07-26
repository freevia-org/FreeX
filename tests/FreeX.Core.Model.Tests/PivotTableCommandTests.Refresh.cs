using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableCommandTests
{
    [Fact]
    public void RefreshPivotTableCommand_RefreshesAndUndoRestoresPreviousCells()
    {
        var workbook = new Workbook("PivotCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "E5"),
            // R90-render-pivot-layout-5-3: pin the (former) Tabular default -- this test is about
            // RefreshPivotTableCommand's undo/redo cell restoration, not the pivot-creation default.
            ReportLayout = PivotReportLayout.Tabular
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
    public void RefreshPivotTableCommand_RejectsProtectedSheetWithoutUsePivotReportsPermission()
    {
        var workbook = new Workbook("PivotProtectionTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
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
        sheet.IsProtected = true;

        var outcome = new RefreshPivotTableCommand(sheet.Id, "PivotTable1").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetCell(Addr(sheet, "D3")).Should().BeNull();
    }

    [Fact]
    public void RefreshPivotTableCommand_AllowsProtectedSheetWithUsePivotReportsPermission()
    {
        var workbook = new Workbook("PivotProtectionTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "E5"),
            // R90-render-pivot-layout-5-3: pin the (former) Tabular default -- this test is about the
            // sheet-protection permission gate, not the pivot-creation default.
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);

        var outcome = new RefreshPivotTableCommand(sheet.Id, "PivotTable1").Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(Addr(sheet, "D3"))!.Value.Should().Be(new TextValue("Category"));
    }

    [Fact]
    public void RefreshPivotTableCommand_UpdatesBoundPivotChartDataRange()
    {
        var workbook = new Workbook("PivotChartRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
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
}
