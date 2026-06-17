using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using System.Windows.Media.Imaging;

namespace FreeX.App.UI.Tests;

/// <summary>
/// A chartsheet renders its single chart full-window. The chart's data lives on another worksheet
/// (e.g. the tealeg testchartsheet's "Chart1" plots "Sheet1"!A1:A5), so the host builds a viewport
/// from the data sheet and the renderer matches cells by row/col. This guards that a full-window
/// sized line chart produces a visible (non-blank) bitmap from that data.
/// </summary>
public sealed class ChartsheetRenderTests
{
    [Fact]
    public void Render_FullWindowLineChartsheet_ProducesNonBlankBitmap()
    {
        WpfTestThread.Run(() =>
        {
            var dataSheetId = SheetId.New();
            var chart = new ChartModel
            {
                Type = ChartType.Line,
                // Mirrors tealeg testchartsheet: title "Value", series Sheet1!$A$1 (name) + $A$2:$A$5.
                Title = "Value",
                DataRange = new GridRange(
                    new CellAddress(dataSheetId, 1, 1),
                    new CellAddress(dataSheetId, 5, 1)),
                // Full-window dimensions, as the chartsheet host sizes the chart to the viewport.
                Width = 1000,
                Height = 700
            };

            var image = ChartRenderer.Render(
                chart,
                new ViewportModel(
                    [
                        new DisplayCell(1, 1, new TextValue("Value"), "Value", null, StyleId.Default, null),
                        new DisplayCell(2, 1, new NumberValue(1), "1", null, StyleId.Default, null),
                        new DisplayCell(3, 1, new NumberValue(2), "2", null, StyleId.Default, null),
                        new DisplayCell(4, 1, new NumberValue(3), "3", null, StyleId.Default, null),
                        new DisplayCell(5, 1, new NumberValue(4), "4", null, StyleId.Default, null)
                    ],
                    [],
                    []),
                WorkbookTheme.Office,
                renderScale: 1.0);

            var bitmap = image.Should().BeAssignableTo<BitmapSource>().Subject;
            // A line series plus title/axes/gridlines is far sparser than a filled column chart, but
            // still clearly non-blank (a blank chart renders ~0 non-white pixels).
            ChartRendererTests.CountVisiblePixels(bitmap).Should().BeGreaterThan(200);
        });
    }
}
