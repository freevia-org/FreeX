using FreeX.Core.Model;

namespace FreeX.App.Presentation.Rendering;

public static class ViewportMetricEndpointLookup
{
    public static bool TryFindRows(
        IReadOnlyList<RowMetric> metrics,
        uint firstRow,
        uint lastRow,
        out RowMetric firstMetric,
        out RowMetric lastMetric) =>
        TryFind(
            metrics,
            firstRow,
            lastRow,
            static metric => metric.Row,
            out firstMetric,
            out lastMetric);

    public static bool TryFindColumns(
        IReadOnlyList<ColMetric> metrics,
        uint firstColumn,
        uint lastColumn,
        out ColMetric firstMetric,
        out ColMetric lastMetric) =>
        TryFind(
            metrics,
            firstColumn,
            lastColumn,
            static metric => metric.Col,
            out firstMetric,
            out lastMetric);

    private static bool TryFind<TMetric>(
        IReadOnlyList<TMetric> metrics,
        uint firstIndex,
        uint lastIndex,
        Func<TMetric, uint> indexSelector,
        out TMetric firstMetric,
        out TMetric lastMetric)
        where TMetric : class
    {
        TMetric? foundFirst = null;
        TMetric? foundLast = null;

        foreach (var metric in metrics)
        {
            var index = indexSelector(metric);
            if (index > lastIndex)
                break;

            if (foundFirst is null && index == firstIndex)
                foundFirst = metric;

            if (foundLast is null && index == lastIndex)
                foundLast = metric;

            if (foundFirst is not null && foundLast is not null)
            {
                firstMetric = foundFirst;
                lastMetric = foundLast;
                return true;
            }
        }

        firstMetric = null!;
        lastMetric = null!;
        return false;
    }
}
