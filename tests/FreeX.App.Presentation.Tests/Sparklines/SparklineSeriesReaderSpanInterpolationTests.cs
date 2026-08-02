using FluentAssertions;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Sparklines;

/// <summary>
/// Round-24 finding R24-sparklines-3: with <see cref="SparklineEmptyCellDisplay.Span"/> ("Connect
/// data points with line"), a blank cell must keep its original slot in the series (interpolated
/// between its real neighbors) instead of being dropped, so later points keep their original
/// x-axis spacing in <see cref="SparklineLayoutEngine.VisitLineLayout{TConsumer}"/>.
/// </summary>
public sealed class SparklineSeriesReaderSpanInterpolationTests
{
    [Fact]
    public void ReadSeries_SpanMode_KeepsBlankSlotAndInterpolatesItsValue()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var sparklineId = Guid.NewGuid();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        // Column 2 left blank.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new NumberValue(5));
        var sparkline = new SparklineModel
        {
            Id = sparklineId,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 5)),
            Location = new CellAddress(sheet.Id, 2, 1),
            Kind = SparklineKind.Line,
            DisplayEmptyCellsAs = SparklineEmptyCellDisplay.Span
        };
        sheet.Sparklines.Add(sparkline);

        var series = SparklineSeriesReader.ReadSeries(workbook, sheet, sparkline);

        // The blank keeps its slot: 5 values, not 4 -- so downstream x-positions
        // (i / (Count - 1)) land at 0, 1/4, 2/4, 3/4, 4/4 for the original 5 cells.
        series.Should().HaveCount(5);
        series[0].Should().Be(1);
        series[1].Should().Be(2, "the blank interpolates linearly between its neighbors (1 and 3)");
        series[2].Should().Be(3);
        series[3].Should().Be(4);
        series[4].Should().Be(5);
    }

    [Fact]
    public void ReadSeries_SpanMode_InterpolatesProportionallyAcrossMultipleConsecutiveBlanks()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var sparklineId = Guid.NewGuid();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(0));
        // Columns 2 and 3 left blank.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new NumberValue(9));
        var sparkline = new SparklineModel
        {
            Id = sparklineId,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 4)),
            Location = new CellAddress(sheet.Id, 2, 1),
            Kind = SparklineKind.Line,
            DisplayEmptyCellsAs = SparklineEmptyCellDisplay.Span
        };
        sheet.Sparklines.Add(sparkline);

        var series = SparklineSeriesReader.ReadSeries(workbook, sheet, sparkline);

        series.Should().HaveCount(4);
        series[0].Should().Be(0);
        series[1].Should().Be(3, "1/3 of the way from 0 to 9");
        series[2].Should().Be(6, "2/3 of the way from 0 to 9");
        series[3].Should().Be(9);
    }

    [Fact]
    public void ReadSeries_SpanMode_LeadingAndTrailingBlanksFallBackToNaN()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var sparklineId = Guid.NewGuid();
        // Column 1 left blank (leading).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        // Column 3 left blank (trailing).
        var sparkline = new SparklineModel
        {
            Id = sparklineId,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 3)),
            Location = new CellAddress(sheet.Id, 2, 1),
            Kind = SparklineKind.Line,
            DisplayEmptyCellsAs = SparklineEmptyCellDisplay.Span
        };
        sheet.Sparklines.Add(sparkline);

        var series = SparklineSeriesReader.ReadSeries(workbook, sheet, sparkline);

        series.Should().HaveCount(3, "blank slots are kept even when they can't be interpolated");
        double.IsNaN(series[0]).Should().BeTrue("no real value precedes the leading blank to connect from");
        series[1].Should().Be(2);
        double.IsNaN(series[2]).Should().BeTrue("no real value follows the trailing blank to connect to");
    }
}
