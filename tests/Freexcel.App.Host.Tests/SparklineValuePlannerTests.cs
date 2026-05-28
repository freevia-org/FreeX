using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class SparklineValuePlannerTests
{
    [Fact]
    public void BuildValues_CollectsSupportedScalarValuesInRowMajorOrder()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var sparklineId = Guid.NewGuid();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(12.5));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), DateTimeValue.FromDateTime(new DateTime(2026, 5, 19)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("ignored"));
        sheet.Sparklines.Add(new SparklineModel
        {
            Id = sparklineId,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 2)),
            Location = new CellAddress(sheet.Id, 1, 3),
            Kind = SparklineKind.Line
        });

        var values = SparklineValuePlanner.BuildValues(sheet);

        values.Should().ContainKey(sparklineId);
        values[sparklineId].Should().Equal(
            12.5,
            DateTimeValue.FromDateTime(new DateTime(2026, 5, 19)).Value,
            1);
    }

    [Fact]
    public void BuildValues_SkipsHiddenAndFilteredRowsAndColumns()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var sparklineId = Guid.NewGuid();
        var value = 1;
        for (uint row = 1; row <= 4; row++)
        {
            for (uint col = 1; col <= 4; col++)
            {
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(value++));
            }
        }

        sheet.HiddenRows.Add(2);
        sheet.FilterHiddenRows.Add(3);
        sheet.GroupHiddenRows.Add(4);
        sheet.HiddenCols.Add(2);
        sheet.GroupHiddenCols.Add(3);
        sheet.Sparklines.Add(new SparklineModel
        {
            Id = sparklineId,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 4)),
            Location = new CellAddress(sheet.Id, 1, 5),
            Kind = SparklineKind.Line
        });

        var values = SparklineValuePlanner.BuildValues(sheet);

        values.Should().ContainKey(sparklineId);
        values[sparklineId].Should().Equal(1, 4);
    }
}
