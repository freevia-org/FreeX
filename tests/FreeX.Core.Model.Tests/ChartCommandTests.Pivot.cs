using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class ChartCommandTests
{

    [Fact]
    public void ConfigurePivotChartOptionsCommand_UpdatesStyleAndFieldButtonsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            ChartStyleId = 4,
            ShowPivotChartReportFilterButtons = true,
            ShowPivotChartAxisFieldButtons = true,
            ShowPivotChartValueFieldButtons = true
        };
        sheet.Charts.Add(chart);

        var command = new ConfigurePivotChartOptionsCommand(
            sheet.Id,
            chart.Id,
            99,
            showFieldButtons: false,
            showReportFilterButtons: false,
            showAxisFieldButtons: true,
            showValueFieldButtons: false);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.ChartStyleId.Should().Be(48);
        chart.ShowPivotChartFieldButtons.Should().BeFalse();
        chart.ShowPivotChartReportFilterButtons.Should().BeFalse();
        chart.ShowPivotChartAxisFieldButtons.Should().BeTrue();
        chart.ShowPivotChartValueFieldButtons.Should().BeFalse();

        command.Revert(ctx);

        chart.ChartStyleId.Should().Be(4);
        chart.ShowPivotChartFieldButtons.Should().BeTrue();
        chart.ShowPivotChartReportFilterButtons.Should().BeTrue();
        chart.ShowPivotChartAxisFieldButtons.Should().BeTrue();
        chart.ShowPivotChartValueFieldButtons.Should().BeTrue();
    }

    [Fact]
    public void ConfigurePivotChartOptionsCommand_RejectsNormalCharts()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range
        };
        sheet.Charts.Add(chart);

        var outcome = new ConfigurePivotChartOptionsCommand(sheet.Id, chart.Id, 12, showFieldButtons: false).Apply(ctx);

        outcome.Success.Should().BeFalse();
        chart.ChartStyleId.Should().BeNull();
        chart.ShowPivotChartFieldButtons.Should().BeTrue();
    }

    [Fact]
    public void ConfigurePivotChartOptionsCommand_RejectsProtectedSheetWithoutUsePivotReportsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            ChartStyleId = 4
        };
        sheet.Charts.Add(chart);
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);

        var outcome = new ConfigurePivotChartOptionsCommand(sheet.Id, chart.Id, 12, showFieldButtons: false).Apply(ctx);

        outcome.Success.Should().BeFalse();
        chart.ChartStyleId.Should().Be(4);
        chart.ShowPivotChartFieldButtons.Should().BeTrue();
    }

    [Fact]
    public void ConfigurePivotChartOptionsCommand_PreservesIndividualButtonsWhenCallerOmitsThem()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            ShowPivotChartReportFilterButtons = false,
            ShowPivotChartAxisFieldButtons = true,
            ShowPivotChartValueFieldButtons = false
        };
        sheet.Charts.Add(chart);

        var command = new ConfigurePivotChartOptionsCommand(sheet.Id, chart.Id, 12, showFieldButtons: false);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.ShowPivotChartFieldButtons.Should().BeFalse();
        chart.ShowPivotChartReportFilterButtons.Should().BeFalse();
        chart.ShowPivotChartAxisFieldButtons.Should().BeTrue();
        chart.ShowPivotChartValueFieldButtons.Should().BeFalse();
    }

    [Fact]
    public void ConfigurePivotChartOptionsCommand_CreatesDataTableAndUndoRestores()
    {
        var workbook = new Workbook("PivotChartOptionsDataTableCommandTest");
        var sheet = workbook.AddSheet("Sheet1");
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            DataTable = null
        };
        sheet.Charts.Add(chart);
        var ctx = new SimpleCtx(workbook);

        var command = new ConfigurePivotChartOptionsCommand(
            sheet.Id,
            chart.Id,
            12,
            showFieldButtons: true,
            showDataTable: true,
            showDataTableLegendKeys: true);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.DataTable.Should().NotBeNull();
        chart.DataTable!.ShowLegendKeys.Should().BeTrue();
        chart.DataTable.ShowHorizontalBorder.Should().BeTrue();
        chart.DataTable.ShowVerticalBorder.Should().BeTrue();
        chart.DataTable.ShowOutline.Should().BeTrue();

        command.Revert(ctx);

        chart.DataTable.Should().BeNull();
    }

    [Fact]
    public void ConfigurePivotChartOptionsCommand_PreservesExistingDataTableFormattingWhenShowingDataTable()
    {
        var workbook = new Workbook("PivotChartOptionsFormattedDataTableCommandTest");
        var sheet = workbook.AddSheet("Sheet1");
        var existingDataTable = new ChartDataTableModel
        {
            ShowHorizontalBorder = false,
            ShowVerticalBorder = true,
            ShowOutline = false,
            ShowLegendKeys = false,
            FillColor = new CellColor(10, 20, 30),
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25),
            BorderColor = new CellColor(40, 50, 60),
            BorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25),
            BorderThickness = 2.5,
            TextColor = new CellColor(70, 80, 90),
            TextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1),
            FontSize = 13.5
        };
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            DataTable = existingDataTable
        };
        sheet.Charts.Add(chart);
        var ctx = new SimpleCtx(workbook);

        var command = new ConfigurePivotChartOptionsCommand(
            sheet.Id,
            chart.Id,
            12,
            showFieldButtons: true,
            showDataTable: true,
            showDataTableLegendKeys: true);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.DataTable.Should().BeSameAs(existingDataTable);
        chart.DataTable!.ShowLegendKeys.Should().BeTrue();
        chart.DataTable.ShowHorizontalBorder.Should().BeFalse();
        chart.DataTable.ShowVerticalBorder.Should().BeTrue();
        chart.DataTable.ShowOutline.Should().BeFalse();
        chart.DataTable.FillColor.Should().Be(new CellColor(10, 20, 30));
        chart.DataTable.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25));
        chart.DataTable.BorderColor.Should().Be(new CellColor(40, 50, 60));
        chart.DataTable.BorderThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25));
        chart.DataTable.BorderThickness.Should().Be(2.5);
        chart.DataTable.TextColor.Should().Be(new CellColor(70, 80, 90));
        chart.DataTable.TextThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1));
        chart.DataTable.FontSize.Should().Be(13.5);
    }

    [Fact]
    public void ConfigurePivotChartOptionsCommand_UndoRestoresDataTableFormattingWhenOnlyLegendKeysChange()
    {
        var workbook = new Workbook("PivotChartOptionsDataTableUndoFormattingCommandTest");
        var sheet = workbook.AddSheet("Sheet1");
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            DataTable = new ChartDataTableModel
            {
                ShowLegendKeys = false,
                FillColor = new CellColor(11, 22, 33),
                FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.1),
                BorderColor = new CellColor(44, 55, 66),
                BorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.1),
                BorderThickness = 3,
                TextColor = new CellColor(77, 88, 99),
                TextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Light1),
                FontSize = 14
            }
        };
        sheet.Charts.Add(chart);
        var ctx = new SimpleCtx(workbook);

        var command = new ConfigurePivotChartOptionsCommand(
            sheet.Id,
            chart.Id,
            12,
            showFieldButtons: true,
            showDataTableLegendKeys: true);

        command.Apply(ctx).Success.Should().BeTrue();
        chart.DataTable!.ShowLegendKeys.Should().BeTrue();

        command.Revert(ctx);

        chart.DataTable.Should().NotBeNull();
        chart.DataTable!.ShowLegendKeys.Should().BeFalse();
        chart.DataTable.FillColor.Should().Be(new CellColor(11, 22, 33));
        chart.DataTable.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.1));
        chart.DataTable.BorderColor.Should().Be(new CellColor(44, 55, 66));
        chart.DataTable.BorderThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.1));
        chart.DataTable.BorderThickness.Should().Be(3);
        chart.DataTable.TextColor.Should().Be(new CellColor(77, 88, 99));
        chart.DataTable.TextThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Light1));
        chart.DataTable.FontSize.Should().Be(14);
    }

    [Fact]
    public void ConfigurePivotChartOptionsCommand_UpdatesDesignFlagsAndUndoRestores()
    {
        var workbook = new Workbook("PivotChartOptionsDesignCommandTest");
        var sheet = workbook.AddSheet("Sheet1");
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            RoundedCorners = false,
            ShowDataInHiddenRowsAndColumns = false,
            BlankDisplayMode = ChartBlankDisplayMode.Gap
        };
        sheet.Charts.Add(chart);
        var ctx = new SimpleCtx(workbook);

        var command = new ConfigurePivotChartOptionsCommand(
            sheet.Id,
            chart.Id,
            12,
            showFieldButtons: true,
            roundedCorners: true,
            showHiddenData: true,
            blankDisplayMode: ChartBlankDisplayMode.Zero);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.RoundedCorners.Should().BeTrue();
        chart.ShowDataInHiddenRowsAndColumns.Should().BeTrue();
        chart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Zero);

        command.Revert(ctx);

        chart.RoundedCorners.Should().BeFalse();
        chart.ShowDataInHiddenRowsAndColumns.Should().BeFalse();
        chart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Gap);
    }
}
