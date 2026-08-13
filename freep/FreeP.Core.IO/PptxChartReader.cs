using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Free.Shared.Opc;
using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>
/// Parses a <c>ppt/charts/chartN.xml</c> part from a .pptx archive and returns a
/// <see cref="ChartShape"/> model. Entry point: <see cref="ReadChartPart"/>.
/// </summary>
internal static class PptxChartReader
{
    /// <summary>Upper bound on cached points read for one chart series (one worksheet column's worth).</summary>
    private const int MaxChartSeriesPoints = 1_048_576;

    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace A = PptxColorReader.A;
    private static readonly XNamespace Cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";

    // Default accent color cycle (theme slots accent1..6).
    private static readonly ThemeColorSlot[] AccentSlots =
    [
        ThemeColorSlot.Accent1, ThemeColorSlot.Accent2, ThemeColorSlot.Accent3,
        ThemeColorSlot.Accent4, ThemeColorSlot.Accent5, ThemeColorSlot.Accent6
    ];

    /// <summary>
    /// Reads a chart part from the archive and returns the parsed <see cref="ChartShape"/>,
    /// or null if the part is missing or malformed.
    /// </summary>
    internal static ChartShape? ReadChartPart(
        ZipArchive archive, string chartPath, PresentationColorScheme scheme)
    {
        var entry = archive.GetEntry(chartPath);
        if (entry is null) return null;

        XDocument doc;
        try
        {
            doc = OpcXml.LoadXml(entry);
        }
        catch { return null; }

        var chartSpace = doc.Root; // c:chartSpace
        if (chartSpace is null) return null;

        var protection = chartSpace.Element(C + "protection");

        var chartEl = chartSpace.Element(C + "chart");
        if (chartEl is null) return null;

        var shape = new ChartShape
        {
            StyleId = ReadStyleId(chartSpace),
            ChartDate1904 = ParseNullableBoolElement(chartSpace.Element(C + "date1904")),
            ChartLanguage = chartSpace.Element(C + "lang")?.Attribute("val")?.Value,
            RoundedCorners = ParseNullableBoolElement(chartSpace.Element(C + "roundedCorners")),
            PreservedPivotSourceXml = chartSpace.Element(C + "pivotSource")
                ?.ToString(SaveOptions.DisableFormatting),
            PreservedChartProtectionXml = chartSpace.Element(C + "protection")
                ?.ToString(SaveOptions.DisableFormatting),
            ChartObjectProtected = ParseNullableBoolAttr(protection?.Attribute("chartObject")?.Value),
            ChartDataProtected = ParseNullableBoolAttr(protection?.Attribute("data")?.Value),
            ChartFormattingProtected = ParseNullableBoolAttr(protection?.Attribute("formatting")?.Value),
            ChartSelectionProtected = ParseNullableBoolAttr(protection?.Attribute("selection")?.Value),
            PreservedChartSpaceExtensionsXml = chartSpace.Element(C + "extLst")
                ?.ToString(SaveOptions.DisableFormatting)
        };
        var chartSpaceSpPr = chartSpace.Element(C + "spPr");
        shape.ChartAreaFill = chartSpaceSpPr is null ? null : PptxColorReader.TryReadFill(chartSpaceSpPr, scheme);
        shape.ChartAreaOutline = chartSpaceSpPr is null
            ? null
            : PptxColorReader.TryReadOutline(chartSpaceSpPr.Element(A + "ln"), scheme);
        // PowerPoint uses an 18pt default for chart titles when c:txPr is absent,
        // but axes and data labels retain their role-specific defaults. Preserve
        // that inherited state without turning it into authored chart text.
        shape.TextStyle = ReadChartTextStyle(chartSpace.Element(C + "txPr"), scheme)
            ?? new ChartTextStyle { FontSizePt = 18.0, IsImplicitDefault = true };

        // Title
        var titleElement = chartEl.Element(C + "title");
        shape.Title = ReadTitle(titleElement);
        shape.TitleOverlay = ReadTitleOverlay(titleElement);
        shape.TitleStyle = ReadTitleStyle(titleElement, scheme);
        shape.DisplayBlanksAs = ReadDisplayBlanksAs(
            chartEl.Element(C + "dispBlanksAs")?.Attribute("val")?.Value);
        shape.PlotVisibleOnly = ParseNullableBoolElement(chartEl.Element(C + "plotVisOnly"));
        shape.ShowDataLabelsOverMaximum = ParseNullableBoolElement(
            chartEl.Element(C + "showDLblsOverMax"));
        shape.View3D = ReadView3D(chartEl.Element(C + "view3D"));

        // plotArea
        var plotArea = chartEl.Element(C + "plotArea");
        if (plotArea is null) return shape;
        shape.PlotAreaManualLayout = ReadManualLayout(plotArea.Element(C + "layout"));
        var plotAreaSpPr = plotArea.Element(C + "spPr");
        shape.PlotAreaFill = plotAreaSpPr is null ? null : PptxColorReader.TryReadFill(plotAreaSpPr, scheme);
        shape.PlotAreaOutline = plotAreaSpPr is null
            ? null
            : PptxColorReader.TryReadOutline(plotAreaSpPr.Element(A + "ln"), scheme);

        var serIdxMap = DetectChartTypeAndSeries(plotArea, shape, scheme);
        var seriesLines = plotArea.Elements()
            .Where(IsChartTypeElement)
            .Select(chartType => chartType.Element(C + "serLines"))
            .FirstOrDefault(element => element is not null);
        shape.SeriesLinesSpecified = seriesLines is not null;
        shape.SeriesLineStyle = seriesLines is null
            ? null
            : ReadLineStyle(seriesLines.Element(C + "spPr")?.Element(A + "ln"), scheme);
        shape.LeaderLinesSpecified = plotArea.Elements()
            .Where(IsChartTypeElement)
            .Any(chartType => chartType.Element(C + "leaderLines") is not null);
        if (shape.ChartType == ChartType.OfPie)
            shape.OfPieSeriesLinesSpecified = shape.SeriesLinesSpecified;
        ApplyPowerPointAutomaticTitleDefault(chartEl, shape);

        // Axes (catAx / dateAx = category axis; valAx = value axis)
        bool primaryValAxRead = false;
        foreach (var axEl in plotArea.Elements())
        {
            if (axEl.Name == C + "catAx" || axEl.Name == C + "dateAx")
                ReadAxis(axEl, shape.CategoryAxis, scheme);
            else if (axEl.Name == C + "valAx")
            {
                if (!primaryValAxRead)
                {
                    ReadAxis(axEl, shape.ValueAxis, scheme);
                    primaryValAxRead = true;
                }
                else
                {
                    shape.SecondaryValueAxis = new ChartAxis();
                    ReadAxis(axEl, shape.SecondaryValueAxis, scheme);
                }
            }
        }

        // Chart-level data labels (c:plotArea/c:xxx/c:dLbls or chart-level)
        // Per OOXML the dLbls lives inside each plot-type element, read it from the first chart type el.
        var firstChartTypeEl = plotArea.Elements().FirstOrDefault(e =>
            e.Name.LocalName is "barChart" or "lineChart" or "pieChart" or "doughnutChart"
            or "areaChart" or "scatterChart" or "bubbleChart" or "radarChart"
            or "bar3DChart" or "line3DChart" or "pie3DChart" or "area3DChart" or "ofPieChart"
            or "stockChart" or "surfaceChart" or "surface3DChart");
        var chartDataLabelsEl = firstChartTypeEl?.Element(C + "dLbls");
        shape.DataLabels = ReadDataLabels(chartDataLabelsEl, scheme);
        ApplyPowerPointPercentStackedDataLabelDefaults(firstChartTypeEl, chartDataLabelsEl, shape.DataLabels);
        ApplyPowerPointPiePercentDataLabelDefaults(firstChartTypeEl, chartDataLabelsEl, shape.DataLabels);
        shape.DataTable = ReadDataTable(plotArea.Element(C + "dTable"), scheme);

        // Secondary value axis detection
        // Each plotType element has c:axId refs; if there's a second c:valAx, check which series use it.
        var valAxIds = new List<int>();
        foreach (var axEl in plotArea.Elements(C + "valAx"))
        {
            var axId = ParseInt(axEl.Element(C + "axId")?.Attribute("val")?.Value);
            valAxIds.Add(axId);
        }
        // Scatter and bubble charts always use two value axes: one for X and one
        // for Y. They are not a primary/secondary pair, so do not reclassify all
        // of their series onto a nonexistent secondary value axis.
        bool hasIndependentXAndYAxis = shape.ChartType is ChartType.Scatter or ChartType.Bubble;

        // If we have 2+ valAx elements, the second one is the secondary axis.
        if (!hasIndependentXAndYAxis && valAxIds.Count >= 2)
        {
            int secondaryAxId = valAxIds[1]; // second valAx is secondary

            // Now detect which series are on the secondary axis.
            // A plot group element references its axes via c:axId children.
            // If a plot group's second c:axId equals secondaryAxId, its series are on the secondary axis.
            foreach (var plotEl in plotArea.Elements())
            {
                var axIds = plotEl.Elements(C + "axId").Select(a => ParseInt(a.Attribute("val")?.Value)).ToList();
                if (axIds.Count >= 2 && axIds.Any(id => id == secondaryAxId))
                {
                    // All series in this plot group are on the secondary axis.
                    // Resolve each c:ser's c:idx through the idx→ChartSeries map built during reading.
                    // This is correct for combo charts where c:idx values are interleaved across
                    // chart-type groups (e.g. primary group has idx 0,2 and secondary group has idx 1)
                    // — positional indexing into shape.Series would flag the wrong series in that case.
                    foreach (var serEl in plotEl.Elements(C + "ser"))
                    {
                        int serIdx = ParseInt(serEl.Element(C + "idx")?.Attribute("val")?.Value);
                        if (serIdxMap.TryGetValue(serIdx, out var mappedSeries))
                            mappedSeries.OnSecondaryAxis = true;
                        else if (serIdx < shape.Series.Count)
                            // Fall back to positional index for series with no recorded c:idx
                            shape.Series[serIdx].OnSecondaryAxis = true;
                    }
                }
            }
        }

        // Legend
        var legendEl = chartEl.Element(C + "legend");
        shape.Legend = legendEl is not null
            ? legendEl.Element(C + "legendPos")?.Attribute("val")?.Value switch
            {
                "r" or "rt" => LegendPosition.Right,
                "l"         => LegendPosition.Left,
                "t"         => LegendPosition.Top,
                "b"         => LegendPosition.Bottom,
                _           => LegendPosition.Right
            }
            : (LegendPosition?)null;
        if (legendEl is not null)
        {
            shape.LegendManualLayout = ReadManualLayout(legendEl.Element(C + "layout"));
            shape.LegendOverlay = ParseNullableBoolAttr(
                legendEl.Element(C + "overlay")?.Attribute("val")?.Value);
            shape.LegendTextStyle = ReadChartTextStyle(legendEl.Element(C + "txPr"), scheme);
        }

        return shape;
    }

