using System.Globalization;
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
    int StyleId,
    string ColorSchemeId,
    int QuickLayoutId,
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
    string TextHex,
    string BorderHex,
    double BorderThickness,
    double CornerRadius,
    double ShadowOpacity,
    double ShadowBlur,
    double ShadowDepth,
    string ConnectorHex);

public sealed record SmartArtHierarchyNodeGeometry(
    int NodeIndex,
    int? ParentNodeIndex,
    int Depth,
    double X,
    double Y,
    double Width,
    double Height);

public sealed record SmartArtHierarchyConnectorGeometry(
    int ParentNodeIndex,
    int ChildNodeIndex,
    double X1,
    double Y1,
    double X2,
    double Y2);

public sealed record SmartArtHierarchyGeometryPlan(
    IReadOnlyList<SmartArtHierarchyNodeGeometry> Nodes,
    IReadOnlyList<SmartArtHierarchyConnectorGeometry> Connectors,
    int MaxDepth,
    double NaturalWidth,
    double NaturalHeight);

public enum SmartArtLayoutGeometryKind
{
    BasicList,
    VerticalBulletList,
    HorizontalList,
    BasicProcess,
    ContinuousBlockProcess,
    StepUp,
    StepDown,
    Cycle,
    Pyramid,
    Radial,
    Matrix
}

public enum SmartArtLayoutConnectorKind
{
    Line,
    Arrow
}

public sealed record SmartArtLayoutPoint(
    double X,
    double Y);

public sealed record SmartArtLayoutNodeGeometry(
    int NodeIndex,
    double X,
    double Y,
    double Width,
    double Height,
    IReadOnlyList<SmartArtLayoutPoint> PolygonPoints)
{
    public SmartArtLayoutNodeGeometry(
        int NodeIndex,
        double X,
        double Y,
        double Width,
        double Height)
        : this(NodeIndex, X, Y, Width, Height, [])
    {
    }

    public bool HasPolygon => PolygonPoints.Count > 0;
}


public sealed record SmartArtLayoutConnectorGeometry(
    int SourceNodeIndex,
    int TargetNodeIndex,
    SmartArtLayoutConnectorKind Kind,
    double X1,
    double Y1,
    double X2,
    double Y2);

public sealed record SmartArtLayoutGeometryPlan(
    SmartArtLayoutGeometryKind Kind,
    IReadOnlyList<SmartArtLayoutNodeGeometry> Nodes,
    IReadOnlyList<SmartArtLayoutConnectorGeometry> Connectors,
    double NaturalWidth,
    double NaturalHeight);

