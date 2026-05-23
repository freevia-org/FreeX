using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class ChartOptionCycler
{
    public static ChartDataLabelPosition NextDataLabelPosition(ChartDataLabelPosition current) =>
        current switch
        {
            ChartDataLabelPosition.BestFit => ChartDataLabelPosition.OutsideEnd,
            ChartDataLabelPosition.OutsideEnd => ChartDataLabelPosition.InsideEnd,
            ChartDataLabelPosition.InsideEnd => ChartDataLabelPosition.Center,
            _ => ChartDataLabelPosition.BestFit
        };

    public static ChartDataLabelNumberFormat NextDataLabelNumberFormat(ChartDataLabelNumberFormat current) =>
        current switch
        {
            ChartDataLabelNumberFormat.General => ChartDataLabelNumberFormat.Number,
            ChartDataLabelNumberFormat.Number => ChartDataLabelNumberFormat.Currency,
            ChartDataLabelNumberFormat.Currency => ChartDataLabelNumberFormat.Percent,
            _ => ChartDataLabelNumberFormat.General
        };

    public static ChartTrendlineType NextTrendlineType(ChartTrendlineType current) =>
        current switch
        {
            ChartTrendlineType.Linear => ChartTrendlineType.Exponential,
            ChartTrendlineType.Exponential => ChartTrendlineType.Logarithmic,
            ChartTrendlineType.Logarithmic => ChartTrendlineType.Power,
            ChartTrendlineType.Power => ChartTrendlineType.MovingAverage,
            ChartTrendlineType.MovingAverage => ChartTrendlineType.Polynomial,
            _ => ChartTrendlineType.Linear
        };

    public static CellColor NextTrendlineColor(CellColor? current)
    {
        if (current is null)
            return new CellColor(217, 83, 25);
        if (current.Value.R == 217 && current.Value.G == 83 && current.Value.B == 25)
            return new CellColor(0, 114, 178);
        if (current.Value.R == 0 && current.Value.G == 114 && current.Value.B == 178)
            return new CellColor(0, 158, 115);
        return new CellColor(128, 128, 128);
    }

    public static (ChartAxisTickStyle Major, ChartAxisTickStyle Minor) NextAxisTickState(
        ChartAxisTickStyle currentMajor,
        ChartAxisTickStyle currentMinor)
    {
        if (currentMajor == ChartAxisTickStyle.Outside && currentMinor == ChartAxisTickStyle.None)
            return (ChartAxisTickStyle.Inside, ChartAxisTickStyle.None);
        if (currentMajor == ChartAxisTickStyle.Inside && currentMinor == ChartAxisTickStyle.None)
            return (ChartAxisTickStyle.Cross, ChartAxisTickStyle.Inside);
        if (currentMajor == ChartAxisTickStyle.Cross)
            return (ChartAxisTickStyle.None, ChartAxisTickStyle.None);
        return (ChartAxisTickStyle.Outside, ChartAxisTickStyle.None);
    }

    public static double NextAxisLabelAngle(double currentAngle)
    {
        if (Math.Abs(currentAngle) < 0.5)
            return -45;
        if (currentAngle <= -44.5)
            return 45;
        if (currentAngle < 89.5)
            return 90;
        return 0;
    }

    public static (CellColor Color, double Thickness) NextAxisLineState(CellColor? currentColor, double currentThickness)
    {
        if (currentColor is null || currentThickness < 1.5)
            return (new CellColor(89, 89, 89), 1.5);
        if (currentThickness < 2.5)
            return (new CellColor(0, 114, 178), 2.5);
        if (currentThickness < 3.5)
            return (new CellColor(213, 94, 0), 3.5);
        return (new CellColor(89, 89, 89), 1);
    }

    public static (bool ShowMajor, bool ShowMinor) NextGridlineState(bool currentMajor, bool currentMinor)
    {
        if (!currentMajor)
            return (true, false);
        if (!currentMinor)
            return (true, true);
        return (false, false);
    }

    public static CellColor NextSeriesColor(CellColor? current)
    {
        if (current is null)
            return new CellColor(0, 114, 178);
        if (current.Value.R == 0 && current.Value.G == 114 && current.Value.B == 178)
            return new CellColor(213, 94, 0);
        if (current.Value.R == 213 && current.Value.G == 94 && current.Value.B == 0)
            return new CellColor(0, 158, 115);
        return new CellColor(0, 114, 178);
    }

    public static ChartType ParseChartType(string type)
    {
        var normalized = type.Trim().ToLowerInvariant();
        return normalized switch
        {
            "line" => ChartType.Line,
            "3d line" or "three d line" or "three-dimensional line" => ChartType.ThreeDLine,
            "pie" => ChartType.Pie,
            "3d pie" or "three d pie" or "three-dimensional pie" => ChartType.ThreeDPie,
            "doughnut" or "donut" => ChartType.Doughnut,
            "bar" => ChartType.Bar,
            "stackedbar" or "stacked bar" => ChartType.StackedBar,
            "percentstackedbar" or "100% stacked bar" or "100%stackedbar" => ChartType.PercentStackedBar,
            "stackedcolumn" or "stacked column" => ChartType.StackedColumn,
            "percentstackedcolumn" or "100% stacked column" or "100%stackedcolumn" => ChartType.PercentStackedColumn,
            "scatter" => ChartType.Scatter,
            "bubble" => ChartType.Bubble,
            "area" => ChartType.Area,
            "3d area" or "three d area" or "three-dimensional area" => ChartType.ThreeDArea,
            "radar" => ChartType.Radar,
            "stock" => ChartType.Stock,
            _ => ChartType.Column
        };
    }
    public static bool TryGetAxisBounds(Sheet sheet, ChartModel chart, bool useXAxis, out double minimum, out double maximum)
    {
        minimum = 0;
        maximum = 0;
        var values = new List<double>();
        var startRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        if (startRow > chart.DataRange.End.Row)
            return false;

        if (useXAxis)
        {
            var xColumns = ChartTypeSupport.GetXAxisValueColumns(chart);
            foreach (var xColumn in xColumns)
            {
                for (var row = startRow; row <= chart.DataRange.End.Row; row++)
                {
                    if (sheet.GetValue(row, xColumn) is NumberValue number)
                        values.Add(number.Value);
                }
            }
        }
        else
        {
            var yColumns = ChartTypeSupport.GetYAxisValueColumns(chart);
            for (var row = startRow; row <= chart.DataRange.End.Row; row++)
            {
                foreach (var col in yColumns)
                {
                    if (sheet.GetValue(row, col) is NumberValue number)
                        values.Add(number.Value);
                }
            }
        }

        if (values.Count == 0)
            return false;

        minimum = values.Min();
        maximum = values.Max();
        if (Math.Abs(maximum - minimum) < double.Epsilon)
        {
            minimum -= 1;
            maximum += 1;
        }

        return true;
    }

    public static int GetSeriesCount(ChartModel chart) => ChartTypeSupport.GetDataSeriesCount(chart);

    public static (bool ShowSecondaryAxis, int[] SeriesIndexes) GetNextSecondaryAxisSeries(ChartModel chart, int seriesCount)
    {
        if (!chart.ShowSecondaryAxis)
            return (true, [1]);

        if (chart.SecondaryAxisSeriesIndexes.Count == 0)
            return (false, []);

        var current = chart.SecondaryAxisSeriesIndexes.Min();
        if (current + 1 < seriesCount)
            return (true, [current + 1]);

        return (true, []);
    }

    public static int[] GetNextComboLineSeries(ChartModel chart, int seriesCount)
    {
        if (!chart.UseComboLineForSecondarySeries || chart.ComboLineSeriesIndexes.Count == 0)
            return [1];

        var current = chart.ComboLineSeriesIndexes.Min();
        if (current + 1 < seriesCount)
            return [current + 1];

        return [];
    }
}
