using FreeX.Core.Model;

namespace FreeX.App.Host;

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
                if (sheet.IsRowEffectivelyHidden(row))
                    continue;

                for (var col = sparkline.DataRange.Start.Col; col <= sparkline.DataRange.End.Col; col++)
                {
                    if (sheet.IsColEffectivelyHidden(col))
                        continue;

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