    /// <summary>
    /// Reads the compact ChartEx payload used by current PowerPoint waterfall charts.
    /// ChartEx stores values in chartData and marks total points with
    /// cx:series/cx:layoutPr/cx:subtotals/cx:idx.
    /// </summary>
    internal static ChartShape? ReadChartExPart(
        ZipArchive archive, string chartPath, PresentationColorScheme scheme)
    {
        var entry = archive.GetEntry(chartPath);
        if (entry is null) return null;

        XDocument doc;
        try { doc = OpcXml.LoadXml(entry); }
        catch { return null; }

        var chartSpace = doc.Root;
        var chart = chartSpace?.Element(Cx + "chart");
        var chartExTitle = chart?.Element(Cx + "title");
        var region = chart?.Element(Cx + "plotArea")?.Element(Cx + "plotAreaRegion");
        var seriesEl = region?.Element(Cx + "series");
        if (chartSpace is null || chart is null || region is null || seriesEl is null)
            return null;

        var dataElements = chartSpace.Element(Cx + "chartData")?.Elements(Cx + "data").ToList() ?? [];
        var categoryData = FindChartExCategoryData(dataElements);
        var categoryLevel = categoryData?.Element(Cx + "strDim")?.Element(Cx + "lvl");
        var categories = categoryLevel?.Elements(Cx + "pt")
            .OrderBy(point => ParseInt(point.Attribute("idx")?.Value))
            .Select(point => point.Value)
            .ToArray() ?? [];

        var shape = new ChartShape
        {
            ChartExLayoutId = seriesEl.Attribute("layoutId")?.Value,
            ChartType = string.Equals(seriesEl.Attribute("layoutId")?.Value, "waterfall", StringComparison.OrdinalIgnoreCase)
                ? ChartType.Waterfall
                : ChartType.ColumnClustered,
            ShowWaterfallConnectorLines = ParseNullableBoolAttr(
                seriesEl.Element(Cx + "layoutPr")?.Element(Cx + "visibility")?.Attribute("connectorLines")?.Value) ?? true,
            WaterfallTotalPointIndices = seriesEl.Element(Cx + "layoutPr")?.Element(Cx + "subtotals")?
                .Elements(Cx + "idx")
                .Select(element => ParseInt(element.Attribute("val")?.Value))
                .Where(index => index >= 0)
                .Distinct()
                .OrderBy(index => index)
                .ToList(),
            Title = ReadChartExTitle(chartExTitle),
            TitleOverlay = ParseNullableBoolAttr(chartExTitle?.Attribute("overlay")?.Value),
            ChartExTitlePosition = chartExTitle?.Attribute("pos")?.Value switch
            {
                "t" => ChartExTitlePosition.Top,
                "b" => ChartExTitlePosition.Bottom,
                "l" => ChartExTitlePosition.Left,
                "r" => ChartExTitlePosition.Right,
                _ => null,
            },
            ChartExTitleAlignment = chartExTitle?.Attribute("align")?.Value switch
            {
                "near" => ChartExTitleAlignment.Near,
                "ctr" => ChartExTitleAlignment.Center,
                "far" => ChartExTitleAlignment.Far,
                _ => null,
            },
            TitleStyle = ReadChartTextStyle(chartExTitle?.Element(Cx + "txPr"), scheme),
        };
        var chartSpaceSpPr = chartSpace.Element(Cx + "spPr");
        shape.ChartAreaFill = chartSpaceSpPr is null
            ? null
            : PptxColorReader.TryReadFill(chartSpaceSpPr, scheme);
        shape.ChartAreaOutline = chartSpaceSpPr is null
            ? null
            : PptxColorReader.TryReadOutline(chartSpaceSpPr.Element(A + "ln"), scheme);
        var plotSurfaceSpPr = region.Element(Cx + "plotSurface")?.Element(Cx + "spPr");
        shape.PlotAreaFill = plotSurfaceSpPr is null
            ? null
            : PptxColorReader.TryReadFill(plotSurfaceSpPr, scheme);
        shape.PlotAreaOutline = plotSurfaceSpPr is null
            ? null
            : PptxColorReader.TryReadOutline(plotSurfaceSpPr.Element(A + "ln"), scheme);
        var legend = chart.Element(Cx + "legend");
        if (legend is not null)
        {
            shape.Legend = legend.Attribute("pos")?.Value switch
            {
                "l" => LegendPosition.Left,
                "t" => LegendPosition.Top,
                "b" => LegendPosition.Bottom,
                "r" => LegendPosition.Right,
                _ => null,
            };
            shape.LegendOverlay = ParseNullableBoolAttr(legend.Attribute("overlay")?.Value);
            shape.LegendTextStyle = ReadChartTextStyle(legend.Element(Cx + "txPr"), scheme);
        }
        shape.Categories.AddRange(categories);

        foreach (var seriesElement in region.Elements(Cx + "series"))
        {
            var series = new ChartSeries
            {
                Name = seriesElement.Element(Cx + "tx")?.Element(Cx + "txData")?.Element(Cx + "v")?.Value ?? string.Empty,
                ChartExLayoutId = seriesElement.Attribute("layoutId")?.Value,
            };
            var seriesData = FindChartExSeriesData(dataElements, seriesElement);
            var valueLevel = FindChartExValueDataLevel(seriesData);
            if (valueLevel is not null)
            {
                series.Values.AddRange(ReadChartExValues(valueLevel));
            }

            var seriesShapeProperties = seriesElement.Element(Cx + "spPr");
            if (seriesShapeProperties is not null)
                ReadSeriesShapeProperties(seriesShapeProperties, scheme, series);

            series.ValueColorScale = ReadChartExValueColorScale(seriesElement, scheme);
            ReadChartExDataPoints(seriesElement, scheme, series);
            ReadChartExDataLabels(seriesElement.Element(Cx + "dataLabels"), scheme, series);

            shape.Series.Add(series);
        }

        return shape;
    }

    private static ChartValueColorScale? ReadChartExValueColorScale(
        XElement seriesElement,
        PresentationColorScheme scheme)
    {
        var colors = seriesElement.Element(Cx + "valueColors");
        var positions = seriesElement.Element(Cx + "valueColorPositions");
        if (colors is null && positions is null)
            return null;

        var scale = new ChartValueColorScale
        {
            MinColor = ReadChartExValueColor(colors?.Element(Cx + "minColor"), scheme),
            MidColor = ReadChartExValueColor(colors?.Element(Cx + "midColor"), scheme),
            MaxColor = ReadChartExValueColor(colors?.Element(Cx + "maxColor"), scheme),
            PositionCount = ParseNullableInt(positions?.Attribute("count")?.Value),
            MinPosition = ReadChartExValueColorPosition(positions?.Element(Cx + "min")),
            MidPosition = ReadChartExValueColorPosition(positions?.Element(Cx + "mid")),
            MaxPosition = ReadChartExValueColorPosition(positions?.Element(Cx + "max")),
        };
        return scale;
    }

    private static ThemeAwareColor? ReadChartExValueColor(
        XElement? element,
        PresentationColorScheme scheme) =>
        element is null
            ? null
            : PptxColorReader.TryReadColor(element.Element(A + "solidFill"), scheme);

