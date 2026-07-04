using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for group H-sparklines finding H12: Duplicate Sheet must deep-copy every
/// sparkline group setting (colors, axis scaling, markers, custom min/max, hidden/empty handling),
/// not just DataRange/Location/Kind — matching Excel's Move-or-Copy behavior, which preserves
/// sparkline groups verbatim.
/// </summary>
public sealed class HSparklinesRegressionTests
{
    [Fact]
    public void DuplicateSheet_CopiesAllSparklineGroupSettings_NotJustLocationAndKind()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5));
        var location = new CellAddress(sheet.Id, 1, 6);

        var source = new SparklineModel
        {
            DataRange = dataRange,
            Location = location,
            Kind = SparklineKind.Column,
            GroupId = 7,
            ShowMarkers = true,
            ShowHighPoint = true,
            ShowLowPoint = true,
            ShowFirstPoint = true,
            ShowLastPoint = true,
            ShowNegativePoints = true,
            ShowAxis = true,
            DisplayHidden = true,
            RightToLeft = true,
            SeriesColor = new CellColor(0x11, 0x22, 0x33),
            NegativeColor = new CellColor(0xAA, 0x00, 0x00),
            AxisColor = new CellColor(0x00, 0x00, 0x00),
            MarkersColor = new CellColor(0x00, 0xFF, 0x00),
            HighPointColor = new CellColor(0xFF, 0xA5, 0x00),
            LowPointColor = new CellColor(0x80, 0x00, 0x80),
            FirstPointColor = new CellColor(0x00, 0x00, 0xFF),
            LastPointColor = new CellColor(0xC0, 0xC0, 0xC0),
            LineWeight = 2.0,
            MinAxisType = SparklineAxisScaling.Group,
            MaxAxisType = SparklineAxisScaling.Custom,
            ManualMin = -5.0,
            ManualMax = 100.0,
            DisplayEmptyCellsAs = SparklineEmptyCellDisplay.Zero
        };
        sheet.Sparklines.Add(source);

        var command = new DuplicateSheetCommand(sheet.Id);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var copy = wb.Sheets[1];
        var copied = copy.Sparklines.Should().ContainSingle().Subject;

        copied.Kind.Should().Be(source.Kind);
        copied.GroupId.Should().Be(source.GroupId);
        copied.ShowMarkers.Should().Be(source.ShowMarkers);
        copied.ShowHighPoint.Should().Be(source.ShowHighPoint);
        copied.ShowLowPoint.Should().Be(source.ShowLowPoint);
        copied.ShowFirstPoint.Should().Be(source.ShowFirstPoint);
        copied.ShowLastPoint.Should().Be(source.ShowLastPoint);
        copied.ShowNegativePoints.Should().Be(source.ShowNegativePoints);
        copied.ShowAxis.Should().Be(source.ShowAxis);
        copied.DisplayHidden.Should().Be(source.DisplayHidden);
        copied.RightToLeft.Should().Be(source.RightToLeft);
        copied.SeriesColor.Should().Be(source.SeriesColor);
        copied.NegativeColor.Should().Be(source.NegativeColor);
        copied.AxisColor.Should().Be(source.AxisColor);
        copied.MarkersColor.Should().Be(source.MarkersColor);
        copied.HighPointColor.Should().Be(source.HighPointColor);
        copied.LowPointColor.Should().Be(source.LowPointColor);
        copied.FirstPointColor.Should().Be(source.FirstPointColor);
        copied.LastPointColor.Should().Be(source.LastPointColor);
        copied.LineWeight.Should().Be(source.LineWeight);
        copied.MinAxisType.Should().Be(source.MinAxisType);
        copied.MaxAxisType.Should().Be(source.MaxAxisType);
        copied.ManualMin.Should().Be(source.ManualMin);
        copied.ManualMax.Should().Be(source.ManualMax);
        copied.DisplayEmptyCellsAs.Should().Be(source.DisplayEmptyCellsAs);

        // DataRange/Location must still be remapped onto the copy's own sheet id.
        copied.DataRange.Start.Sheet.Should().Be(copy.Id);
        copied.Location.Sheet.Should().Be(copy.Id);
    }
}
