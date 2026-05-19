using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class SparklineValuePlanner
{
    public static IReadOnlyDictionary<Guid, IReadOnlyList<double>> BuildValues(Sheet sheet)
    {
        var values = new Dictionary<Guid, IReadOnlyList<double>>();
        foreach (var sparkline in sheet.Sparklines)
        {
            var series = new List<double>();
            for (var row = sparkline.DataRange.Start.Row; row <= sparkline.DataRange.End.Row; row++)
            {
                for (var col = sparkline.DataRange.Start.Col; col <= sparkline.DataRange.End.Col; col++)
                {
                    switch (sheet.GetValue(row, col))
                    {
                        case NumberValue number:
                            series.Add(number.Value);
                            break;
                        case DateTimeValue date:
                            series.Add(date.Value);
                            break;
                        case BoolValue boolean:
                            series.Add(boolean.Value ? 1 : 0);
                            break;
                    }
                }
            }

            values[sparkline.Id] = series;
        }

        return values;
    }
}