public sealed record SmartArtVisualPlan(
    SmartArtKind Kind,
    string LayoutId,
    SmartArtLayoutPreset Layout,
    SmartArtColorScheme ColorScheme,
    SmartArtStyle Style,
    IReadOnlyList<SmartArtNodeVisualPlan> Nodes,
    SmartArtHierarchyGeometryPlan? HierarchyGeometry = null,
    SmartArtLayoutGeometryPlan? LayoutGeometry = null);

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

        var quickLayout = chart.QuickLayoutId > 0 ? ChartQuickLayout.FindById(chart.QuickLayoutId) : null;
        if (quickLayout is not null)
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
            style.Id,
            scheme.Id,
            quickLayout?.Id ?? 0,
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

    public static IReadOnlyList<string> BuildChartVisualSignatures(IEnumerable<ChartVisualPlan> charts)
    {
        ArgumentNullException.ThrowIfNull(charts);

        return charts
            .Select(BuildChartVisualSignature)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();
    }

    public static string BuildChartVisualSignature(ChartVisualPlan chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return string.Join(
            "|",
            "kind=" + chart.Kind,
            "geometry=" + chart.GeometryKind,
            "style=" + chart.StyleId.ToString(CultureInfo.InvariantCulture),
            "colorScheme=" + NormalizeSignatureText(chart.ColorSchemeId),
            "quickLayout=" + chart.QuickLayoutId.ToString(CultureInfo.InvariantCulture),
            "title=" + BoolFlag(chart.ShowTitle),
            "legend=" + BoolFlag(chart.ShowLegend),
            "gridlines=" + BoolFlag(chart.ShowGridlines),
            "plotFill=" + BoolFlag(chart.PlotAreaFill),
            "markers=" + BoolFlag(chart.ShowMarkers),
            "dataLabels=" + BoolFlag(chart.ShowDataLabels),
            "axisTitles=" + BoolFlag(chart.ShowAxisTitles),
            "categoryAxis=" + NormalizeSignatureText(chart.CategoryAxisTitle),
            "valueAxis=" + NormalizeSignatureText(chart.ValueAxisTitle),
            "palette=" + string.Join(",", chart.PaletteHex));
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
        FlattenNodes(smartArt.Nodes, depth: 0, nodes, colorScheme, style);

        var hierarchyGeometry = layout.Kind == SmartArtKind.Hierarchy
            ? BuildHierarchyGeometry(smartArt.Nodes)
            : null;
        var layoutGeometry = hierarchyGeometry is null
            ? BuildLayoutGeometry(layout.Id, nodes.Count)
            : null;

        return new SmartArtVisualPlan(
            layout.Kind,
            layout.Id,
            layout,
            colorScheme,
            style,
            nodes,
            hierarchyGeometry,
            layoutGeometry);
    }

    public static IReadOnlyList<string> BuildSmartArtVisualSignatures(IEnumerable<SmartArtVisualPlan> smartArts)
    {
        ArgumentNullException.ThrowIfNull(smartArts);

        return smartArts
            .Select(BuildSmartArtVisualSignature)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();
    }

    public static string BuildSmartArtVisualSignature(SmartArtVisualPlan smartArt)
    {
        ArgumentNullException.ThrowIfNull(smartArt);

        return string.Join(
            "|",
            "kind=" + smartArt.Kind,
            "layout=" + NormalizeSignatureText(smartArt.LayoutId),
            "preset=" + NormalizeSignatureText(smartArt.Layout.Id),
            "colorScheme=" + NormalizeSignatureText(smartArt.ColorScheme.Id),
            "style=" + NormalizeSignatureText(smartArt.Style.Id),
            "hierarchy=" + BuildSmartArtHierarchyVisualSignature(smartArt.HierarchyGeometry),
            "geometry=" + BuildSmartArtLayoutGeometryVisualSignature(smartArt.LayoutGeometry),
            "nodes=" + string.Join(";", smartArt.Nodes.Select(BuildSmartArtNodeVisualSignature)));
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
        SmartArtColorScheme colorScheme,
        SmartArtStyle style)
    {
        foreach (var node in nodes)
        {
            var colorIndex = into.Count;
            var fillHex = AdjustBrightness(NormalizeHex(colorScheme.FillHexAt(colorIndex)), style.BrightnessAdjust);
            into.Add(new SmartArtNodeVisualPlan(
                node.Text,
                depth,
                colorIndex,
                fillHex,
                NormalizeHex(colorScheme.TextHex),
                AdjustBrightness(fillHex, -0.18),
                Math.Max(0, style.BorderThickness),
                Math.Max(0, style.CornerRadius),
                Math.Clamp(style.ShadowOpacity, 0, 1),
                style.ShadowOpacity > 0 ? 4 + style.ShadowOpacity * 8 : 0,
                style.ShadowOpacity > 0 ? 1.5 + style.ShadowOpacity * 2 : 0,
                ConnectorContrast(fillHex)));
            FlattenNodes(node.Children, depth + 1, into, colorScheme, style);
        }
    }

    private static SmartArtHierarchyGeometryPlan BuildHierarchyGeometry(IReadOnlyList<SmartArtNode> roots)
    {
        const double margin = 8;
        const double nodeWidth = 112;
        const double nodeHeight = 30;
        const double horizontalSpacing = 22;
        const double verticalSpacing = 34;

        var boxes = new List<SmartArtHierarchyNodeGeometry>();
        var connectors = new List<SmartArtHierarchyConnectorGeometry>();
        var leafIndex = 0;
        var maxDepth = 0;

        (int Index, double CenterX) LayoutNode(SmartArtNode node, int? parentIndex, int depth)
        {
            var nodeIndex = boxes.Count;
            boxes.Add(new SmartArtHierarchyNodeGeometry(nodeIndex, parentIndex, depth, 0, 0, nodeWidth, nodeHeight));
            maxDepth = Math.Max(maxDepth, depth);

            double centerX;
            var childResults = new List<(int Index, double CenterX)>();
            foreach (var child in node.Children)
                childResults.Add(LayoutNode(child, nodeIndex, depth + 1));

            if (childResults.Count == 0)
            {
                centerX = margin + nodeWidth / 2 + leafIndex * (nodeWidth + horizontalSpacing);
                leafIndex++;
            }
            else
            {
                centerX = (childResults[0].CenterX + childResults[^1].CenterX) / 2;
            }

            var x = centerX - nodeWidth / 2;
            var y = margin + depth * (nodeHeight + verticalSpacing);
            boxes[nodeIndex] = new SmartArtHierarchyNodeGeometry(
                nodeIndex,
                parentIndex,
                depth,
                x,
                y,
                nodeWidth,
                nodeHeight);

            foreach (var child in childResults)
            {
                var childBox = boxes[child.Index];
                connectors.Add(new SmartArtHierarchyConnectorGeometry(
                    nodeIndex,
                    child.Index,
                    x + nodeWidth / 2,
                    y + nodeHeight,
                    childBox.X + childBox.Width / 2,
                    childBox.Y));
            }

            return (nodeIndex, centerX);
        }

        foreach (var root in roots)
            LayoutNode(root, parentIndex: null, depth: 0);

        if (boxes.Count == 0)
            return new SmartArtHierarchyGeometryPlan([], [], 0, 0, 0);

        var naturalWidth = boxes.Max(box => box.X + box.Width) + margin;
        var naturalHeight = boxes.Max(box => box.Y + box.Height) + margin;
        return new SmartArtHierarchyGeometryPlan(
            boxes,
            connectors,
            maxDepth,
            naturalWidth,
            naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan? BuildLayoutGeometry(string layoutId, int nodeCount) =>
        layoutId switch
        {
            "list1" => BuildVerticalListGeometry(nodeCount, SmartArtLayoutGeometryKind.BasicList),
            "vertbullet1" => BuildVerticalListGeometry(nodeCount, SmartArtLayoutGeometryKind.VerticalBulletList),
            "horizbullet1" => BuildHorizontalListGeometry(nodeCount),
            "process1" => BuildBasicProcessGeometry(nodeCount),
            "continuousBlockProcess" => BuildContinuousBlockProcessGeometry(nodeCount),
            "stepup1" => BuildStepGeometry(nodeCount, ascending: true),
            "stepdown1" => BuildStepGeometry(nodeCount, ascending: false),
            "cycle1" => BuildCycleGeometry(nodeCount),
            "pyramid1" => BuildPyramidGeometry(nodeCount),
            "radial1" => BuildRadialGeometry(nodeCount),
            "matrix1" => BuildMatrixGeometry(nodeCount),
            _ => null
        };

    private static SmartArtLayoutGeometryPlan BuildVerticalListGeometry(
        int nodeCount,
        SmartArtLayoutGeometryKind kind)
    {
        const double margin = 8;
        const double boxWidth = 112;
        const double boxHeight = 30;
        const double gap = 6;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                margin,
                margin + i * (boxHeight + gap),
                boxWidth,
                boxHeight));
        }

        var naturalWidth = nodeCount == 0 ? 0 : margin * 2 + boxWidth;
        var naturalHeight = nodeCount == 0
            ? 0
            : margin * 2 + nodeCount * boxHeight + Math.Max(0, nodeCount - 1) * gap;
        return new SmartArtLayoutGeometryPlan(
            kind,
            nodes,
            [],
            naturalWidth,
            naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan BuildHorizontalListGeometry(int nodeCount)
    {
        const double margin = 8;
        const double boxWidth = 70;
        const double boxHeight = 30;
        const double gap = 8;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                margin + i * (boxWidth + gap),
                margin,
                boxWidth,
                boxHeight));
        }

        var naturalWidth = nodeCount == 0
            ? 0
            : margin * 2 + nodeCount * boxWidth + Math.Max(0, nodeCount - 1) * gap;
        var naturalHeight = nodeCount == 0 ? 0 : margin * 2 + boxHeight;
        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.HorizontalList,
            nodes,
            [],
            naturalWidth,
            naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan BuildBasicProcessGeometry(int nodeCount)
    {
        const double margin = 8;
        const double boxWidth = 70;
        const double boxHeight = 30;
        const double gap = 16;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                margin + i * (boxWidth + gap),
                margin,
                boxWidth,
                boxHeight));
        }

        var connectors = new List<SmartArtLayoutConnectorGeometry>(Math.Max(0, nodeCount - 1));
        for (var i = 0; i < nodeCount - 1; i++)
        {
            var current = nodes[i];
            var next = nodes[i + 1];
            connectors.Add(new SmartArtLayoutConnectorGeometry(
                i,
                i + 1,
                SmartArtLayoutConnectorKind.Arrow,
                current.X + current.Width,
                current.Y + current.Height / 2,
                next.X,
                next.Y + next.Height / 2));
        }

        var naturalWidth = nodeCount == 0
            ? 0
            : margin * 2 + nodeCount * boxWidth + Math.Max(0, nodeCount - 1) * gap;
        var naturalHeight = nodeCount == 0 ? 0 : margin * 2 + boxHeight;
        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.BasicProcess,
            nodes,
            connectors,
            naturalWidth,
            naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan BuildContinuousBlockProcessGeometry(int nodeCount)
    {
        const double margin = 8;
        const double boxWidth = 76;
        const double boxHeight = 34;
        const double gap = 4;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                margin + i * (boxWidth + gap),
                margin,
                boxWidth,
                boxHeight));
        }

        var connectors = new List<SmartArtLayoutConnectorGeometry>(Math.Max(0, nodeCount - 1));
        for (var i = 0; i < nodeCount - 1; i++)
        {
            var current = nodes[i];
            var next = nodes[i + 1];
            connectors.Add(new SmartArtLayoutConnectorGeometry(
                i,
                i + 1,
                SmartArtLayoutConnectorKind.Arrow,
                current.X + current.Width,
                current.Y + current.Height / 2,
                next.X,
                next.Y + next.Height / 2));
        }

        var naturalWidth = nodeCount == 0
            ? 0
            : margin * 2 + nodeCount * boxWidth + Math.Max(0, nodeCount - 1) * gap;
        var naturalHeight = nodeCount == 0 ? 0 : margin * 2 + boxHeight;
        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.ContinuousBlockProcess,
            nodes,
            connectors,
            naturalWidth,
            naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan BuildStepGeometry(int nodeCount, bool ascending)
    {
        const double margin = 8;
        const double boxWidth = 70;
        const double boxHeight = 30;
        const double stepX = 60;
        const double stepY = 28;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                margin + i * stepX,
                margin + (ascending ? nodeCount - 1 - i : i) * stepY,
                boxWidth,
                boxHeight));
        }

        var connectors = new List<SmartArtLayoutConnectorGeometry>(Math.Max(0, nodeCount - 1));
        for (var i = 0; i < nodeCount - 1; i++)
        {
            var current = nodes[i];
            var next = nodes[i + 1];
            connectors.Add(new SmartArtLayoutConnectorGeometry(
                i,
                i + 1,
                SmartArtLayoutConnectorKind.Arrow,
                current.X + current.Width,
                current.Y + current.Height / 2,
                next.X,
                next.Y + next.Height / 2));
        }

        var naturalWidth = nodeCount == 0 ? 0 : margin * 2 + boxWidth + (nodeCount - 1) * stepX;
        var naturalHeight = nodeCount == 0 ? 0 : margin * 2 + boxHeight + (nodeCount - 1) * stepY;
        return new SmartArtLayoutGeometryPlan(
            ascending ? SmartArtLayoutGeometryKind.StepUp : SmartArtLayoutGeometryKind.StepDown,
            nodes,
            connectors,
            naturalWidth,
            naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan BuildCycleGeometry(int nodeCount)
    {
        const double naturalWidth = 200;
        const double naturalHeight = 160;
        const double centerX = naturalWidth / 2;
        const double centerY = naturalHeight / 2;
        const double radiusX = 72;
        const double radiusY = 56;
        const double boxWidth = 52;
        const double boxHeight = 26;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            var angle = 2 * Math.PI * i / nodeCount - Math.PI / 2;
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                centerX + radiusX * Math.Cos(angle) - boxWidth / 2,
                centerY + radiusY * Math.Sin(angle) - boxHeight / 2,
                boxWidth,
                boxHeight));
        }

        var connectors = new List<SmartArtLayoutConnectorGeometry>(nodeCount);
        if (nodeCount > 1)
        {
            for (var i = 0; i < nodeCount; i++)
            {
                var current = nodes[i];
                var next = nodes[(i + 1) % nodeCount];
                connectors.Add(new SmartArtLayoutConnectorGeometry(
                    i,
                    (i + 1) % nodeCount,
                    SmartArtLayoutConnectorKind.Arrow,
                    current.X + current.Width / 2,
                    current.Y + current.Height / 2,
                    next.X + next.Width / 2,
                    next.Y + next.Height / 2));
            }
        }

        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.Cycle,
            nodes,
            connectors,
            nodeCount == 0 ? 0 : naturalWidth,
            nodeCount == 0 ? 0 : naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan BuildRadialGeometry(int nodeCount)
    {
        const double naturalWidth = 220;
        const double naturalHeight = 180;
        const double centerX = naturalWidth / 2;
        const double centerY = naturalHeight / 2;
        const double centerWidth = 56;
        const double centerHeight = 36;
        const double radiusX = 76;
        const double radiusY = 58;
        const double satelliteWidth = 48;
        const double satelliteHeight = 24;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        if (nodeCount > 0)
        {
            nodes.Add(new SmartArtLayoutNodeGeometry(
                0,
                centerX - centerWidth / 2,
                centerY - centerHeight / 2,
                centerWidth,
                centerHeight));
        }

        var satellites = Math.Max(0, nodeCount - 1);
        for (var i = 0; i < satellites; i++)
        {
            var angle = 2 * Math.PI * i / satellites - Math.PI / 2;
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i + 1,
                centerX + radiusX * Math.Cos(angle) - satelliteWidth / 2,
                centerY + radiusY * Math.Sin(angle) - satelliteHeight / 2,
                satelliteWidth,
                satelliteHeight));
        }

        var connectors = new List<SmartArtLayoutConnectorGeometry>(satellites);
        for (var i = 1; i < nodes.Count; i++)
        {
            var satellite = nodes[i];
            connectors.Add(new SmartArtLayoutConnectorGeometry(
                0,
                i,
                SmartArtLayoutConnectorKind.Line,
                centerX,
                centerY,
                satellite.X + satellite.Width / 2,
                satellite.Y + satellite.Height / 2));
        }

        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.Radial,
            nodes,
            connectors,
            nodeCount == 0 ? 0 : naturalWidth,
            nodeCount == 0 ? 0 : naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan BuildPyramidGeometry(int nodeCount)
    {
        // Shared Basic Pyramid approximation: centered text bounds plus renderer-neutral band polygons.
        const double margin = 8;
        const double minBandWidth = 54;
        const double maxBandWidth = 160;
        const double bandHeight = 30;
        const double gap = 4;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        var widthRange = Math.Max(0, maxBandWidth - minBandWidth);
        var divisor = Math.Max(1, nodeCount - 1);

        for (var i = 0; i < nodeCount; i++)
        {
            var width = nodeCount == 1
                ? maxBandWidth
                : minBandWidth + widthRange * i / divisor;
            var topWidth = nodeCount == 1
                ? maxBandWidth
                : minBandWidth + widthRange * i / nodeCount;
            var bottomWidth = nodeCount == 1
                ? maxBandWidth
                : minBandWidth + widthRange * (i + 1) / nodeCount;
            var y = margin + i * (bandHeight + gap);
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                margin + (maxBandWidth - width) / 2,
                y,
                width,
                bandHeight,
                BuildCenteredBandPolygon(margin, maxBandWidth, topWidth, bottomWidth, y, bandHeight)));
        }

        var naturalWidth = nodeCount == 0 ? 0 : margin * 2 + maxBandWidth;
        var naturalHeight = nodeCount == 0
            ? 0
            : margin * 2 + nodeCount * bandHeight + Math.Max(0, nodeCount - 1) * gap;
        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.Pyramid,
            nodes,
            [],
            naturalWidth,
            naturalHeight);
    }

    private static IReadOnlyList<SmartArtLayoutPoint> BuildCenteredBandPolygon(
        double margin,
        double maxWidth,
        double topWidth,
        double bottomWidth,
        double y,
        double height)
    {
        var topLeft = margin + (maxWidth - topWidth) / 2;
        var bottomLeft = margin + (maxWidth - bottomWidth) / 2;
        return
        [
            new SmartArtLayoutPoint(topLeft, y),
            new SmartArtLayoutPoint(topLeft + topWidth, y),
            new SmartArtLayoutPoint(bottomLeft + bottomWidth, y + height),
            new SmartArtLayoutPoint(bottomLeft, y + height)
        ];
    }

    private static SmartArtLayoutGeometryPlan BuildMatrixGeometry(int nodeCount)
    {
        const double margin = 8;
        const double boxWidth = 78;
        const double boxHeight = 34;
        const double gap = 10;

        var columns = nodeCount <= 4 ? 2 : (int)Math.Ceiling(Math.Sqrt(nodeCount));
        columns = Math.Max(1, columns);
        var rows = nodeCount == 0 ? 0 : (int)Math.Ceiling(nodeCount / (double)columns);

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            var column = i % columns;
            var row = i / columns;
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                margin + column * (boxWidth + gap),
                margin + row * (boxHeight + gap),
                boxWidth,
                boxHeight));
        }

        var naturalWidth = nodeCount == 0 ? 0 : margin * 2 + columns * boxWidth + (columns - 1) * gap;
        var naturalHeight = nodeCount == 0 ? 0 : margin * 2 + rows * boxHeight + Math.Max(0, rows - 1) * gap;
        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.Matrix,
            nodes,
            [],
            naturalWidth,
            naturalHeight);
    }

    private static string ConnectorContrast(string fillHex)
    {
        var (r, g, b) = ParseRgb(fillHex);
        var luminance = (r * 0.299 + g * 0.587 + b * 0.114) / 255.0;
        return AdjustBrightness(fillHex, luminance < 0.25 ? 0.30 : -0.30);
    }

    private static string AdjustBrightness(string hex, double delta)
    {
        if (delta == 0)
            return NormalizeHex(hex);

        var (r, g, b) = ParseRgb(hex);
        var offset = delta * 255;
        return ToHex(Clamp(r + offset), Clamp(g + offset), Clamp(b + offset));
    }

    private static (byte R, byte G, byte B) ParseRgb(string hex)
    {
        var normalized = NormalizeHex(hex);
        return (
            Convert.ToByte(normalized.Substring(1, 2), 16),
            Convert.ToByte(normalized.Substring(3, 2), 16),
            Convert.ToByte(normalized.Substring(5, 2), 16));
    }

    private static byte Clamp(double value) =>
        (byte)Math.Max(0, Math.Min(255, value));

    private static string ToHex(byte r, byte g, byte b) =>
        $"#{r:X2}{g:X2}{b:X2}";

    private static string BuildSmartArtNodeVisualSignature(SmartArtNodeVisualPlan node) =>
        string.Join(
            ":",
            NormalizeSignatureText(node.Text),
            node.Depth.ToString(CultureInfo.InvariantCulture),
            node.ColorIndex.ToString(CultureInfo.InvariantCulture),
            node.FillHex,
            node.TextHex,
            node.BorderHex,
            FormatSignatureDouble(node.BorderThickness),
            FormatSignatureDouble(node.CornerRadius),
            FormatSignatureDouble(node.ShadowOpacity),
            FormatSignatureDouble(node.ShadowBlur),
            FormatSignatureDouble(node.ShadowDepth),
            node.ConnectorHex);

    private static string BuildSmartArtHierarchyVisualSignature(SmartArtHierarchyGeometryPlan? geometry)
    {
        if (geometry is null)
            return "none";

        var nodeSignature = string.Join(
            ",",
            geometry.Nodes.Select(node => string.Join(
                ":",
                node.NodeIndex.ToString(CultureInfo.InvariantCulture),
                node.ParentNodeIndex?.ToString(CultureInfo.InvariantCulture) ?? "root",
                node.Depth.ToString(CultureInfo.InvariantCulture),
                FormatSignatureDouble(node.X),
                FormatSignatureDouble(node.Y),
                FormatSignatureDouble(node.Width),
                FormatSignatureDouble(node.Height))));

        var connectorSignature = string.Join(
            ",",
            geometry.Connectors.Select(connector => string.Join(
                ":",
                connector.ParentNodeIndex.ToString(CultureInfo.InvariantCulture),
                connector.ChildNodeIndex.ToString(CultureInfo.InvariantCulture),
                FormatSignatureDouble(connector.X1),
                FormatSignatureDouble(connector.Y1),
                FormatSignatureDouble(connector.X2),
                FormatSignatureDouble(connector.Y2))));

        return string.Join(
            "/",
            "maxDepth=" + geometry.MaxDepth.ToString(CultureInfo.InvariantCulture),
            "nodes=" + geometry.Nodes.Count.ToString(CultureInfo.InvariantCulture),
            "connectors=" + geometry.Connectors.Count.ToString(CultureInfo.InvariantCulture),
            "size=" + FormatSignatureDouble(geometry.NaturalWidth) + "x" + FormatSignatureDouble(geometry.NaturalHeight),
            "boxes=" + nodeSignature,
            "lines=" + connectorSignature);
    }

    private static string BuildSmartArtLayoutGeometryVisualSignature(SmartArtLayoutGeometryPlan? geometry)
    {
        if (geometry is null)
            return "none";

        var nodeSignature = string.Join(
            ",",
            geometry.Nodes.Select(node => string.Join(
                ":",
                node.NodeIndex.ToString(CultureInfo.InvariantCulture),
                FormatSignatureDouble(node.X),
                FormatSignatureDouble(node.Y),
                FormatSignatureDouble(node.Width),
                FormatSignatureDouble(node.Height))));

        var polygonSignature = string.Join(
            ",",
            geometry.Nodes
                .Where(node => node.HasPolygon)
                .Select(node => node.NodeIndex.ToString(CultureInfo.InvariantCulture)
                    + "="
                    + string.Join(
                        ";",
                        node.PolygonPoints.Select(point =>
                            FormatSignatureDouble(point.X) + ":" + FormatSignatureDouble(point.Y)))));

        var connectorSignature = string.Join(
            ",",
            geometry.Connectors.Select(connector => string.Join(
                ":",
                connector.SourceNodeIndex.ToString(CultureInfo.InvariantCulture),
                connector.TargetNodeIndex.ToString(CultureInfo.InvariantCulture),
                connector.Kind,
                FormatSignatureDouble(connector.X1),
                FormatSignatureDouble(connector.Y1),
                FormatSignatureDouble(connector.X2),
                FormatSignatureDouble(connector.Y2))));

        return string.Join(
            "/",
            "kind=" + geometry.Kind,
            "nodes=" + geometry.Nodes.Count.ToString(CultureInfo.InvariantCulture),
            "connectors=" + geometry.Connectors.Count.ToString(CultureInfo.InvariantCulture),
            "size=" + FormatSignatureDouble(geometry.NaturalWidth) + "x" + FormatSignatureDouble(geometry.NaturalHeight),
            "boxes=" + nodeSignature,
            "polygons=" + polygonSignature,
            "lines=" + connectorSignature);
    }

    private static string BoolFlag(bool value) => value ? "1" : "0";

    private static string FormatSignatureDouble(double value) =>
        double.IsFinite(value)
            ? Math.Round(value, 3, MidpointRounding.AwayFromZero).ToString("0.###", CultureInfo.InvariantCulture)
            : "0";

    private static string NormalizeSignatureText(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("|", "/", StringComparison.Ordinal)
            .Replace(";", ",", StringComparison.Ordinal)
            .Replace(":", "-", StringComparison.Ordinal);

        return string.Join(
            " ",
            normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
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
