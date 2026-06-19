using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// Thin host-facing shim over the shared <see cref="SparklineSeriesReader"/>. Kept so existing
/// callers (e.g. <c>MainWindow.Viewport</c>) need no change while the sheet -&gt; series logic lives
/// single-sourced in the portable presentation layer.
/// </summary>
public static class SparklineValuePlanner
{
    public static IReadOnlyDictionary<Guid, IReadOnlyList<double>> BuildValues(Sheet sheet) =>
        SparklineSeriesReader.BuildValues(sheet);
}
