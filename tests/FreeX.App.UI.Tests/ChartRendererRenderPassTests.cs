using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// The grid draws charts from <c>OnRender</c>. OxyPlot's PngExporter returns a blank bitmap when it
/// runs re-entrantly inside the UI thread's render pass, which silently downgraded every grid chart
/// to the simplified direct fallback (no trendlines, error bars, bubbles). Pins that the real
/// OxyPlot export is used from inside a shown window's render pass.
/// </summary>
public sealed class ChartRendererRenderPassTests
{
    private sealed class ChartHost : FrameworkElement
    {
        public ChartModel Chart = null!;
        public ViewportModel Viewport = null!;
        public ImageSource? Image;

        protected override void OnRender(DrawingContext drawingContext) =>
            Image = ChartRenderer.Render(Chart, Viewport, WorkbookTheme.Office, 2.0);
    }

    [Fact]
    public void Render_InsideShownWindowRenderPass_UsesOxyPlotExportNotDirectFallback()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2)),
                Width = 400,
                Height = 300,
            };
            var viewport = new ViewportModel(
                [
                    new DisplayCell(1, 1, new TextValue("Quarter"), "Quarter", null, StyleId.Default, null),
                    new DisplayCell(1, 2, new TextValue("Revenue"), "Revenue", null, StyleId.Default, null),
                    new DisplayCell(2, 1, new TextValue("Q1"), "Q1", null, StyleId.Default, null),
                    new DisplayCell(2, 2, new NumberValue(10), "10", null, StyleId.Default, null),
                    new DisplayCell(3, 1, new TextValue("Q2"), "Q2", null, StyleId.Default, null),
                    new DisplayCell(3, 2, new NumberValue(18), "18", null, StyleId.Default, null),
                    new DisplayCell(4, 1, new TextValue("Q3"), "Q3", null, StyleId.Default, null),
                    new DisplayCell(4, 2, new NumberValue(14), "14", null, StyleId.Default, null),
                ],
                [],
                []);
            var host = new ChartHost { Chart = chart, Viewport = viewport };
            var window = new Window
            {
                Content = host,
                Width = 400,
                Height = 300,
                Left = -32000,
                Top = -32000,
                ShowInTaskbar = false,
                ShowActivated = false,
            };

            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

                host.Image.Should().NotBeNull();
                host.Image.Should().BeOfType<BitmapImage>(
                    "RenderTargetBitmap is the simplified direct fallback used when the OxyPlot export is blank");
            }
            finally
            {
                window.Close();
            }
        });
    }
}