    private static ChartValueColorPosition? ReadChartExValueColorPosition(XElement? element)
    {
        if (element is null)
            return null;

        if (element.Element(Cx + "extremeValue") is not null)
            return new ChartValueColorPosition { IsExtreme = true };

        var number = element.Element(Cx + "number")?.Attribute("val")?.Value;
        if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var numberValue))
            return new ChartValueColorPosition { Number = numberValue };

        var percent = element.Element(Cx + "percent")?.Attribute("val")?.Value;
        if (double.TryParse(percent, NumberStyles.Float, CultureInfo.InvariantCulture, out var percentValue))
            return new ChartValueColorPosition { Percent = percentValue };

        return null;
    }

    private static void ReadChartExDataPoints(
        XElement seriesElement,
        PresentationColorScheme scheme,
        ChartSeries series)
    {
        foreach (var dataPoint in seriesElement.Elements(Cx + "dataPt"))
        {
            if (!int.TryParse(
                    dataPoint.Attribute("idx")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var index)
                || index < 0)
                continue;

            var shapeProperties = dataPoint.Element(Cx + "spPr");
            if (shapeProperties is null)
                continue;

            var style = new ChartPointStyle();
            var fill = PptxColorReader.TryReadFill(shapeProperties, scheme);
            switch (fill)
            {
                case ShapeFill.None:
                    style.Fill = fill;
                    break;
                case ShapeFill.Solid solid:
                    style.FillColor = solid.Color;
                    break;
                case ShapeFill.Gradient gradient:
                    style.Fill = gradient;
                    break;
                case ShapeFill.Pattern pattern:
                    style.Fill = pattern;
                    break;
            }

            var outline = PptxColorReader.TryReadOutline(
                shapeProperties.Element(A + "ln"), scheme);
            if (outline is ShapeOutline.Visible visible)
            {
                style.StrokeColor = visible.Color;
                style.StrokeWidthPt = visible.WidthPt;
            }
            else if (outline is ShapeOutline.GradientVisible gradientOutline)
            {
                style.StrokeWidthPt = gradientOutline.WidthPt;
            }

            if (style.Fill is not null
                || style.FillColor is not null
                || style.StrokeColor is not null
                || style.StrokeWidthPt is not null)
            {
                if (series.PointStyles.TryGetValue(index, out var existing))
                {
                    style.DataLabels = existing.DataLabels;
                    style.Marker = existing.Marker;
                    style.ExplosionPercent = existing.ExplosionPercent;
                }

                series.PointStyles[index] = style;
            }
        }
    }

    private static void ReadChartExDataLabels(
        XElement? dataLabels,
        PresentationColorScheme scheme,
        ChartSeries series)
    {
        if (dataLabels is null)
            return;

        var labels = ReadChartExDataLabelValues(dataLabels, scheme);
        if (labels is not null)
            series.DataLabels = labels;

        foreach (var pointLabel in dataLabels.Elements(Cx + "dataLabel"))
        {
            if (!int.TryParse(
                    pointLabel.Attribute("idx")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var index)
                || index < 0)
                continue;

            var pointLabels = ReadChartExDataLabelValues(pointLabel, scheme);
            if (pointLabels is null)
                continue;

            if (!series.PointStyles.TryGetValue(index, out var style))
                style = new ChartPointStyle();
            style.DataLabels = pointLabels;
            series.PointStyles[index] = style;
        }

        foreach (var hiddenLabel in dataLabels.Elements(Cx + "dataLabelHidden"))
        {
            if (!int.TryParse(
                    hiddenLabel.Attribute("idx")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var index)
                || index < 0)
                continue;

            if (!series.PointStyles.TryGetValue(index, out var style))
                style = new ChartPointStyle();
            style.DataLabels = new ChartDataLabels { Delete = true };
            series.PointStyles[index] = style;
        }
    }

    private static ChartDataLabels? ReadChartExDataLabelValues(
        XElement element,
        PresentationColorScheme scheme)
    {
        var visibility = element.Element(Cx + "visibility");
        var hasContent = visibility is not null
            || element.Attribute("pos") is not null
            || element.Element(Cx + "numFmt") is not null
            || element.Element(Cx + "txPr") is not null
            || element.Element(Cx + "separator") is not null;
        if (!hasContent)
            return null;

        var position = element.Attribute("pos")?.Value switch
        {
            "ctr" => DataLabelPosition.Center,
            "inEnd" => DataLabelPosition.InsideEnd,
            "outEnd" => DataLabelPosition.OutsideEnd,
            "inBase" => DataLabelPosition.InsideBase,
            "bestFit" => DataLabelPosition.BestFit,
            "t" => DataLabelPosition.Above,
            "b" => DataLabelPosition.Below,
            "l" => DataLabelPosition.Left,
            "r" => DataLabelPosition.Right,
            _ => (DataLabelPosition?)null
        };

        return new ChartDataLabels
        {
            ShowSeriesName = ParseNullableBoolAttr(visibility?.Attribute("seriesName")?.Value) ?? false,
            ShowCategoryName = ParseNullableBoolAttr(visibility?.Attribute("categoryName")?.Value) ?? false,
            ShowValue = ParseNullableBoolAttr(visibility?.Attribute("value")?.Value) ?? false,
            ShowPercent = ParseNullableBoolAttr(visibility?.Attribute("percent")?.Value) ?? false,
            ShowLegendKey = ParseNullableBoolAttr(visibility?.Attribute("legendKey")?.Value) ?? false,
            ShowBubbleSize = ParseNullableBoolAttr(visibility?.Attribute("bubbleSize")?.Value) ?? false,
            ShowLeaderLines = ParseNullableBoolAttr(visibility?.Attribute("leaderLines")?.Value),
            Position = position,
            NumberFormat = element.Element(Cx + "numFmt")?.Attribute("formatCode")?.Value,
            Separator = element.Element(Cx + "separator")?.Value,
            TextStyle = ReadChartTextStyle(element.Element(Cx + "txPr"), scheme)
        };
    }

    private static string? ReadChartExTitle(XElement? title)
    {
        if (title is null)
            return null;

        var value = title.Descendants(Cx + "v")
            .Select(element => element.Value)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
        if (value is not null)
            return value;

        var richText = string.Concat(title.Descendants(A + "t").Select(element => element.Value));
        return string.IsNullOrWhiteSpace(richText) ? string.Empty : richText;
    }

    private static XElement? FindChartExCategoryData(IReadOnlyList<XElement> dataElements)
    {
        var stringData = dataElements
            .Where(data => data.Element(Cx + "strDim") is not null)
            .ToList();
        if (stringData.Count == 1)
            return stringData[0];

        var categoryData = stringData
            .Where(data => string.Equals(
                data.Element(Cx + "strDim")?.Attribute("type")?.Value,
                "cat",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        return categoryData.Count == 1 ? categoryData[0] : null;
    }

    private static XElement? FindChartExSeriesData(
        IReadOnlyList<XElement> dataElements,
        XElement series)
    {
        var dataId = series.Element(Cx + "dataId")?.Attribute("val")?.Value;
        if (int.TryParse(dataId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return dataElements.FirstOrDefault(data => ParseInt(data.Attribute("id")?.Value) == id);

        return dataElements.Count == 1 ? dataElements[0] : null;
    }

    private static XElement? FindChartExValueDataLevel(XElement? data)
    {
        if (data is null)
            return null;

        var numericDimensions = data.Elements(Cx + "numDim").ToList();
        if (numericDimensions.Count == 1)
            return numericDimensions[0].Element(Cx + "lvl");

        var valueDimension = numericDimensions
            .Where(dimension => string.Equals(
                dimension.Attribute("type")?.Value,
                "val",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        return valueDimension.Count == 1 ? valueDimension[0].Element(Cx + "lvl") : null;
    }

    private static IReadOnlyList<double?> ReadChartExValues(XElement level)
    {
        var points = level.Elements(Cx + "pt")
            .Select(point =>
            {
                var index = ParseInt(point.Attribute("idx")?.Value);
                var value = double.TryParse(
                    point.Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                    ? (double?)parsed
                    : null;
                return (Index: index, Value: value);
            })
            .ToList();
        var pointCount = ParseInt(level.Attribute("ptCount")?.Value);
        var count = Math.Max(pointCount, points.Count == 0 ? 0 : points.Max(point => point.Index) + 1);
        // ptCount/idx are file-declared; clamp before allocating so a corrupt chart cannot ask for
        // an int.MaxValue-element array.
        count = Math.Clamp(count, 0, MaxChartSeriesPoints);
        var values = Enumerable.Repeat<double?>(null, count).ToArray();
        foreach (var point in points.Where(point => point.Index >= 0 && point.Index < values.Length))
            values[point.Index] = point.Value;

        return values;
    }

    private static bool IsChartTypeElement(XElement element) => element.Name.LocalName is
        "barChart" or "bar3DChart" or "lineChart" or "line3DChart" or
        "pieChart" or "pie3DChart" or "ofPieChart" or "doughnutChart" or
        "areaChart" or "area3DChart" or "scatterChart" or "bubbleChart" or
        "stockChart" or "radarChart" or "surfaceChart" or "surface3DChart" or
        "funnelChart" or "waterfallChart";

    private static int? ReadStyleId(XElement chartSpace)
    {
        // Office may place a c14:style compatibility marker before the
        // authoritative c:style. Prefer the chart namespace so style 2 is not
        // misread as the compatibility style 102.
        var style = chartSpace.Element(C + "style")
            ?? chartSpace.Descendants(C + "style").FirstOrDefault();
        return int.TryParse(style?.Attribute("val")?.Value, out var value) ? value : null;
    }

    // ── Title ─────────────────────────────────────────────────────────────────

    private static string? ReadTitle(XElement? titleEl)
    {
        if (titleEl is null) return null;

        var tx = titleEl.Element(C + "tx");
        if (tx is not null)
        {
            // Rich text path: c:tx/c:rich/a:p/a:r/a:t
            var rich = tx.Element(C + "rich");
            if (rich is not null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var t in rich.Descendants(A + "t"))
                    sb.Append(t.Value);
                var text = sb.ToString().Trim();
                if (text.Length > 0) return text;
            }

            // Cached string ref path: c:tx/c:strRef/c:strCache/c:pt/c:v
            var v = tx.Element(C + "strRef")
                ?.Element(C + "strCache")
                ?.Elements(C + "pt").FirstOrDefault()
                ?.Element(C + "v")?.Value;
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }

        return null;
    }

    private static bool? ReadTitleOverlay(XElement? titleEl) =>
        ParseNullableBoolElement(titleEl?.Element(C + "overlay"));

    // ── Chart type dispatch ───────────────────────────────────────────────────

    private static Dictionary<int, ChartSeries> DetectChartTypeAndSeries(
        XElement plotArea, ChartShape shape, PresentationColorScheme scheme)
    {
        // idx→ChartSeries map: populated as series are read so secondary-axis detection
        // can resolve a c:idx value to the right ChartSeries regardless of append order.
        var idxMap = new Dictionary<int, ChartSeries>();
        bool primaryFound = false;

        foreach (var el in plotArea.Elements())
        {
            bool isChartType = el.Name.LocalName is
                "barChart" or "bar3DChart" or "lineChart" or "line3DChart" or
                "pieChart" or "pie3DChart" or "ofPieChart" or "doughnutChart" or
                "areaChart" or "area3DChart" or "scatterChart" or "bubbleChart" or
                "stockChart" or "radarChart" or "surfaceChart" or "surface3DChart" or
                "funnelChart" or "waterfallChart";

            if (!isChartType) continue;

            if (!primaryFound)
            {
                // First chart-type group: sets shape.ChartType and reads primary series.
                primaryFound = true;
                switch (el.Name.LocalName)
                {
                    case "barChart":
                    case "bar3DChart":
                        ReadBarChart(el, shape, scheme, idxMap); break;
                    case "lineChart":
                    case "line3DChart":
                        ReadLineChart(el, shape, scheme, idxMap); break;
                    case "pieChart":
                    case "pie3DChart":
                        ReadPieChart(el, shape, scheme, idxMap); break;
                    case "ofPieChart":
                        ReadOfPieChart(el, shape, scheme, idxMap); break;
                    case "doughnutChart":
                        ReadDoughnutChart(el, shape, scheme, idxMap); break;
                    case "areaChart":
                    case "area3DChart":
                        ReadAreaChart(el, shape, scheme, idxMap); break;
                    case "scatterChart":
                        ReadScatterChartDistinct(el, shape, scheme, idxMap); break;
                    case "bubbleChart":
                        ReadBubbleChart(el, shape, scheme, idxMap); break;
                    case "stockChart":
                        ReadStockChart(el, shape, scheme, idxMap); break;
                    case "funnelChart":
                        ReadFunnelChart(el, shape, scheme, idxMap); break;
                    case "waterfallChart":
                        ReadWaterfallChart(el, shape, scheme, idxMap); break;
                    case "radarChart":
                        ReadRadarChart(el, shape, scheme, idxMap); break;
                    case "surfaceChart":
                        ReadSurfaceChart(el, shape, scheme, idxMap, is3D: false); break;
                    case "surface3DChart":
                        ReadSurfaceChart(el, shape, scheme, idxMap, is3D: true); break;
                }
            }
            else
            {
                // CA4: Secondary chart-type group in a combo chart (e.g. lineChart holding secondary
                // series). Read its c:ser elements without changing shape.ChartType.
                // The secondary axis detection (valAxIds loop below) will then mark these series
                // with OnSecondaryAxis = true via their c:idx values resolved through idxMap.
                // CA4b: Also stamp OverrideChartType on each newly-added series so the renderer
                // knows to draw them with the secondary group's chart type (e.g. Line) rather
                // than the primary chart type (e.g. ColumnClustered).
                // Snapshot which series indices already exist before reading the secondary group.
                var keysBefore = new System.Collections.Generic.HashSet<int>(idxMap.Keys);
                switch (el.Name.LocalName)
                {
                    case "scatterChart":
                        ReadScatterSeriesFromChart(el, shape, scheme, idxMap); break;
                    case "bubbleChart":
                        ReadBubbleSeriesFromChart(el, shape, scheme, idxMap); break;
                    default:
                        // All other combo secondaries (lineChart, barChart, areaChart, etc.)
                        // use the standard cat/val series format.
                        ReadSeriesFromChart(el, shape, scheme, idxMap); break;
                }
                // Derive override chart type from the secondary group element name.
                ChartType? overrideType;
                if (el.Name.LocalName is "lineChart" or "line3DChart")
                {
                    bool hasMarkers = el.Elements(C + "ser").Any(s =>
                    {
                        var sym = s.Element(C + "marker")?.Element(C + "symbol")?.Attribute("val")?.Value;
                        return sym is null || sym != "none";
                    });
                    overrideType = hasMarkers ? ChartType.LineMarkers : ChartType.Line;
                }
                else if (el.Name.LocalName == "stockChart")
                {
                    overrideType = ChartType.Stock;
                }
                else if (el.Name.LocalName is "barChart" or "bar3DChart")
                {
                    var barDir   = el.Element(C + "barDir")?.Attribute("val")?.Value ?? "col";
                    var grouping = el.Element(C + "grouping")?.Attribute("val")?.Value ?? "clustered";
                    overrideType = (barDir, grouping) switch
                    {
                        ("col", "stacked")        => ChartType.ColumnStacked,
                        ("col", "percentStacked") => ChartType.ColumnStacked100,
                        ("bar", _)                => ChartType.BarClustered,
                        _                         => ChartType.ColumnClustered
                    };
                }
                else if (el.Name.LocalName is "areaChart" or "area3DChart")
                {
                    var grouping = el.Element(C + "grouping")?.Attribute("val")?.Value ?? "standard";
                    overrideType = grouping == "stacked" ? ChartType.AreaStacked : ChartType.Area;
                }
                else if (el.Name.LocalName is "surfaceChart" or "surface3DChart")
                {
                    overrideType = el.Name.LocalName == "surface3DChart"
                        ? ChartType.Surface3D
                        : ChartType.Surface;
                }
                else
                {
                    overrideType = null;
                }
                // Stamp the override on series that were just added by this secondary group.
                if (overrideType.HasValue)
                {
                    foreach (var kvp in idxMap)
                        if (!keysBefore.Contains(kvp.Key))
                            kvp.Value.OverrideChartType = overrideType;
                }
            }
        }

        if (!primaryFound)
            shape.ChartType = ChartType.Unknown;

        return idxMap;
    }

    private static void ReadBarChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);

        var barDir   = el.Element(C + "barDir")?.Attribute("val")?.Value   ?? "col";
        var grouping = el.Element(C + "grouping")?.Attribute("val")?.Value ?? "clustered";

        shape.ChartType = (barDir, grouping) switch
        {
            ("col", "clustered")      => ChartType.ColumnClustered,
            ("col", "stacked")        => ChartType.ColumnStacked,
            ("col", "percentStacked") => ChartType.ColumnStacked100,
            ("bar", "clustered")      => ChartType.BarClustered,
            ("bar", "stacked")        => ChartType.BarStacked,
            ("bar", "percentStacked") => ChartType.BarStacked100,
            _                         => ChartType.ColumnClustered
        };

        shape.BarGapWidthPercent = ReadBarGapWidth(el);
        shape.BarOverlapPercent = ReadBarOverlap(el);
        shape.BarGapDepthPercent = ReadBarGapDepth(el) ??
            (el.Name.LocalName == "bar3DChart" ? 150 : null);
        if (el.Name.LocalName == "bar3DChart")
            shape.ThreeDStyle = barDir == "bar" ? ChartThreeDStyle.Bar : ChartThreeDStyle.Column;

        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadLineChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
        if (el.Name.LocalName == "line3DChart")
            shape.ThreeDStyle = ChartThreeDStyle.Line;

        // A line chart "has markers" when any series has an explicit marker that is not "none",
        // or has no marker element at all (OOXML default for lineChart is to show markers).
        bool hasMarkers = el.Elements(C + "ser").Any(s =>
        {
            var sym = s.Element(C + "marker")?.Element(C + "symbol")?.Attribute("val")?.Value;
            return sym is null || sym != "none";
        });

        shape.ChartType = hasMarkers ? ChartType.LineMarkers : ChartType.Line;
        shape.ShowDropLines = el.Element(C + "dropLines") is not null;
        var upDownBars = el.Element(C + "upDownBars");
        shape.ShowUpDownBars = upDownBars is not null;
        shape.UpDownBarGapWidthPercent = ParseNullableInt(
            upDownBars?.Element(C + "gapWidth")?.Attribute("val")?.Value);
        var upBarSpPr = upDownBars?.Element(C + "upBars")?.Element(C + "spPr");
        shape.UpBarFill = upBarSpPr is null ? null : PptxColorReader.TryReadFill(upBarSpPr, scheme);
        var downBarSpPr = upDownBars?.Element(C + "downBars")?.Element(C + "spPr");
        shape.DownBarFill = downBarSpPr is null ? null : PptxColorReader.TryReadFill(downBarSpPr, scheme);
        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadStockChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
        shape.ChartType = ChartType.Stock;
        shape.HasHighLowLines = el.Element(C + "hiLowLines") is not null;
        shape.ShowDropLines = el.Element(C + "dropLines") is not null;
        var upDownBars = el.Element(C + "upDownBars");
        shape.ShowUpDownBars = upDownBars is not null;
        shape.UpDownBarGapWidthPercent = ParseNullableInt(
            upDownBars?.Element(C + "gapWidth")?.Attribute("val")?.Value);
        var upBarSpPr = upDownBars?.Element(C + "upBars")?.Element(C + "spPr");
        shape.UpBarFill = upBarSpPr is null ? null : PptxColorReader.TryReadFill(upBarSpPr, scheme);
        var downBarSpPr = upDownBars?.Element(C + "downBars")?.Element(C + "spPr");
        shape.DownBarFill = downBarSpPr is null ? null : PptxColorReader.TryReadFill(downBarSpPr, scheme);
        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadFunnelChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
        shape.ChartType = ChartType.Funnel;
        shape.BarGapWidthPercent = ParseNullableInt(el.Element(C + "gapWidth")?.Attribute("val")?.Value);
        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadWaterfallChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
        shape.ChartType = ChartType.Waterfall;
        shape.ShowWaterfallConnectorLines = ParseNullableBoolElement(
            el.Element(C + "showConnectorLines")) ?? true;
        shape.BarGapWidthPercent = ParseNullableInt(el.Element(C + "gapWidth")?.Attribute("val")?.Value);
        shape.WaterfallTotalPointIndices = ReadWaterfallTotalIndices(el);
        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static List<int>? ReadWaterfallTotalIndices(XElement waterfall)
    {
        var ext = waterfall.Ancestors(C + "chartSpace").FirstOrDefault()?.Element(C + "extLst")
            ?.Descendants().FirstOrDefault(element => element.Name.LocalName == "waterfallTotals");
        if (ext is null)
            return null;

        return ext.Elements().Where(element => element.Name.LocalName == "idx")
            .Select(element => int.TryParse(element.Attribute("val")?.Value, out var index) ? index : -1)
            .Where(index => index >= 0)
            .Distinct()
            .OrderBy(index => index)
            .ToList();
    }

    private static void ReadSurfaceChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap, bool is3D)
    {
        ReadVaryColors(el, shape);
        if (is3D)
        {
            var wireframe = el.Element(C + "wireframe");
            shape.WireframeSpecified = wireframe is not null;
            shape.Wireframe = ParseBoolAttr(wireframe);
        }
        shape.ChartType = is3D ? ChartType.Surface3D : ChartType.Surface;
        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadPieChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
        shape.ChartType = ChartType.Pie;
        if (el.Name.LocalName == "pie3DChart")
            shape.ThreeDStyle = ChartThreeDStyle.Pie;
        shape.FirstSliceAngleDegrees = ReadFirstSliceAngle(el);
        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadOfPieChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
        shape.ChartType = ChartType.OfPie;
        shape.OfPieType = (el.Element(C + "ofPieType")?.Attribute("val")?.Value ?? "pie") == "bar"
            ? OfPieType.Bar
            : OfPieType.Pie;
        shape.OfPieSplitType = el.Element(C + "splitType")?.Attribute("val")?.Value switch
        {
            "cust" => OfPieSplitType.Custom,
            "percent" => OfPieSplitType.Percent,
            "pos" => OfPieSplitType.Position,
            "val" => OfPieSplitType.Value,
            "auto" => OfPieSplitType.Auto,
            _ => null
        };
        shape.OfPieSplitPosition = ParseDouble(
            el.Element(C + "splitPos")?.Attribute("val")?.Value);
        shape.OfPieSecondPieSizePercent = ParseNullableInt(
            el.Element(C + "secondPieSize")?.Attribute("val")?.Value);
        shape.OfPieCustomPointIndices = (el.Element(C + "custSplit")?.Elements(C + "secondPiePt") ?? Enumerable.Empty<XElement>())
            .Concat(el.Elements(C + "secondPiePt"))
            .Select(point => ParseNullableInt(point.Attribute("val")?.Value))
            .OfType<int>()
            .Distinct()
            .ToList();
        shape.BarGapWidthPercent = ParseNullableInt(
            el.Element(C + "gapWidth")?.Attribute("val")?.Value);
        shape.OfPieSeriesLinesSpecified = el.Element(C + "serLines") is not null;
        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadAreaChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);

        var grouping = el.Element(C + "grouping")?.Attribute("val")?.Value ?? "standard";
        shape.ChartType = grouping == "stacked" ? ChartType.AreaStacked : ChartType.Area;
        if (el.Name.LocalName == "area3DChart")
            shape.ThreeDStyle = ChartThreeDStyle.Area;
        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadDoughnutChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
        shape.ChartType = ChartType.Doughnut;
        shape.FirstSliceAngleDegrees = ReadFirstSliceAngle(el);

        // c:holeSize val= gives the inner radius as a percentage (default 50).
        var holeSizeStr = el.Element(C + "holeSize")?.Attribute("val")?.Value;
        if (holeSizeStr is not null && int.TryParse(holeSizeStr,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var hs))
            shape.DoughnutHolePercent = Math.Clamp(hs, 0, 90);

        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadScatterChartDistinct(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
        shape.ChartType = ChartType.Scatter;

        // c:scatterStyle val= → marker/line/lineMarker/smooth/smoothMarker
        var styleStr = el.Element(C + "scatterStyle")?.Attribute("val")?.Value ?? "lineMarker";
        shape.ScatterStyle = styleStr switch
        {
            "marker"       => ScatterStyle.Marker,
            "line"         => ScatterStyle.Line,
            "lineMarker"   => ScatterStyle.LineMarker,
            "smooth"       => ScatterStyle.Smooth,
            "smoothMarker" => ScatterStyle.SmoothMarker,
            _              => ScatterStyle.LineMarker
        };

        ReadScatterSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadRadarChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
        shape.ChartType = ChartType.Radar;

        var styleStr = el.Element(C + "radarStyle")?.Attribute("val")?.Value ?? "standard";
        shape.RadarStyle = styleStr switch
        {
            "marker" => RadarStyle.Marker,
            "filled" => RadarStyle.Filled,
            _        => RadarStyle.Standard
        };

        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadBubbleChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
        shape.ChartType = ChartType.Bubble;

        // Bubble charts also have a scatterStyle-like attribute (c:bubble3D is irrelevant for us).
        // Treat as SmoothMarker by default; exact style rarely stored explicitly.
        shape.ScatterStyle = ScatterStyle.Marker;
        shape.BubbleScalePercent = ReadBubbleScalePercent(el);
        shape.BubbleSizeRepresents = ReadBubbleSizeRepresentation(el);
        shape.ShowNegativeBubbles = ParseBoolAttr(el.Element(C + "showNegBubbles"));

        ReadBubbleSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static int ReadBubbleScalePercent(XElement bubbleChartEl)
    {
        var parsed = ParseNullableInt(bubbleChartEl.Element(C + "bubbleScale")?.Attribute("val")?.Value);
        return parsed.HasValue ? Math.Clamp(parsed.Value, 0, 300) : 100;
    }

    private static BubbleSizeRepresentation ReadBubbleSizeRepresentation(XElement bubbleChartEl) =>
        bubbleChartEl.Element(C + "sizeRepresents")?.Attribute("val")?.Value == "w"
            ? BubbleSizeRepresentation.Width
            : BubbleSizeRepresentation.Area;

    private static void ReadVaryColors(XElement chartTypeEl, ChartShape shape) =>
        shape.VaryColors = ParseBoolAttr(chartTypeEl.Element(C + "varyColors"));

    // ── Scatter series (x:xVal / c:yVal, no categories axis) ─────────────────

    private static void ReadScatterSeriesFromChart(
        XElement chartEl, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        int seriesIndex = 0;
        foreach (var serEl in OrderedSeriesElements(chartEl))
        {
            var series = new ChartSeries();
            ReadSeriesNameAndColor(serEl, shape, scheme, seriesIndex, series);

            // X values (c:xVal)
            var xValEl = serEl.Element(C + "xVal");
            if (xValEl is not null)
            {
                series.FormulaReferences.XValues = ReadFormula(xValEl.Element(C + "numRef"));
                ReadValues(xValEl, series.XValues);
            }

            // Y values (c:yVal)
            var yValEl = serEl.Element(C + "yVal");
            if (yValEl is not null)
            {
                series.FormulaReferences.YValues = ReadFormula(yValEl.Element(C + "numRef"));
                ReadValues(yValEl, series.Values);
            }

            ReadPointStyles(serEl, scheme, series);

            // If categories are empty but we have X values, build string labels from them
            if (shape.Categories.Count == 0 && series.XValues.Count > 0)
            {
                foreach (var xv in series.XValues)
                    shape.Categories.Add(xv.HasValue
                        ? xv.Value.ToString("G4", System.Globalization.CultureInfo.InvariantCulture)
                        : string.Empty);
            }

            // Per-series data labels override
            var dataLabelsEl = serEl.Element(C + "dLbls");
            series.DataLabels = ReadDataLabels(dataLabelsEl, scheme);
            ReadPointDataLabels(dataLabelsEl, scheme, series);

            shape.Series.Add(series);

            // Record idx→series mapping for secondary-axis detection.
            // c:idx is the OOXML series index; fall back to append position if absent.
            var idxStr = serEl.Element(C + "idx")?.Attribute("val")?.Value;
            int serIdx = idxStr is not null ? ParseInt(idxStr) : seriesIndex;
            idxMap.TryAdd(serIdx, series);

            seriesIndex++;
        }
    }

    // ── Bubble series (c:xVal / c:yVal / c:bubbleSize) ───────────────────────

    private static void ReadBubbleSeriesFromChart(
        XElement chartEl, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        int seriesIndex = 0;
        foreach (var serEl in OrderedSeriesElements(chartEl))
        {
            var series = new ChartSeries();
            ReadSeriesNameAndColor(serEl, shape, scheme, seriesIndex, series);

            // X values (c:xVal)
            var xValEl = serEl.Element(C + "xVal");
            if (xValEl is not null)
            {
                series.FormulaReferences.XValues = ReadFormula(xValEl.Element(C + "numRef"));
                ReadValues(xValEl, series.XValues);
            }

            // Y values (c:yVal)
            var yValEl = serEl.Element(C + "yVal");
            if (yValEl is not null)
            {
                series.FormulaReferences.YValues = ReadFormula(yValEl.Element(C + "numRef"));
                ReadValues(yValEl, series.Values);
            }

            // Bubble sizes (c:bubbleSize)
            var sizeEl = serEl.Element(C + "bubbleSize");
            if (sizeEl is not null)
            {
                series.FormulaReferences.BubbleSizes = ReadFormula(sizeEl.Element(C + "numRef"));
                ReadValues(sizeEl, series.BubbleSizes);
            }

            ReadPointStyles(serEl, scheme, series);

            // Per-series data labels override
            var dataLabelsEl = serEl.Element(C + "dLbls");
            series.DataLabels = ReadDataLabels(dataLabelsEl, scheme);
            ReadPointDataLabels(dataLabelsEl, scheme, series);

            shape.Series.Add(series);

            // Record idx→series mapping for secondary-axis detection.
            var idxStr = serEl.Element(C + "idx")?.Attribute("val")?.Value;
            int serIdx = idxStr is not null ? ParseInt(idxStr) : seriesIndex;
            idxMap.TryAdd(serIdx, series);

            seriesIndex++;
        }
    }

    // ── Shared series header reader ───────────────────────────────────────────

    private static void ReadSeriesNameAndColor(
        XElement serEl, ChartShape shape, PresentationColorScheme scheme,
        int seriesIndex, ChartSeries series)
    {
        // Series name
        var txEl = serEl.Element(C + "tx");
        if (txEl is not null)
        {
            series.FormulaReferences.SeriesName = ReadFormula(txEl.Element(C + "strRef"));
            var nameV = txEl.Element(C + "strRef")
                ?.Element(C + "strCache")
                ?.Elements(C + "pt").FirstOrDefault()
                ?.Element(C + "v")?.Value;
            if (nameV is not null)
                series.Name = nameV;
            else
            {
                var directV = txEl.Element(C + "v")?.Value;
                if (directV is not null) series.Name = directV;
            }
        }
        if (string.IsNullOrWhiteSpace(series.Name))
            series.Name = $"Series {seriesIndex + 1}";

        // Series fill color from c:spPr/a:solidFill
        var spPr = serEl.Element(C + "spPr");
        if (spPr is not null)
            ReadSeriesShapeProperties(spPr, scheme, series);

        series.MarkerStyle = ReadMarkerStyle(serEl.Element(C + "marker"), scheme);
        series.SmoothLine = ParseNullableBoolElement(serEl.Element(C + "smooth"));
        series.InvertIfNegative = ParseNullableBoolElement(serEl.Element(C + "invertIfNegative"));
        series.ErrorBars = ReadErrorBars(serEl.Element(C + "errBars"));
        series.Trendline = ReadTrendline(serEl.Element(C + "trendline"));

        // Fall back to the OOXML series index in the theme accent cycle. Combo-chart
        // plot groups can arrive out of visual order, so the group-local index would
        // restart the palette and assign a duplicate color.
        if (series.FillColor is null)
        {
            int fallbackIndex = ParseNullableInt(serEl.Element(C + "idx")?.Attribute("val")?.Value)
                ?? seriesIndex;
            var slot = AccentSlots[Math.Abs(fallbackIndex) % AccentSlots.Length];
            series.FillColor = new ThemeAwareColor(
                new SrgbColor(0x4F, 0x81, 0xBD),
                new SchemeColorRef { Slot = slot, LumMod = 1.0, LumOff = 0.0 });
        }
    }

    // ── Series parsing ────────────────────────────────────────────────────────

    private static void ReadSeriesFromChart(
        XElement chartEl, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        int seriesIndex = 0;
        foreach (var serEl in OrderedSeriesElements(chartEl))
        {
            var series = new ChartSeries();

            // Series name (c:tx → strRef cache or direct c:v)
            var txEl = serEl.Element(C + "tx");
            if (txEl is not null)
            {
                series.FormulaReferences.SeriesName = ReadFormula(txEl.Element(C + "strRef"));
                var nameV = txEl.Element(C + "strRef")
                    ?.Element(C + "strCache")
                    ?.Elements(C + "pt").FirstOrDefault()
                    ?.Element(C + "v")?.Value;
                if (nameV is not null)
                    series.Name = nameV;
                else
                {
                    var directV = txEl.Element(C + "v")?.Value;
                    if (directV is not null) series.Name = directV;
                }
            }
            if (string.IsNullOrWhiteSpace(series.Name))
                series.Name = $"Series {seriesIndex + 1}";

            // Series fill color from c:spPr/a:solidFill
            var spPr = serEl.Element(C + "spPr");
            if (spPr is not null)
                ReadSeriesShapeProperties(spPr, scheme, series);

            series.MarkerStyle = ReadMarkerStyle(serEl.Element(C + "marker"), scheme);
            series.SmoothLine = ParseNullableBoolElement(serEl.Element(C + "smooth"));
            series.InvertIfNegative = ParseNullableBoolElement(serEl.Element(C + "invertIfNegative"));
            series.ErrorBars = ReadErrorBars(serEl.Element(C + "errBars"));
            series.Trendline = ReadTrendline(serEl.Element(C + "trendline"));

            // Combo-chart plot groups can arrive out of visual order. Use c:idx
            // rather than the group-local position so their theme accent is stable.
            if (series.FillColor is null)
            {
                int fallbackIndex = ParseNullableInt(serEl.Element(C + "idx")?.Attribute("val")?.Value)
                    ?? seriesIndex;
                var slot = AccentSlots[Math.Abs(fallbackIndex) % AccentSlots.Length];
                series.FillColor = new ThemeAwareColor(
                    new SrgbColor(0x4F, 0x81, 0xBD),  // sRGB fallback
                    new SchemeColorRef { Slot = slot, LumMod = 1.0, LumOff = 0.0 });
            }

            // Categories (c:cat or c:xVal) — read only from the first series
            if (shape.Categories.Count == 0)
            {
                var catEl = serEl.Element(C + "cat") ?? serEl.Element(C + "xVal");
                if (catEl is not null)
                    ReadCategories(catEl, shape.Categories);
            }
            var seriesCatEl = serEl.Element(C + "cat") ?? serEl.Element(C + "xVal");
            if (seriesCatEl is not null)
                series.FormulaReferences.Category = ReadCategoryFormula(seriesCatEl);

            // Values (c:val or c:yVal)
            var valEl = serEl.Element(C + "val") ?? serEl.Element(C + "yVal");
            if (valEl is not null)
            {
                series.FormulaReferences.Values = ReadFormula(valEl.Element(C + "numRef"));
                ReadValues(valEl, series.Values);
            }

            // Per-point colors (c:dPt) — mainly used by pie/doughnut charts
            foreach (var dptEl in serEl.Elements(C + "dPt"))
            {
                var idx = ParseInt(dptEl.Element(C + "idx")?.Attribute("val")?.Value);
                var dptSpPr = dptEl.Element(C + "spPr");
                ReadPointColorCompatibility(dptSpPr, scheme, series, idx);

                var pointStyle = ReadPointStyle(dptSpPr, dptEl.Element(C + "marker"), scheme);
                pointStyle = ApplyPointExplosion(dptEl, pointStyle);
                if (pointStyle is not null)
                    series.PointStyles[idx] = pointStyle;
            }

            // Per-series data labels override
            var dataLabelsEl = serEl.Element(C + "dLbls");
            series.DataLabels = ReadDataLabels(dataLabelsEl, scheme);
            ReadPointDataLabels(dataLabelsEl, scheme, series);

            shape.Series.Add(series);

            // Record idx→series mapping for secondary-axis detection.
            // c:idx is the OOXML series index; fall back to append position if absent.
            var idxStr = serEl.Element(C + "idx")?.Attribute("val")?.Value;
            int serIdx = idxStr is not null ? ParseInt(idxStr) : seriesIndex;
            idxMap.TryAdd(serIdx, series);

            seriesIndex++;
        }
    }

    private static IReadOnlyList<XElement> OrderedSeriesElements(XElement chartEl)
    {
        var series = chartEl.Elements(C + "ser").ToList();
        if (series.Count < 2)
            return series;

        // c:order is the authored plot/legend order. PowerPoint can leave the
        // physical c:ser elements in a different order after series edits, so
        // XML position is not a reliable substitute. If a producer omits the
        // token on any series, retain the source order rather than inventing a
        // partial reorder.
        var ordered = series
            .Select((element, sourceIndex) => new
            {
                Element = element,
                SourceIndex = sourceIndex,
                Order = ParseNullableInt(element.Element(C + "order")?.Attribute("val")?.Value),
            })
            .ToList();
        if (ordered.Any(item => item.Order is null))
            return series;

        return ordered
            .OrderBy(item => item.Order)
            .ThenBy(item => item.SourceIndex)
            .Select(item => item.Element)
            .ToList();
    }

    private static ChartErrorBars? ReadErrorBars(XElement? element)
    {
        if (element is null)
            return null;

        var valueType = element.Element(C + "errValType")?.Attribute("val")?.Value;
        var barType = element.Element(C + "errBarType")?.Attribute("val")?.Value;
        var direction = element.Element(C + "errDir")?.Attribute("val")?.Value;
        return new ChartErrorBars
        {
            Direction = direction == "x" ? ChartErrorDirection.X : ChartErrorDirection.Y,
            BarType = barType switch
            {
                "minus" => ChartErrorBarType.Minus,
                "plus" => ChartErrorBarType.Plus,
                _ => ChartErrorBarType.Both,
            },
            ValueType = valueType == "percentage"
                ? ChartErrorValueType.Percentage
                : ChartErrorValueType.Fixed,
            Value = ParseDouble(element.Element(C + "val")?.Attribute("val")?.Value) ?? 0,
            NoEndCap = ParseBoolAttr(element.Element(C + "noEndCap")),
        };
    }

    private static ChartTrendline? ReadTrendline(XElement? element)
    {
        if (element is null)
            return null;

        var type = element.Element(C + "trendlineType")?.Attribute("val")?.Value;
        return new ChartTrendline
        {
            Type = type switch
            {
                "exp" => ChartTrendlineType.Exponential,
                "log" => ChartTrendlineType.Logarithmic,
                "poly" => ChartTrendlineType.Polynomial,
                "power" => ChartTrendlineType.Power,
                "movingAvg" => ChartTrendlineType.MovingAverage,
                _ => ChartTrendlineType.Linear,
            },
            PolynomialOrder = ParseNullableInt(element.Element(C + "order")?.Attribute("val")?.Value),
            MovingAveragePeriod = ParseNullableInt(element.Element(C + "period")?.Attribute("val")?.Value),
            Forward = ParseDouble(element.Element(C + "forward")?.Attribute("val")?.Value),
            Backward = ParseDouble(element.Element(C + "backward")?.Attribute("val")?.Value),
            DisplayEquation = ParseBoolAttr(element.Element(C + "dispEq")),
            DisplayRSquared = ParseBoolAttr(element.Element(C + "dispRSqr")),
        };
    }

    private static void ReadSeriesShapeProperties(
        XElement spPr,
        PresentationColorScheme scheme,
        ChartSeries series)
    {
        var fill = PptxColorReader.TryReadFill(spPr, scheme);
        switch (fill)
        {
            case ShapeFill.Solid solid:
                series.FillColor = solid.Color;
                break;
            case ShapeFill.Gradient gradient:
                series.Fill = gradient;
                break;
            case ShapeFill.Pattern pattern:
                series.Fill = pattern;
                break;
        }

        series.LineStyle = ReadLineStyle(spPr.Element(A + "ln"), scheme);
    }

    private static void ReadPointStyles(
        XElement serEl,
        PresentationColorScheme scheme,
        ChartSeries series)
    {
        foreach (var dptEl in serEl.Elements(C + "dPt"))
        {
            var idx = ParseInt(dptEl.Element(C + "idx")?.Attribute("val")?.Value);
            var dptSpPr = dptEl.Element(C + "spPr");
            ReadPointColorCompatibility(dptSpPr, scheme, series, idx);

            var pointStyle = ReadPointStyle(dptSpPr, dptEl.Element(C + "marker"), scheme);
            pointStyle = ApplyPointExplosion(dptEl, pointStyle);
            if (pointStyle is not null)
                series.PointStyles[idx] = pointStyle;
        }
    }

    private static ChartLineStyle? ReadLineStyle(XElement? lnEl, PresentationColorScheme scheme)
    {
        if (lnEl is null)
            return null;

        var style = new ChartLineStyle();
        if (lnEl.Element(A + "noFill") is not null)
            style.NoFill = true;

        var solidFill = lnEl.Element(A + "solidFill");
        if (solidFill is not null)
            style.Color = PptxColorReader.TryReadColor(solidFill, scheme);

        style.Dash = ReadLineDash(lnEl.Element(A + "prstDash")?.Attribute("val")?.Value);

        if (long.TryParse(lnEl.Attribute("w")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var widthEmu) && widthEmu > 0)
            style.WidthPt = DrawingMlCoordinateUnits.EmuToPoints(widthEmu);

        return style;
    }

    private static OutlineDash ReadLineDash(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "dash" => OutlineDash.Dash,
            "dot" => OutlineDash.Dot,
            "dashdot" => OutlineDash.DashDot,
            "lgdash" => OutlineDash.LongDash,
            "lgdashdot" => OutlineDash.LongDashDot,
            "lgdashdotdot" => OutlineDash.LongDashDotDot,
            "sysdash" => OutlineDash.SystemDash,
            "sysdot" => OutlineDash.SystemDot,
            "sysdashdot" => OutlineDash.SystemDashDot,
            _ => OutlineDash.Solid
        };

    private static ChartMarkerStyle? ReadMarkerStyle(XElement? markerEl, PresentationColorScheme scheme)
    {
        if (markerEl is null)
            return null;

        var style = new ChartMarkerStyle();
        var symbol = ReadMarkerSymbol(markerEl.Element(C + "symbol")?.Attribute("val")?.Value);
        if (symbol.HasValue)
            style.Symbol = symbol.Value;

        if (double.TryParse(markerEl.Element(C + "size")?.Attribute("val")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var sizePt))
            style.SizePt = sizePt;

        var spPr = markerEl.Element(C + "spPr");
        if (spPr is not null)
            ApplyMarkerShapeProperties(spPr, scheme, style);

        return style;
    }

    private static ChartPointStyle? ReadPointStyle(
        XElement? spPr,
        XElement? markerEl,
        PresentationColorScheme scheme)
    {
        ChartPointStyle? pointStyle = null;
        if (spPr is not null)
        {
            pointStyle = new ChartPointStyle();
            var fill = PptxColorReader.TryReadFill(spPr, scheme);
            switch (fill)
            {
                case ShapeFill.Solid solid:
                    pointStyle.FillColor = solid.Color;
                    break;
                case ShapeFill.Gradient gradient:
                    pointStyle.Fill = gradient;
                    break;
                case ShapeFill.Pattern pattern:
                    pointStyle.Fill = pattern;
                    break;
            }

            var lineStyle = ReadLineStyle(spPr.Element(A + "ln"), scheme);
            if (lineStyle is not null)
            {
                pointStyle.StrokeColor = lineStyle.Color;
                pointStyle.StrokeWidthPt = lineStyle.WidthPt;
            }
        }

        var markerStyle = ReadMarkerStyle(markerEl, scheme);
        if (markerStyle is not null)
        {
            pointStyle ??= new ChartPointStyle();
            pointStyle.Marker = markerStyle;
        }

        return pointStyle;
    }

    private static ChartPointStyle? ApplyPointExplosion(
        XElement dptEl,
        ChartPointStyle? pointStyle)
    {
        var explosion = ParseNullableInt(dptEl.Element(C + "explosion")?.Attribute("val")?.Value);
        if (!explosion.HasValue)
            return pointStyle;

        pointStyle ??= new ChartPointStyle();
        pointStyle.ExplosionPercent = Math.Clamp(explosion.Value, 0, 100);
        return pointStyle;
    }

    private static void ApplyMarkerShapeProperties(
        XElement spPr,
        PresentationColorScheme scheme,
        ChartMarkerStyle style)
    {
        if (spPr.Element(A + "noFill") is not null)
            style.NoFill = true;

        var fill = PptxColorReader.TryReadFill(spPr, scheme);
        switch (fill)
        {
            case ShapeFill.Solid solid:
                style.FillColor = solid.Color;
                break;
            case ShapeFill.Gradient gradient:
                style.Fill = gradient;
                break;
            case ShapeFill.Pattern pattern:
                style.Fill = pattern;
                break;
        }

        var line = ReadLineStyle(spPr.Element(A + "ln"), scheme);
        if (line is not null)
        {
            style.NoStroke = line.NoFill;
            style.StrokeColor = line.Color;
            style.StrokeWidthPt = line.WidthPt;
        }
    }

    private static void ReadPointColorCompatibility(
        XElement? spPr,
        PresentationColorScheme scheme,
        ChartSeries series,
        int pointIndex)
    {
        var dptSolid = spPr?.Element(A + "solidFill");
        if (dptSolid is null)
            return;

        var color = PptxColorReader.TryReadColor(dptSolid, scheme);
        if (color is not null)
            series.PointColors[pointIndex] = color;
    }

    private static int? ReadFirstSliceAngle(XElement chartEl)
    {
        var value = ParseNullableInt(chartEl.Element(C + "firstSliceAng")?.Attribute("val")?.Value);
        return value.HasValue ? Math.Clamp(value.Value, 0, 360) : null;
    }

    private static int? ReadBarGapWidth(XElement chartEl)
    {
        var value = ParseNullableInt(chartEl.Element(C + "gapWidth")?.Attribute("val")?.Value);
        return value.HasValue ? Math.Clamp(value.Value, 0, 500) : null;
    }

    private static int? ReadBarOverlap(XElement chartEl)
    {
        var value = ParseNullableInt(chartEl.Element(C + "overlap")?.Attribute("val")?.Value);
        return value.HasValue ? Math.Clamp(value.Value, -100, 100) : null;
    }

    private static int? ReadBarGapDepth(XElement chartEl)
    {
        var value = ParseNullableInt(chartEl.Element(C + "gapDepth")?.Attribute("val")?.Value);
        return value.HasValue ? Math.Clamp(value.Value, 0, 500) : null;
    }

    private static ChartDisplayBlanksAs? ReadDisplayBlanksAs(string? value) =>
        value switch
        {
            "span" => ChartDisplayBlanksAs.Span,
            "gap" => ChartDisplayBlanksAs.Gap,
            "zero" => ChartDisplayBlanksAs.Zero,
            _ => null
        };

    private static ChartMarkerSymbol? ReadMarkerSymbol(string? value) =>
        value switch
        {
            "auto" => ChartMarkerSymbol.Auto,
            "circle" => ChartMarkerSymbol.Circle,
            "dash" => ChartMarkerSymbol.Dash,
            "diamond" => ChartMarkerSymbol.Diamond,
            "dot" => ChartMarkerSymbol.Dot,
            "none" => ChartMarkerSymbol.None,
            "picture" => ChartMarkerSymbol.Picture,
            "plus" => ChartMarkerSymbol.Plus,
            "square" => ChartMarkerSymbol.Square,
            "star" => ChartMarkerSymbol.Star,
            "triangle" => ChartMarkerSymbol.Triangle,
            "x" => ChartMarkerSymbol.X,
            _ => null
        };

    private static string? ReadCategoryFormula(XElement catEl) =>
        ReadFormula(catEl.Element(C + "strRef")) ??
        ReadFormula(catEl.Element(C + "numRef"));

    private static string? ReadFormula(XElement? refEl)
    {
        var value = refEl?.Element(C + "f")?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void ReadCategories(XElement catEl, List<string> categories)
    {
        // strRef → strCache → pt/v
        var strRef = catEl.Element(C + "strRef");
        if (strRef is not null)
        {
            foreach (var pt in strRef.Element(C + "strCache")?.Elements(C + "pt")
                     ?? Enumerable.Empty<XElement>())
                categories.Add(pt.Element(C + "v")?.Value ?? string.Empty);
            return;
        }

        // numRef → numCache → pt/v (numeric categories)
        var numRef = catEl.Element(C + "numRef");
        if (numRef is not null)
        {
            foreach (var pt in numRef.Element(C + "numCache")?.Elements(C + "pt")
                     ?? Enumerable.Empty<XElement>())
                categories.Add(pt.Element(C + "v")?.Value ?? string.Empty);
            return;
        }

        // strLit (literal inline strings)
        var strLit = catEl.Element(C + "strLit");
        if (strLit is not null)
        {
            foreach (var pt in strLit.Elements(C + "pt"))
                categories.Add(pt.Element(C + "v")?.Value ?? string.Empty);
        }
    }

    private static void ReadValues(XElement valEl, List<double?> values)
    {
        var numRef = valEl.Element(C + "numRef");
        if (numRef is not null)
        {
            var cache = numRef.Element(C + "numCache");
            int ptCount = ParseInt(cache?.Element(C + "ptCount")?.Attribute("val")?.Value);

            // Pre-size with nulls. ptCount and idx come straight from the file, so a corrupt or
            // hostile chart can declare int.MaxValue points and exhaust memory before a single
            // value is read; a negative idx would index the list out of range. Clamp both.
            ptCount = Math.Clamp(ptCount, 0, MaxChartSeriesPoints);
            for (int i = 0; i < ptCount; i++) values.Add(null);

            foreach (var pt in cache?.Elements(C + "pt") ?? Enumerable.Empty<XElement>())
            {
                int idx = ParseInt(pt.Attribute("idx")?.Value);
                if (idx < 0 || idx >= MaxChartSeriesPoints) continue;
                while (values.Count <= idx) values.Add(null);
                var v = pt.Element(C + "v")?.Value;
                if (v is not null &&
                    double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var dv))
                    values[idx] = dv;
            }
            return;
        }

        // numLit (inline literal values)
        var numLit = valEl.Element(C + "numLit");
        if (numLit is not null)
        {
            foreach (var pt in numLit.Elements(C + "pt"))
            {
                var v = pt.Element(C + "v")?.Value;
                values.Add(v is not null &&
                    double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var dv)
                    ? dv : null);
            }
        }
    }

    // ── Axis parsing ──────────────────────────────────────────────────────────

    private static void ReadAxis(XElement axEl, ChartAxis axis, PresentationColorScheme scheme)
    {
        axis.Delete = axEl.Element(C + "delete")?.Attribute("val")?.Value is "1" or "true";
        axis.HasMajorGridlines = axEl.Element(C + "majorGridlines") is not null;
        axis.HasMinorGridlines = axEl.Element(C + "minorGridlines") is not null;
        var majorTickMark = ParseTickMark(axEl.Element(C + "majorTickMark"));
        axis.MajorTickMark = majorTickMark.Value;
        axis.RawMajorTickMarkToken = majorTickMark.RawToken;
        var minorTickMark = ParseTickMark(axEl.Element(C + "minorTickMark"));
        axis.MinorTickMark = minorTickMark.Value;
        axis.RawMinorTickMarkToken = minorTickMark.RawToken;
        var tickLabelPosition = ParseTickLabelPosition(axEl.Element(C + "tickLblPos"));
        axis.TickLabelPosition = tickLabelPosition.Value;
        axis.RawTickLabelPositionToken = tickLabelPosition.RawToken;
        axis.LabelOffsetPercent = ParseNullableInt(axEl.Element(C + "lblOffset")?.Attribute("val")?.Value);
        axis.NoMultiLevelLabels = ParseNullableBoolElement(axEl.Element(C + "noMultiLvlLbl"));
        var crossBetween = ParseCrossBetween(axEl.Element(C + "crossBetween"));
        axis.CrossBetween = crossBetween.Value;
        axis.RawCrossBetweenToken = crossBetween.RawToken;
        axis.AutoCrossing = ParseNullableBoolElement(axEl.Element(C + "auto"));
        var labelAlignment = ParseLabelAlignment(axEl.Element(C + "lblAlgn"));
        axis.LabelAlignment = labelAlignment.Value;
        axis.RawLabelAlignmentToken = labelAlignment.RawToken;
        var crosses = ParseAxisCrossing(axEl.Element(C + "crosses"));
        axis.Crosses = crosses.Value;
        axis.RawCrossesToken = crosses.RawToken;
        axis.CrossesAt = ParseDouble(axEl.Element(C + "crossesAt")?.Attribute("val")?.Value);
        var title = axEl.Element(C + "title");
        axis.Title = ReadTitle(title);
        axis.TitleStyle = ReadTitleStyle(title, scheme);

        var numFmt = axEl.Element(C + "numFmt");
        if (numFmt is not null)
        {
            var formatCode = numFmt.Attribute("formatCode")?.Value;
            axis.NumberFormatCode = string.IsNullOrWhiteSpace(formatCode) ? null : formatCode;
            axis.NumberFormatSourceLinked =
                ParseNullableBoolAttr(numFmt.Attribute("sourceLinked")?.Value);
        }

        var displayUnits = axEl.Element(C + "dispUnits");
        var displayUnitToken = displayUnits?.Element(C + "builtInUnit")?.Attribute("val")?.Value;
        var customDisplayUnit = ParseDouble(displayUnits?.Element(C + "customUnit")?.Attribute("val")?.Value);
        if (string.IsNullOrWhiteSpace(displayUnitToken) && customDisplayUnit is > 0)
        {
            axis.DisplayUnit = ChartAxisDisplayUnit.Custom;
            axis.CustomDisplayUnit = customDisplayUnit;
            axis.RawDisplayUnitToken = null;
        }
        else
        {
            axis.DisplayUnit = ParseDisplayUnit(displayUnitToken);
            axis.CustomDisplayUnit = null;
            axis.RawDisplayUnitToken = axis.DisplayUnit == ChartAxisDisplayUnit.Unsupported
                ? displayUnitToken
                : null;
        }

        var scaling = axEl.Element(C + "scaling");
        if (scaling is not null)
        {
            axis.ReverseOrder = scaling.Element(C + "orientation")?.Attribute("val")?.Value
                is "maxMin" or "max-min";
            var minStr = scaling.Element(C + "min")?.Attribute("val")?.Value;
            var maxStr = scaling.Element(C + "max")?.Attribute("val")?.Value;
            if (minStr is not null &&
                double.TryParse(minStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var minV))
                axis.Min = minV;
            if (maxStr is not null &&
                double.TryParse(maxStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxV))
                axis.Max = maxV;
        }

        axis.MajorUnit = ParseDouble(axEl.Element(C + "majorUnit")?.Attribute("val")?.Value);
        axis.MinorUnit = ParseDouble(axEl.Element(C + "minorUnit")?.Attribute("val")?.Value);
    }

    private static (ChartTickMark? Value, string? RawToken) ParseTickMark(XElement? element)
    {
        var token = element?.Attribute("val")?.Value;
        return token switch
        {
            "none"  => (ChartTickMark.None, null),
            "cross" => (ChartTickMark.Cross, null),
            "in"    => (ChartTickMark.In, null),
            "out"   => (ChartTickMark.Out, null),
            null or "" => (null, null),
            _ => (null, token),
        };
    }

    private static ChartAxisDisplayUnit ParseDisplayUnit(string? token) => token switch
    {
        null or "" => ChartAxisDisplayUnit.None,
        "hundreds" => ChartAxisDisplayUnit.Hundreds,
        "thousands" => ChartAxisDisplayUnit.Thousands,
        "tenThousands" => ChartAxisDisplayUnit.TenThousands,
        "hundredThousands" => ChartAxisDisplayUnit.HundredThousands,
        "millions" => ChartAxisDisplayUnit.Millions,
        "tenMillions" => ChartAxisDisplayUnit.TenMillions,
        "hundredMillions" => ChartAxisDisplayUnit.HundredMillions,
        "billions" => ChartAxisDisplayUnit.Billions,
        "trillions" => ChartAxisDisplayUnit.Trillions,
        _ => ChartAxisDisplayUnit.Unsupported,
    };

    private static (ChartTickLabelPosition? Value, string? RawToken) ParseTickLabelPosition(XElement? element)
    {
        var token = element?.Attribute("val")?.Value;
        return token switch
        {
            "none"   => (ChartTickLabelPosition.None, null),
            "low"    => (ChartTickLabelPosition.Low, null),
            "high"   => (ChartTickLabelPosition.High, null),
            "nextTo" => (ChartTickLabelPosition.NextTo, null),
            null or "" => (null, null),
            _ => (null, token),
        };
    }

    private static (ChartCrossBetween? Value, string? RawToken) ParseCrossBetween(XElement? element)
    {
        var token = element?.Attribute("val")?.Value;
        return token switch
        {
            "between" => (ChartCrossBetween.Between, null),
            "midCat"  => (ChartCrossBetween.MidCat, null),
            null or "" => (null, null),
            _ => (null, token),
        };
    }

    private static (ChartLabelAlignment? Value, string? RawToken) ParseLabelAlignment(XElement? element)
    {
        var token = element?.Attribute("val")?.Value;
        return token switch
        {
            "l"   => (ChartLabelAlignment.Left, null),
            "ctr" => (ChartLabelAlignment.Center, null),
            "r"   => (ChartLabelAlignment.Right, null),
            null or "" => (null, null),
            _ => (null, token),
        };
    }

    private static (ChartAxisCrossing? Value, string? RawToken) ParseAxisCrossing(XElement? element)
    {
        var token = element?.Attribute("val")?.Value;
        return token switch
        {
            "autoZero" => (ChartAxisCrossing.AutoZero, null),
            "min"      => (ChartAxisCrossing.Min, null),
            "max"      => (ChartAxisCrossing.Max, null),
            null or "" => (null, null),
            _ => (null, token),
        };
    }

    // ── Data-label parsing ─────────────────────────────────────────────────────

    private static ChartDataLabels? ReadDataLabels(
        XElement? dLblsEl,
        PresentationColorScheme scheme)
        => ReadDataLabelValues(dLblsEl, scheme, allowEmpty: false);

    private static void ReadPointDataLabels(
        XElement? dLblsEl,
        PresentationColorScheme scheme,
        ChartSeries series)
    {
        if (dLblsEl is null)
            return;

        foreach (var pointLabelEl in dLblsEl.Elements(C + "dLbl"))
        {
            var indexText = pointLabelEl.Element(C + "idx")?.Attribute("val")?.Value;
            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index < 0)
                continue;

            var labels = ReadDataLabelValues(pointLabelEl, scheme, allowEmpty: true);
            if (labels is null)
                continue;

            if (!series.PointStyles.TryGetValue(index, out var style))
                style = new ChartPointStyle();
            style.DataLabels = labels;
            series.PointStyles[index] = style;
        }
    }

    private static ChartDataLabels? ReadDataLabelValues(
        XElement? dLblsEl,
        PresentationColorScheme scheme,
        bool allowEmpty)
    {
        if (dLblsEl is null) return null;

        // Check if labels are explicitly turned off (c:showVal val="0" and nothing else)
        bool showVal     = ParseBoolAttr(dLblsEl.Element(C + "showVal"));
        bool showPct     = ParseBoolAttr(dLblsEl.Element(C + "showPercent"));
        bool showCat     = ParseBoolAttr(dLblsEl.Element(C + "showCatName"));
        bool showSer     = ParseBoolAttr(dLblsEl.Element(C + "showSerName"));
        bool showLegend  = ParseBoolAttr(dLblsEl.Element(C + "showLegendKey"));
        bool showBubble  = ParseBoolAttr(dLblsEl.Element(C + "showBubbleSize"));
        bool? showLeader = dLblsEl.Element(C + "showLeaderLines") is { } leaderEl
            ? ParseBoolAttr(leaderEl)
            : null;
        bool? deleted = dLblsEl.Element(C + "delete") is { } deleteEl
            ? ParseBoolAttr(deleteEl)
            : null;

        // If nothing is shown this is a no-op element — return null to keep model clean.
        if (!allowEmpty && !deleted.HasValue && !showVal && !showPct && !showCat && !showSer && !showLegend && !showBubble && !showLeader.HasValue)
            return null;

        var posStr = dLblsEl.Element(C + "dLblPos")?.Attribute("val")?.Value;
        var numFmt = dLblsEl.Element(C + "numFmt")?.Attribute("formatCode")?.Value;

        return new ChartDataLabels
        {
            Delete            = deleted,
            ShowValue        = showVal,
            ShowPercent      = showPct,
            ShowCategoryName = showCat,
            ShowSeriesName   = showSer,
            ShowLegendKey    = showLegend,
            ShowBubbleSize   = showBubble,
            ShowLeaderLines  = showLeader,
            NumberFormat     = string.IsNullOrEmpty(numFmt) ? null : numFmt,
            Separator        = dLblsEl.Element(C + "separator")?.Value,
            TextStyle        = ReadChartTextStyle(dLblsEl.Element(C + "txPr"), scheme),
            Position         = posStr switch
            {
                "ctr"      => DataLabelPosition.Center,
                "inEnd"    => DataLabelPosition.InsideEnd,
                "outEnd"   => DataLabelPosition.OutsideEnd,
                "inBase"   => DataLabelPosition.InsideBase,
                "bestFit"  => DataLabelPosition.BestFit,
                "t"        => DataLabelPosition.Above,
                "b"        => DataLabelPosition.Below,
                "l"        => DataLabelPosition.Left,
                "r"        => DataLabelPosition.Right,
                _          => (DataLabelPosition?)null
            }
        };
    }

    private static void ApplyPowerPointPercentStackedDataLabelDefaults(
        XElement? chartTypeEl,
        XElement? dataLabelsEl,
        ChartDataLabels? labels)
    {
        if (chartTypeEl?.Name != C + "barChart" ||
            chartTypeEl.Element(C + "grouping")?.Attribute("val")?.Value != "percentStacked" ||
            dataLabelsEl is null ||
            labels is not { ShowValue: true, ShowPercent: true, ShowSeriesName: false, ShowCategoryName: false } ||
            dataLabelsEl.Element(C + "showSerName") is not null ||
            dataLabelsEl.Element(C + "showCatName") is not null)
        {
            return;
        }

        // PowerPoint expands this sparse dLbls form into series/category/value labels
        // and suppresses the percentage token when laying out a 100%-stacked chart.
        labels.ShowSeriesName = true;
        labels.ShowCategoryName = true;
        labels.ShowPercent = false;
        labels.ShowLegendKey = true;
        labels.Separator ??= ", ";
    }

    private static void ApplyPowerPointPiePercentDataLabelDefaults(
        XElement? chartTypeEl,
        XElement? dataLabelsEl,
        ChartDataLabels? labels)
    {
        if (chartTypeEl?.Name == C + "pieChart" &&
            dataLabelsEl is not null &&
            labels?.ShowPercent == true)
        {
            labels.Separator ??= ", ";
        }

        if (chartTypeEl?.Name != C + "pieChart" ||
            dataLabelsEl is null ||
            dataLabelsEl.Element(C + "showVal") is not null ||
            labels is not { ShowValue: false, ShowPercent: true, ShowSeriesName: false, ShowCategoryName: false })
        {
            return;
        }

        // PowerPoint exposes the sparse pie form (showPercent without showVal)
        // as value-and-percent labels when it opens an imported presentation.
        labels.ShowValue = true;
        labels.Separator ??= ", ";
    }

    private static void ApplyPowerPointAutomaticTitleDefault(
        XElement chartEl,
        ChartShape shape)
    {
        if (shape.Title is not null ||
            chartEl.Element(C + "autoTitleDeleted")?.Attribute("val")?.Value != "0" ||
            shape.Series.Count != 1 ||
            string.IsNullOrWhiteSpace(shape.Series[0].Name))
        {
            return;
        }

        // PowerPoint renders a single series name as an automatic chart title
        // when autoTitleDeleted is explicitly false.
        shape.Title = shape.Series[0].Name;
        shape.HasAutomaticTitle = true;
    }

    private static ChartDataTableSettings? ReadDataTable(XElement? dTableEl, PresentationColorScheme scheme)
    {
        if (dTableEl is null) return null;

        return new ChartDataTableSettings
        {
            ShowHorizontalBorder = ParseBoolAttr(dTableEl.Element(C + "showHorzBorder")),
            ShowVerticalBorder   = ParseBoolAttr(dTableEl.Element(C + "showVertBorder")),
            ShowOutlineBorder    = ParseBoolAttr(dTableEl.Element(C + "showOutline")),
            ShowLegendKeys       = ParseBoolAttr(dTableEl.Element(C + "showKeys")),
            BackgroundFill       = ReadDataTableBackgroundFill(dTableEl, scheme),
            BorderOutline        = ReadDataTableBorderOutline(dTableEl, scheme),
            TextStyle            = ReadDataTableTextStyle(dTableEl, scheme),
        };
    }

    private static ChartManualLayout? ReadManualLayout(XElement? layoutEl)
    {
        var manualLayoutEl = layoutEl?.Element(C + "manualLayout");
        if (manualLayoutEl is null)
            return null;

        var xModeToken = manualLayoutEl.Element(C + "xMode")?.Attribute("val")?.Value;
        var yModeToken = manualLayoutEl.Element(C + "yMode")?.Attribute("val")?.Value;
        var widthModeToken = manualLayoutEl.Element(C + "wMode")?.Attribute("val")?.Value;
        var heightModeToken = manualLayoutEl.Element(C + "hMode")?.Attribute("val")?.Value;
        var xMode = ReadManualLayoutMode(xModeToken, out var rawXModeToken);
        var yMode = ReadManualLayoutMode(yModeToken, out var rawYModeToken);
        var widthMode = ReadManualLayoutMode(widthModeToken, out var rawWidthModeToken);
        var heightMode = ReadManualLayoutMode(heightModeToken, out var rawHeightModeToken);

        var layout = new ChartManualLayout
        {
            LayoutTarget = EmptyToNull(manualLayoutEl.Element(C + "layoutTarget")?.Attribute("val")?.Value),
            XMode = xMode,
            YMode = yMode,
            WidthMode = widthMode,
            HeightMode = heightMode,
            RawXModeToken = rawXModeToken,
            RawYModeToken = rawYModeToken,
            RawWidthModeToken = rawWidthModeToken,
            RawHeightModeToken = rawHeightModeToken,
            X = ParseDouble(manualLayoutEl.Element(C + "x")?.Attribute("val")?.Value),
            Y = ParseDouble(manualLayoutEl.Element(C + "y")?.Attribute("val")?.Value),
            Width = ParseDouble(manualLayoutEl.Element(C + "w")?.Attribute("val")?.Value),
            Height = ParseDouble(manualLayoutEl.Element(C + "h")?.Attribute("val")?.Value),
        };

        return layout.LayoutTarget is not null ||
               layout.X.HasValue ||
               layout.Y.HasValue ||
               layout.Width.HasValue ||
               layout.Height.HasValue ||
               layout.XMode != ChartManualLayoutMode.Factor ||
               layout.YMode != ChartManualLayoutMode.Factor ||
               layout.WidthMode != ChartManualLayoutMode.Factor ||
               layout.HeightMode != ChartManualLayoutMode.Factor
            ? layout
            : null;
    }

    private static ChartManualLayoutMode ReadManualLayoutMode(string? value, out string? rawToken)
    {
        rawToken = null;
        return value switch
        {
            null or "factor" => ChartManualLayoutMode.Factor,
            "edge" => ChartManualLayoutMode.Edge,
            _ => UnsupportedMode(value, out rawToken),
        };
    }

    private static ChartManualLayoutMode UnsupportedMode(string value, out string rawToken)
    {
        rawToken = value;
        return ChartManualLayoutMode.Unsupported;
    }

    private static ShapeFill? ReadDataTableBackgroundFill(XElement dTableEl, PresentationColorScheme scheme)
    {
        var spPr = dTableEl.Element(C + "spPr");
        return spPr is null ? null : PptxColorReader.TryReadFill(spPr, scheme);
    }

    private static ShapeOutline? ReadDataTableBorderOutline(XElement dTableEl, PresentationColorScheme scheme)
    {
        // Preserve every outline kind TryReadOutline can produce, including
        // ShapeOutline.GradientVisible (a:ln/a:gradFill). Previously only
        // Visible/None were kept here and a gradient data-table border was
        // silently discarded, causing it to be replaced by the default gray
        // outline on round-trip.
        return PptxColorReader.TryReadOutline(
            dTableEl.Element(C + "spPr")?.Element(A + "ln"),
            scheme);
    }

    private static ChartTextStyle? ReadDataTableTextStyle(XElement dTableEl, PresentationColorScheme scheme)
    {
        return ReadChartTextStyle(dTableEl.Element(C + "txPr"), scheme);
    }

    private static ChartTextStyle? ReadChartTextStyle(XElement? txPrEl, PresentationColorScheme scheme)
    {
        var defRPr = txPrEl
            ?.Element(A + "p")
            ?.Element(A + "pPr")
            ?.Element(A + "defRPr");
        if (defRPr is null)
            return null;

        double? fontSizePt = null;
        if (int.TryParse(defRPr.Attribute("sz")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sz)
            && sz > 0)
        {
            fontSizePt = sz / 100.0;
        }

        bool? bold = ParseNullableBoolAttr(defRPr.Attribute("b")?.Value);
        bool? italic = ParseNullableBoolAttr(defRPr.Attribute("i")?.Value);
        var color = PptxColorReader.TryReadColor(defRPr.Element(A + "solidFill"), scheme);
        string? fontFamily = defRPr.Element(A + "latin")?.Attribute("typeface")?.Value;

        return fontSizePt.HasValue || bold.HasValue || italic.HasValue || color is not null || fontFamily is not null
            ? new ChartTextStyle
            {
                FontSizePt = fontSizePt,
                Bold       = bold,
                Italic     = italic,
                Color      = color,
                FontFamily = fontFamily,
            }
            : null;
    }

    private static ChartTextStyle? ReadTitleStyle(XElement? titleEl, PresentationColorScheme scheme)
    {
        var rich = titleEl?.Element(C + "tx")?.Element(C + "rich");
        if (rich is null)
            return null;

        var style = ReadChartTextStyle(rich, scheme);
        if (style is not null)
            return style;

        var runPr = rich.Descendants(A + "rPr").FirstOrDefault();
        if (runPr is null)
            return null;

        var color = PptxColorReader.TryReadColor(runPr.Element(A + "solidFill"), scheme);
        var family = runPr.Element(A + "latin")?.Attribute("typeface")?.Value;
        double? size = int.TryParse(runPr.Attribute("sz")?.Value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var sz) && sz > 0 ? sz / 100.0 : null;
        var bold = ParseNullableBoolAttr(runPr.Attribute("b")?.Value);
        var italic = ParseNullableBoolAttr(runPr.Attribute("i")?.Value);
        return size.HasValue || bold.HasValue || italic.HasValue || color is not null || family is not null
            ? new ChartTextStyle { FontSizePt = size, Bold = bold, Italic = italic, Color = color, FontFamily = family }
            : null;
    }

    private static bool? ParseNullableBoolAttr(string? value) =>
        value switch
        {
            null => null,
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null
        };

    private static bool? ParseNullableBoolElement(XElement? el)
    {
        if (el is null) return null;
        return ParseNullableBoolAttr(el.Attribute("val")?.Value) ?? true;
    }

    private static bool ParseBoolAttr(XElement? el)
    {
        if (el is null) return false;
        var val = el.Attribute("val")?.Value;
        // No val attribute = true (OOXML boolean element default)
        return val is null || val == "1" || val == "true";
    }

    private static Chart3DView? ReadView3D(XElement? view3DEl)
    {
        if (view3DEl is null) return null;

        var view = new Chart3DView
        {
            RotationX = ParseNullableInt(view3DEl.Element(C + "rotX")?.Attribute("val")?.Value),
            HeightPercent = ParseNullableInt(view3DEl.Element(C + "hPercent")?.Attribute("val")?.Value),
            RotationY = ParseNullableInt(view3DEl.Element(C + "rotY")?.Attribute("val")?.Value),
            DepthPercent = ParseNullableInt(view3DEl.Element(C + "depthPercent")?.Attribute("val")?.Value),
            RightAngleAxes = ParseNullableBoolElement(view3DEl.Element(C + "rAngAx")),
            Perspective = ParseNullableInt(view3DEl.Element(C + "perspective")?.Attribute("val")?.Value),
        };

        return view.RotationX is null
            && view.HeightPercent is null
            && view.RotationY is null
            && view.DepthPercent is null
            && view.RightAngleAxes is null
            && view.Perspective is null
                ? null
                : view;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int ParseInt(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static int? ParseNullableInt(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? ParseDouble(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
