using FluentAssertions;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Sparklines;

public sealed class SparklineSeriesReaderTests
{
    [Fact]
    public void BuildValues_CollectsSupportedScalarValuesInRowMajorOrder()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
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

        var values = SparklineSeriesReader.BuildValues(workbook, sheet);

        values.Should().ContainKey(sparklineId);
        // Round-8 finding N5: text cells are treated as blank, and the default DisplayEmptyCellsAs
        // (Gap) keeps the blank's position in the series as NaN so the layout engine breaks the
        // line there, matching Excel, instead of silently dropping the position.
        values[sparklineId].Should().HaveCount(4);
        values[sparklineId][0].Should().Be(12.5);
        values[sparklineId][1].Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 19)).Value);
        values[sparklineId][2].Should().Be(1);
        double.IsNaN(values[sparklineId][3]).Should().BeTrue("the text cell is treated as blank and Gap keeps its position as NaN");
    }

    [Fact]
    public void BuildValues_SkipsHiddenAndFilteredRowsAndColumns()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
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

        var values = SparklineSeriesReader.BuildValues(workbook, sheet);

        values.Should().ContainKey(sparklineId);
        values[sparklineId].Should().Equal(1, 4);
    }

    [Fact]
    public void BuildValues_SkipsOversizedSourceRanges()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var sparklineId = Guid.NewGuid();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.Sparklines.Add(new SparklineModel
        {
            Id = sparklineId,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, (uint)(SparklineRangeLimits.MaxDataCellCount + 1), 1)),
            Location = new CellAddress(sheet.Id, 1, 3),
            Kind = SparklineKind.Line
        });

        var values = SparklineSeriesReader.BuildValues(workbook, sheet);

        values.Should().ContainKey(sparklineId);
        values[sparklineId].Should().BeEmpty();
    }
}
