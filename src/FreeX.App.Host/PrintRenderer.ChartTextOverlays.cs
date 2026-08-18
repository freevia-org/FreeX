using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    private static readonly Typeface PrintedChartTypeface = new(PrintChartTextOverlayPlanner.FontFamily);

    private static void AddPrintedChartTextOverlays(
        ICollection<PdfTextOverlay> textOverlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        Rect chartRect,
        ViewportModel viewport,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> pageCellLookup,
        Sheet sheet)
    {
        // `sheet` lets the planner drop hidden-row/column cells that reach it through pageCellLookup
        // (built from ViewportModel.Cells, which deliberately retains hidden merge-anchor rows) when the
        // chart has "Show data in hidden rows and columns" off -- viewport.ChartDataCells suppresses
        // those by omission, which on its own just falls through to the un-filtered page value.
        var overlayPlans = PrintChartTextOverlayPlanner.Build(
            chart,
            workbookTheme,
            ToLayoutRect(chartRect),
            viewport.ChartDataCells,
            pageCellLookup,
            MeasurePrintedChartOverlayText,
            sheet);

        foreach (var overlay in overlayPlans)
            textOverlays.Add(CreatePrintedChartTextOverlay(overlay));
    }

    private static PrintChartOverlayTextMetrics MeasurePrintedChartOverlayText(string text, double fontSize)
    {
        var measured = MeasurePrintedChartText(text, fontSize);
        return new PrintChartOverlayTextMetrics(
            measured.Width,
            measured.WidthIncludingTrailingWhitespace);
    }

    private static PdfTextOverlay CreatePrintedChartTextOverlay(PrintChartTextOverlayPlan overlay) =>
        new(
            overlay.Text,
            overlay.X,
            overlay.Y,
            overlay.FontSize,
            PrintedChartTypeface.FontFamily.Source,
            Bold: false,
            Italic: false,
            Color.FromRgb(overlay.Color.R, overlay.Color.G, overlay.Color.B))
        {
            RotationDegrees = overlay.RotationDegrees
        };

    private static FormattedText MeasurePrintedChartText(string text, double fontSize) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            PrintedChartTypeface,
            fontSize,
            Brushes.Black,
            1.0);

    private static LayoutRect ToLayoutRect(Rect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);
}
