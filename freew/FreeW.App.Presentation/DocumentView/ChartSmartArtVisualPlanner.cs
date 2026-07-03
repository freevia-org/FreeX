using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum ChartVisualGeometryKind
{
    Bars,
    Lines,
    Area,
    Pie,
    Doughnut,
    MarkerOnly
}

public sealed record ChartVisualPlan(
    ChartKind Kind,
    ChartVisualGeometryKind GeometryKind,
    bool ShowTitle,
    bool ShowLegend,
    bool ShowGridlines,
    bool PlotAreaFill,
    bool ShowMarkers,
    bool ShowDataLabels,
    bool ShowAxisTitles,
    string? CategoryAxisTitle,
    string? ValueAxisTitle,
    IReadOnlyList<string> PaletteHex);

public sealed record ChartElementCommandState(
    bool CanToggleLegend,
    bool IsLegendVisible,
    bool CanEditAxisTitles,
    bool HasChartTitle,
    bool HasAxisTitles);

public sealed record SmartArtNodeVisualPlan(
    string Text,
    int Depth,
    int ColorIndex,
    string FillHex,
    string TextHex);

public sealed record SmartArtVisualPlan(
    SmartArtKind Kind,
    string LayoutId,
    SmartArtLayoutPreset Layout,
    SmartArtColorScheme ColorScheme,
    SmartArtStyle Style,
    IReadOnlyList<SmartArtNodeVisualPlan> Nodes);

public static class ChartSmartArtVisualPlanner
{
    public static ChartVisualPlan BuildChartPlan(Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var schemeId = chart.ColorSchemeId?.Trim();
        var scheme = (!string.IsNullOrEmpty(schemeId) ? ChartColorScheme.FindById(schemeId) : null)
                     ?? ChartColorScheme.Default;
        var style = (chart.StyleId > 0 ? ChartStyle.FindById(chart.StyleId) : null)
                    ?? ChartStyle.Default;

        bool showTitle;
        bool showLegend;
        bool showAxisTitles;
        var showGridlines = style.ShowGridlines;
        var showDataLabels = style.ShowDataLabels;

        if (chart.QuickLayoutId > 0 && ChartQuickLayout.FindById(chart.QuickLayoutId) is { } quickLayout)
        {
            showTitle = quickLayout.ShowTitle && !string.IsNullOrEmpty(chart.Title);
            showLegend = quickLayout.ShowLegend && chart.Series.Count > 0;
            showGridlines = quickLayout.ShowGridlines;
            showDataLabels = quickLayout.ShowDataLabels;
            showAxisTitles = quickLayout.ShowAxisTitles;
        }
        else
        {
            showTitle = !string.IsNullOrEmpty(chart.Title);
            showLegend = chart.ShowLegend && chart.Series.Count > 0;
            showAxisTitles = !string.IsNullOrEmpty(chart.CategoryAxisTitle)
                          || !string.IsNullOrEmpty(chart.ValueAxisTitle);
        }

        var isPieFamily = chart.Kind is ChartKind.Pie or ChartKind.Doughnut;
        if (isPieFamily)
            showAxisTitles = false;

        return new ChartVisualPlan(
            chart.Kind,
            ToGeometryKind(chart.Kind),
            showTitle,
            showLegend,
            showGridlines,
            style.PlotAreaFill,
            style.ShowMarkers || chart.Kind == ChartKind.Scatter,
            showDataLabels,
            showAxisTitles,
            showAxisTitles ? chart.CategoryAxisTitle : null,
            showAxisTitles ? chart.ValueAxisTitle : null,
            scheme.Colors.Select(NormalizeHex).ToList());
    }

    public static ChartElementCommandState BuildChartElementCommandState(Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var plan = BuildChartPlan(chart);
        var isPieFamily = chart.Kind is ChartKind.Pie or ChartKind.Doughnut;

        return new ChartElementCommandState(
            CanToggleLegend: chart.Series.Count > 0,
            IsLegendVisible: plan.ShowLegend,
            CanEditAxisTitles: !isPieFamily,
            HasChartTitle: plan.ShowTitle,
            HasAxisTitles: plan.ShowAxisTitles);
    }

    public static SmartArtVisualPlan BuildSmartArtPlan(SmartArt smartArt)
    {
        ArgumentNullException.ThrowIfNull(smartArt);

        var layoutId = ResolveLayoutId(smartArt);
        var layout = SmartArtLayoutPreset.FindById(layoutId)
                     ?? SmartArtLayoutPreset.Default;
        var colorScheme = (!string.IsNullOrWhiteSpace(smartArt.ColorSchemeId)
                ? SmartArtColorScheme.FindById(smartArt.ColorSchemeId)
                : null)
            ?? SmartArtColorScheme.Default;
        var style = (!string.IsNullOrWhiteSpace(smartArt.StyleId)
                ? SmartArtStyle.FindById(smartArt.StyleId)
                : null)
            ?? SmartArtStyle.Default;

        var nodes = new List<SmartArtNodeVisualPlan>();
        FlattenNodes(smartArt.Nodes, depth: 0, nodes, colorScheme);

        return new SmartArtVisualPlan(
            smartArt.Kind,
            layout.Id,
            layout,
            colorScheme,
            style,
            nodes);
    }

    private static ChartVisualGeometryKind ToGeometryKind(ChartKind kind) =>
        kind switch
        {
            ChartKind.Bar or ChartKind.Column => ChartVisualGeometryKind.Bars,
            ChartKind.Line => ChartVisualGeometryKind.Lines,
            ChartKind.Area => ChartVisualGeometryKind.Area,
            ChartKind.Pie => ChartVisualGeometryKind.Pie,
            ChartKind.Doughnut => ChartVisualGeometryKind.Doughnut,
            ChartKind.Scatter => ChartVisualGeometryKind.MarkerOnly,
            _ => ChartVisualGeometryKind.Bars
        };

    private static string ResolveLayoutId(SmartArt smartArt) =>
        !string.IsNullOrWhiteSpace(smartArt.LayoutId)
            ? smartArt.LayoutId.Trim()
            : smartArt.Kind switch
            {
                SmartArtKind.Process => "process1",
                SmartArtKind.Hierarchy => "hierarchy1",
                _ => "list1"
            };

    private static void FlattenNodes(
        IEnumerable<SmartArtNode> nodes,
        int depth,
        List<SmartArtNodeVisualPlan> into,
        SmartArtColorScheme colorScheme)
    {
        foreach (var node in nodes)
        {
            var colorIndex = into.Count;
            into.Add(new SmartArtNodeVisualPlan(
                node.Text,
                depth,
                colorIndex,
                NormalizeHex(colorScheme.FillHexAt(colorIndex)),
                NormalizeHex(colorScheme.TextHex)));
            FlattenNodes(node.Children, depth + 1, into, colorScheme);
        }
    }

    private static string NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "#000000";

        var hex = value.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];
        if (hex.Length == 8)
            hex = hex[2..];
        if (hex.Length != 6)
            return "#000000";

        return int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out _)
            ? "#" + hex.ToUpperInvariant()
            : "#000000";
    }
}
