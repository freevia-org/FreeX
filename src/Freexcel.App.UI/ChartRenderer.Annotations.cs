using OxyPlot;
using OxyPlot.Annotations;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public static partial class ChartRenderer
{
    private static void AddPivotChartFieldButtons(PlotModel model, ChartModel chart)
    {
        if (!chart.IsPivotChart || !chart.ShowPivotChartFieldButtons)
            return;

        var captions = new List<string>();
        if (chart.ShowPivotChartReportFilterButtons)
            captions.Add(string.IsNullOrWhiteSpace(chart.PivotTableName) ? "PivotTable" : chart.PivotTableName);
        if (chart.ShowPivotChartAxisFieldButtons)
            captions.Add("Axis Fields");
        if (chart.ShowPivotChartValueFieldButtons)
            captions.Add("Values");

        for (var index = 0; index < captions.Count; index++)
        {
            model.Annotations.Add(new TextAnnotation
            {
                Text = captions[index],
                TextPosition = new DataPoint(index * 1.2, 0),
                Stroke = OxyColor.FromRgb(128, 128, 128),
                StrokeThickness = 1,
                Background = OxyColor.FromRgb(242, 242, 242),
                TextColor = OxyColor.FromRgb(64, 64, 64),
                FontSize = 10,
                Padding = new OxyThickness(4, 2, 4, 2)
            });
        }
    }

    private static void AddChartDataTableAnnotations(
        PlotModel model,
        ChartModel chart,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyList<string> categories,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol,
        uint headerRow)
    {
        if (chart.DataTable is null || model.Axes.Count == 0)
            return;

        var rows = new List<string>();
        if (chart.FirstRowIsHeader)
        {
            var headers = new List<string>();
            for (uint col = dataStartCol; col <= endCol; col++)
            {
                if (ShouldSkipScatterXColumn(chart, col, dataStartCol))
                    continue;

                var header = cellLookup.TryGetValue((headerRow, col), out var cell) && !string.IsNullOrWhiteSpace(cell.DisplayText)
                    ? cell.DisplayText
                    : $"Series {headers.Count + 1}";
                headers.Add(chart.DataTable.ShowLegendKeys == true ? $"* {header}" : header);
            }

            if (headers.Count > 0)
                rows.Add(string.Join(" | ", headers));
        }

        var categoryIndex = 0;
        for (uint row = dataStartRow; row <= endRow; row++, categoryIndex++)
        {
            var values = new List<string>
            {
                categories.Count > categoryIndex ? categories[categoryIndex] : ""
            };

            for (uint col = dataStartCol; col <= endCol; col++)
            {
                if (ShouldSkipScatterXColumn(chart, col, dataStartCol))
                    continue;
                values.Add(cellLookup.TryGetValue((row, col), out var cell) ? cell.DisplayText : "");
            }

            rows.Add(string.Join(" | ", values));
        }

        if (rows.Count == 0)
            return;

        var outline = chart.DataTable.ShowOutline == true;
        var showHorizontal = chart.DataTable.ShowHorizontalBorder != false;
        var strokeColor = outline || showHorizontal
            ? ToOxyColor(chart.DataTable.BorderColor) ?? OxyColor.FromRgb(166, 166, 166)
            : OxyColors.Transparent;
        var strokeThickness = outline || showHorizontal
            ? GetChartDataTableBorderThickness(chart.DataTable)
            : 0;
        var background = ToOxyColor(chart.DataTable.FillColor) ?? OxyColor.FromAColor(225, OxyColors.White);
        var textColor = ToOxyColor(chart.DataTable.TextColor) ?? OxyColor.FromRgb(64, 64, 64);
        var fontSize = GetChartDataTableFontSize(chart.DataTable);
        for (var index = 0; index < rows.Count; index++)
        {
            model.Annotations.Add(new TextAnnotation
            {
                Text = rows[index],
                TextPosition = new DataPoint(0, -1 - index),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
                TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle,
                Stroke = strokeColor,
                StrokeThickness = strokeThickness,
                Background = background,
                TextColor = textColor,
                FontSize = fontSize,
                Padding = new OxyThickness(4, 2, 4, 2)
            });
        }
    }

    private static double GetChartDataTableBorderThickness(ChartDataTableModel dataTable)
    {
        if (dataTable.BorderThickness is not { } thickness || !double.IsFinite(thickness))
            return 1;

        return Math.Clamp(thickness, 0, 20);
    }

    private static double GetChartDataTableFontSize(ChartDataTableModel dataTable)
    {
        if (dataTable.FontSize is not { } fontSize || !double.IsFinite(fontSize))
            return 9;

        return Math.Clamp(fontSize, 1, 96);
    }
}
