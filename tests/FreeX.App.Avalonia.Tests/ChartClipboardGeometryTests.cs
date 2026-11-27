using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class ChartClipboardGeometryTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task ClipboardChartVisual_ContainsDataBarsAndChangesWithRelativeValues() =>
        Session.Dispatch(() =>
        {
            var workbook = new Workbook();
            var sheet = workbook.AddSheet("Data");
            var chart = CreateChart(sheet);
            var bars = ReadBars(workbook, chart);
            bars.Should().HaveCount(3);
            bars[1].Height.Should().BeApproximately(bars[0].Height * 2, 0.01);
            bars[2].Height.Should().BeApproximately(bars[0].Height * 4, 0.01);

            sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(30));
            var updated = ReadBars(workbook, chart);
            updated[0].Height.Should().BeApproximately(updated[2].Height * 0.75, 0.01);
            updated[2].Height.Should().BeApproximately(bars[2].Height, 0.01);
            return true;
        }, CancellationToken.None);

    [Fact]
    public Task ClipboardChartVisual_UsesReferencedSheetOutsideViewport() =>
        Session.Dispatch(() =>
        {
            var workbook = new Workbook();
            var source = workbook.AddSheet("Source");
            var chart = CreateChart(source, startRow: 1000);
            var destination = workbook.AddSheet("Chart");
            destination.Charts.Add(chart);
            for (uint row = 1001; row <= 1003; row++)
                destination.SetCell(new CellAddress(destination.Id, row, 2), new NumberValue(1));

            var bars = ReadBars(workbook, chart);
            bars.Should().HaveCount(3);
            bars[2].Height.Should().BeApproximately(bars[0].Height * 4, 0.01,
                "chart data must come from its referenced sheet, regardless of the active viewport");
            return true;
        }, CancellationToken.None);

    private static ChartModel CreateChart(Sheet sheet, uint startRow = 1)
    {
        sheet.SetCell(new CellAddress(sheet.Id, startRow, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, startRow, 2), new TextValue("Sales"));
        for (uint i = 0; i < 3; i++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, startRow + i + 1, 1), new TextValue($"Item {i}"));
            sheet.SetCell(new CellAddress(sheet.Id, startRow + i + 1, 2), new NumberValue(10 * (1 << (int)i)));
        }
        return new ChartModel
        {
            Title = "Sales", Type = ChartType.Column, Width = 320, Height = 240, ShowLegend = false,
            DataRange = new GridRange(new CellAddress(sheet.Id, startRow, 1), new CellAddress(sheet.Id, startRow + 3, 2)),
            SeriesFormats = [new ChartSeriesFormat(0, FillColor: new CellColor(17, 90, 150))],
        };
    }

    private static Rectangle[] ReadBars(Workbook workbook, ChartModel chart)
    {
        var visual = DrawingObjectClipboardImageRenderer.CreateChartVisual(workbook, chart);
        visual.Should().NotBeNull();
        visual!.Width.Should().Be(320);
        visual.Height.Should().Be(240);
        return visual.Children.OfType<Rectangle>()
            .Where(rect => rect.Fill is ISolidColorBrush brush && brush.Color == Color.FromRgb(17, 90, 150))
            .OrderBy(Canvas.GetLeft).ToArray();
    }
}
