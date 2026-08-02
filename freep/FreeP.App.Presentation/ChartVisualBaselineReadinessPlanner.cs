using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum ChartVisualBaselineCaptureHost
{
    PowerPoint,
    Wpf,
    Avalonia,
}

public enum ChartVisualBaselineCaptureKind
{
    ChartSurface,
}

public sealed record ChartVisualBaselineCaptureRequest(
    string CaptureId,
    ChartVisualBaselineCaptureHost Host,
    ChartVisualBaselineCaptureKind Kind,
    int SlideIndex,
    int ChartIndex,
    ChartType ChartType,
    string ScenarioId,
    string SurfaceId,
    bool RequiresPowerPointCom,
    string EvidenceSummary);

public sealed record ChartVisualBaselineReadinessPlan(
    string ScenarioId,
    int SlideIndex,
    int ChartCount,
    IReadOnlyList<ChartVisualBaselineCaptureRequest> CaptureRequests,
    IReadOnlyList<string> EvidenceLines)
{
    public int PowerPointRequestCount => CaptureRequests.Count(request =>
        request.Host == ChartVisualBaselineCaptureHost.PowerPoint);

    public int SharedHostRequestCount => CaptureRequests.Count(request =>
        request.Host is ChartVisualBaselineCaptureHost.Wpf or ChartVisualBaselineCaptureHost.Avalonia);

    public bool IsPowerPointAuthoritativeReady =>
        ChartCount > 0
        && CaptureRequests.Any(request => request.Host == ChartVisualBaselineCaptureHost.PowerPoint)
        && CaptureRequests.Any(request => request.Host == ChartVisualBaselineCaptureHost.Wpf)
        && CaptureRequests.Any(request => request.Host == ChartVisualBaselineCaptureHost.Avalonia);
}

public static partial class ChartRenderPlanner
{
    public static ChartVisualBaselineReadinessPlan BuildVisualBaselineReadinessPlan(
        IReadOnlyList<ChartShape> charts,
        int slideIndex,
        string scenarioId = "chart-baseline")
    {
        ArgumentNullException.ThrowIfNull(charts);

        var safeScenarioId = NormalizeChartBaselineScenarioId(scenarioId);
        var safeSlideIndex = Math.Max(0, slideIndex);
        var requests = new List<ChartVisualBaselineCaptureRequest>(charts.Count * 3);

        for (int chartIndex = 0; chartIndex < charts.Count; chartIndex++)
        {
            var chart = charts[chartIndex];
            var chartTypeToken = NormalizeChartBaselineScenarioId(chart.ChartType.ToString());
            var surfaceId = BuildChartBaselineSurfaceId(
                safeScenarioId,
                safeSlideIndex,
                chartIndex,
                chartTypeToken);
            var summary = BuildChartBaselineEvidenceSummary(chart, chartIndex);

            AddChartBaselineHostRequests(
                requests,
                safeScenarioId,
                safeSlideIndex,
                chartIndex,
                chart.ChartType,
                surfaceId,
                summary);
        }

        var evidenceLines = new List<string>
        {
            $"Scenario {safeScenarioId}: slide {safeSlideIndex + 1}; charts {charts.Count}",
            $"Capture requests: {requests.Count}; PowerPoint {requests.Count(request => request.Host == ChartVisualBaselineCaptureHost.PowerPoint)}; WPF {requests.Count(request => request.Host == ChartVisualBaselineCaptureHost.Wpf)}; Avalonia {requests.Count(request => request.Host == ChartVisualBaselineCaptureHost.Avalonia)}",
            "PowerPoint requests are readiness contracts and require desktop PowerPoint COM on the baseline machine",
        };

        return new ChartVisualBaselineReadinessPlan(
            safeScenarioId,
            safeSlideIndex,
            charts.Count,
            requests,
            evidenceLines);
    }

    private static void AddChartBaselineHostRequests(
        List<ChartVisualBaselineCaptureRequest> requests,
        string scenarioId,
        int slideIndex,
        int chartIndex,
        ChartType chartType,
        string surfaceId,
        string evidenceSummary)
    {
        foreach (var host in new[]
        {
            ChartVisualBaselineCaptureHost.PowerPoint,
            ChartVisualBaselineCaptureHost.Wpf,
            ChartVisualBaselineCaptureHost.Avalonia,
        })
        {
            var hostToken = host.ToString().ToLowerInvariant();
            requests.Add(new ChartVisualBaselineCaptureRequest(
                $"{surfaceId}.{hostToken}",
                host,
                ChartVisualBaselineCaptureKind.ChartSurface,
                slideIndex,
                chartIndex,
                chartType,
                scenarioId,
                surfaceId,
                host == ChartVisualBaselineCaptureHost.PowerPoint,
                evidenceSummary));
        }
    }

    private static string BuildChartBaselineSurfaceId(
        string scenarioId,
        int slideIndex,
        int chartIndex,
        string chartType)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"freep.{scenarioId}.slide-{slideIndex + 1}.chart-{chartIndex + 1}.{chartType}");

    private static string BuildChartBaselineEvidenceSummary(ChartShape chart, int chartIndex)
    {
        var decision = chart.ChartType switch
        {
            ChartType.Stock => TryResolveStockVolumeSeries(chart) >= 0
                ? "stock volume columns plus high-low/open-close tick plan"
                : "stock high-low/open-close tick plan",
            ChartType.Surface3D => "3-D surface projected facet, wireframe, and contour plan",
            ChartType.Surface => "surface grid and contour plan",
            ChartType.Scatter when chart.ScatterStyle is ScatterStyle.Smooth or ScatterStyle.SmoothMarker =>
                "scatter smoothed Bezier path plan",
            ChartType.Scatter => "scatter line/marker point plan",
            ChartType.ColumnStacked100 or ChartType.BarStacked100 =>
                "100% stacked normalized axis and series extent plan",
            ChartType.ColumnClustered or ChartType.ColumnStacked or ChartType.BarClustered or ChartType.BarStacked =>
                "bar/column gap, overlap, and depth plan",
            ChartType.Pie when chart.ThreeDStyle == ChartThreeDStyle.Pie =>
                "3-D pie compressed top face and lower depth pass plan",
            ChartType.Pie or ChartType.OfPie => "pie-family first-slice and visible-point sweep plan",
            ChartType.Doughnut => "doughnut ring and first-slice plan",
            ChartType.Radar => chart.RadarStyle switch
            {
                RadarStyle.Filled => "filled radar area opacity, spoke-ring, and blank-point plan",
                RadarStyle.Marker => "radar marker, spoke-ring, and blank-point plan",
                _ => "standard radar spoke-ring and blank-point plan",
            },
            ChartType.Bubble => "bubble size representation and marker plan",
            _ => "shared chart frame, axis, legend, and series plan",
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{chart.ChartType} chart {chartIndex + 1}: {decision}; {chart.Series.Count} series; {chart.Categories.Count} categories");
    }

    private static string NormalizeChartBaselineScenarioId(string value)
    {
        var source = string.IsNullOrWhiteSpace(value)
            ? "chart-baseline"
            : value.Trim().ToLowerInvariant();
        var normalized = new string(source
            .Select(character => character is >= 'a' and <= 'z' or >= '0' and <= '9'
                ? character
                : '-')
            .ToArray())
            .Trim('-');

        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(normalized) ? "chart-baseline" : normalized;
    }
}
