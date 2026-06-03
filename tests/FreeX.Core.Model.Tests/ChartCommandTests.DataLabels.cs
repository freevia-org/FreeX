using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class ChartCommandTests
{

    [Fact]
    public void SetChartLayoutCommand_ClearsPercentageDataLabelStateWhenUnsupported()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelPercentage: true,
                ShowDataLabelCategoryName: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].ShowDataLabels.Should().BeTrue();
        sheet.Charts[0].ShowDataLabelCategoryName.Should().BeTrue();
        sheet.Charts[0].ShowDataLabelPercentage.Should().BeFalse();
    }

    [Fact]
    public void SetChartLayoutCommand_SanitizesPointDataLabelFormatsToExistingPoints()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                PointDataLabelFormats:
                [
                    new ChartPointDataLabelFormat(-1, 0, FillColor: new CellColor(255, 0, 0)),
                    new ChartPointDataLabelFormat(0, -1, FillColor: new CellColor(255, 0, 0)),
                    new ChartPointDataLabelFormat(0, 0, FillColor: new CellColor(0, 114, 178)),
                    new ChartPointDataLabelFormat(0, 0, FillColor: new CellColor(112, 48, 160)),
                    new ChartPointDataLabelFormat(1, 2, FillColor: new CellColor(255, 192, 0)),
                    new ChartPointDataLabelFormat(2, 0, FillColor: new CellColor(255, 0, 0))
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].PointDataLabelFormats.Should().ContainSingle().Which.Should().Be(
            new ChartPointDataLabelFormat(0, 0, FillColor: new CellColor(112, 48, 160)));
    }

    [Fact]
    public void SetChartLayoutCommand_DropsEmptyPointDataLabelFormats()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                PointDataLabelFormats:
                [
                    new ChartPointDataLabelFormat(0, 0)
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].PointDataLabelFormats.Should().BeEmpty();
    }

    [Fact]
    public void SetChartLayoutCommand_ClampsPointDataLabelFormatWeightsAndFontSizes()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                PointDataLabelFormats:
                [
                    new ChartPointDataLabelFormat(0, 0, BorderThickness: 25, FontSize: 2)
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        var format = sheet.Charts[0].PointDataLabelFormats.Should().ContainSingle().Subject;
        format.BorderThickness.Should().Be(10);
        format.FontSize.Should().Be(6);
    }
}
