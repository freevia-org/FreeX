using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetChartWriter
{
    private const string ChartExRelationshipType = "http://schemas.microsoft.com/office/2014/relationships/chartEx";
    private const string ChartExDrawingUri = "http://schemas.microsoft.com/office/drawing/2014/chartex";
    private const string ChartExCompatNamespace = "http://schemas.microsoft.com/office/drawing/2015/9/8/chartex";
    private const string ChartExStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartStyle";
    private const string ChartExColorStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartColorStyle";
    private const string ChartExStyleContentType = "application/vnd.ms-office.chartstyle+xml";
    private const string ChartExColorStyleContentType = "application/vnd.ms-office.chartcolorstyle+xml";

    public static bool HasSupportedCharts(Workbook workbook, Func<ChartModel, bool> isSupportedChart)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (HasSupportedCharts(sheet, isSupportedChart))
                return true;
        }

        return false;
    }

    public static bool HasSupportedCharts(Sheet sheet, Func<ChartModel, bool> isSupportedChart)
    {
        foreach (var chart in sheet.Charts)
        {
            if (isSupportedChart(chart))
                return true;
        }

        return false;
    }

    public static void Save(
        Stream xlsxStream,
        Workbook workbook,
        Func<ChartModel, bool> isSupportedChart,
        Func<ChartModel, Sheet, XDocument> createChartXml,
        Func<ChartModel, string> getChartContentType,
        Func<ChartModel, string> getChartRelationshipType)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var relTargets = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => XlsxPackagePath.NormalizeWorkbookTarget(e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);
        var drawingIndex = 1;
        var chartIndex = 1;
        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId))
                continue;
            if (!sheetsByName.TryGetValue(name, out var sheet))
                continue;
            var supportedCharts = sheet.Charts
                .Where(isSupportedChart)
                .ToList();
            if (supportedCharts.Count == 0)
                continue;
            if (!relTargets.TryGetValue(relId, out var worksheetPath))
                continue;

            WriteWorksheetCharts(archive, worksheetPath, sheet, supportedCharts, drawingIndex++, ref chartIndex, createChartXml, getChartContentType, getChartRelationshipType);
        }
    }

    private static void WriteWorksheetCharts(
        ZipArchive archive,
        string worksheetPath,
        Sheet sheet,
        IReadOnlyList<ChartModel> charts,
        int drawingIndex,
        ref int chartIndex,
        Func<ChartModel, Sheet, XDocument> createChartXml,
        Func<ChartModel, string> getChartContentType,
        Func<ChartModel, string> getChartRelationshipType)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace chartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        XNamespace markupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";

        var drawingPath = $"xl/drawings/drawing{drawingIndex}.xml";
        var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
        archive.GetEntry(drawingPath)?.Delete();
        archive.GetEntry(drawingRelsPath)?.Delete();

        var drawingRelsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        var anchors = new List<XElement>();
        var chartContentTypes = new Dictionary<int, string>();
        foreach (var chart in charts)
        {
            var currentChartIndex = chartIndex++;
            chartContentTypes[currentChartIndex] = getChartContentType(chart);
            var chartPath = $"xl/charts/chart{currentChartIndex}.xml";
            var chartRelationshipType = getChartRelationshipType(chart);
            var isChartEx = IsChartExRelationship(chartRelationshipType);
            var stylePath = isChartEx ? $"xl/charts/style{currentChartIndex}.xml" : null;
            var colorsPath = isChartEx ? $"xl/charts/colors{currentChartIndex}.xml" : null;
            archive.GetEntry(chartPath)?.Delete();
            var chartEntry = archive.CreateEntry(chartPath);
            using (var chartStream = chartEntry.Open())
                createChartXml(chart, sheet).Save(chartStream);
            if (isChartEx)
                WriteChartExStyleParts(archive, stylePath!, colorsPath!);
            WriteChartRelationships(archive, chartPath, chart, packageRelNs, stylePath, colorsPath);

            var chartRelId = $"rIdFreeXChart{currentChartIndex}";
            drawingRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", chartRelId),
                new XAttribute("Type", chartRelationshipType),
                new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(drawingPath, chartPath))));

            anchors.Add(ToChartAnchor(chart, sheet, currentChartIndex, chartRelId, chartRelationshipType, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs));
        }

        XlsxPackageXmlEditor.ReplaceXml(archive, drawingPath, new XDocument(
            new XElement(spreadsheetDrawingNs + "wsDr",
                new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XAttribute(XNamespace.Xmlns + "c", chartNs),
                new XAttribute(XNamespace.Xmlns + "cx", chartExNs),
                new XAttribute(XNamespace.Xmlns + "r", relNs),
                anchors)));
        XlsxPackageXmlEditor.ReplaceXml(archive, drawingRelsPath, drawingRelsXml);

        XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{drawingPath}", "application/vnd.openxmlformats-officedocument.drawing+xml");
        foreach (var (index, contentType) in chartContentTypes)
        {
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/xl/charts/chart{index}.xml", contentType);
            if (string.Equals(contentType, "application/vnd.ms-office.chartex+xml", StringComparison.OrdinalIgnoreCase))
            {
                XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/xl/charts/style{index}.xml", ChartExStyleContentType);
                XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/xl/charts/colors{index}.xml", ChartExColorStyleContentType);
            }
        }

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var worksheetRelsXml = archive.GetEntry(relsPath) is { } relsEntry
            ? XlsxPackageXmlEditor.LoadXml(relsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));

        var drawingRelId = XlsxPackageXmlEditor.NextRelationshipId(worksheetRelsXml, packageRelNs);
        worksheetRelsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", drawingRelId),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
            new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(worksheetPath, drawingPath))));
        XlsxPackageXmlEditor.ReplaceXml(archive, relsPath, worksheetRelsXml);

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        var root = worksheetXml.Root;
        if (root is null)
            return;

        root.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
        XlsxWorksheetDrawingPlacement.SetWorksheetDrawing(root, worksheetNs, relNs, drawingRelId);
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
    }

    private static void WriteChartRelationships(
        ZipArchive archive,
        string chartPath,
        ChartModel chart,
        XNamespace packageRelNs,
        string? chartExStylePath,
        string? chartExColorsPath)
    {
        var relationships = new List<XElement>();
        if (chart.ExternalData is { } externalData &&
            !string.IsNullOrWhiteSpace(externalData.RelationshipId) &&
            !string.IsNullOrWhiteSpace(externalData.RelationshipType) &&
            !string.IsNullOrWhiteSpace(externalData.Target))
        {
            relationships.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", externalData.RelationshipId),
                new XAttribute("Type", externalData.RelationshipType),
                new XAttribute("Target", externalData.Target),
                string.IsNullOrWhiteSpace(externalData.TargetMode)
                    ? null
                    : new XAttribute("TargetMode", externalData.TargetMode)));
        }

        if (chart.UserShapes is { } userShapes &&
            !string.IsNullOrWhiteSpace(userShapes.RelationshipId) &&
            !string.IsNullOrWhiteSpace(userShapes.RelationshipType) &&
            !string.IsNullOrWhiteSpace(userShapes.Target))
        {
            relationships.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", userShapes.RelationshipId),
                new XAttribute("Type", userShapes.RelationshipType),
                new XAttribute("Target", userShapes.Target),
                string.IsNullOrWhiteSpace(userShapes.TargetMode)
                    ? null
                    : new XAttribute("TargetMode", userShapes.TargetMode)));
        }

        if (!string.IsNullOrWhiteSpace(chartExStylePath))
        {
            var relsXml = new XDocument(new XElement(packageRelNs + "Relationships", relationships));
            relationships.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", XlsxPackageXmlEditor.NextRelationshipId(relsXml, packageRelNs)),
                new XAttribute("Type", ChartExStyleRelationshipType),
                new XAttribute("Target", GetChartSiblingRelationshipTarget(chartExStylePath))));
        }

        if (!string.IsNullOrWhiteSpace(chartExColorsPath))
        {
            var relsXml = new XDocument(new XElement(packageRelNs + "Relationships", relationships));
            relationships.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", XlsxPackageXmlEditor.NextRelationshipId(relsXml, packageRelNs)),
                new XAttribute("Type", ChartExColorStyleRelationshipType),
                new XAttribute("Target", GetChartSiblingRelationshipTarget(chartExColorsPath))));
        }

        if (relationships.Count == 0)
        {
            return;
        }

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(chartPath);
        XlsxPackageXmlEditor.ReplaceXml(archive, relsPath, new XDocument(new XElement(packageRelNs + "Relationships", relationships)));
    }

    private static void WriteChartExStyleParts(ZipArchive archive, string stylePath, string colorsPath)
    {
        archive.GetEntry(stylePath)?.Delete();
        archive.GetEntry(colorsPath)?.Delete();
        XlsxPackageXmlEditor.ReplaceXml(archive, stylePath, ToChartExStyleXml());
        XlsxPackageXmlEditor.ReplaceXml(archive, colorsPath, ToChartExColorStyleXml());
    }

    private static XDocument ToChartExColorStyleXml()
    {
        XNamespace chartStyleNs = "http://schemas.microsoft.com/office/drawing/2012/chartStyle";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        return new XDocument(
            new XElement(chartStyleNs + "colorStyle",
                new XAttribute(XNamespace.Xmlns + "cs", chartStyleNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XAttribute("meth", "cycle"),
                new XAttribute("id", "10"),
                Enumerable.Range(1, 6).Select(index =>
                    new XElement(drawingNs + "schemeClr", new XAttribute("val", $"accent{index}"))),
                new XElement(chartStyleNs + "variation"),
                new XElement(chartStyleNs + "variation", new XElement(drawingNs + "lumMod", new XAttribute("val", "60000"))),
                new XElement(chartStyleNs + "variation",
                    new XElement(drawingNs + "lumMod", new XAttribute("val", "80000")),
                    new XElement(drawingNs + "lumOff", new XAttribute("val", "20000"))),
                new XElement(chartStyleNs + "variation", new XElement(drawingNs + "lumMod", new XAttribute("val", "80000"))),
                new XElement(chartStyleNs + "variation",
                    new XElement(drawingNs + "lumMod", new XAttribute("val", "60000")),
                    new XElement(drawingNs + "lumOff", new XAttribute("val", "40000"))),
                new XElement(chartStyleNs + "variation", new XElement(drawingNs + "lumMod", new XAttribute("val", "50000"))),
                new XElement(chartStyleNs + "variation",
                    new XElement(drawingNs + "lumMod", new XAttribute("val", "70000")),
                    new XElement(drawingNs + "lumOff", new XAttribute("val", "30000"))),
                new XElement(chartStyleNs + "variation", new XElement(drawingNs + "lumMod", new XAttribute("val", "70000"))),
                new XElement(chartStyleNs + "variation",
                    new XElement(drawingNs + "lumMod", new XAttribute("val", "50000")),
                    new XElement(drawingNs + "lumOff", new XAttribute("val", "50000")))));
    }

    private static XDocument ToChartExStyleXml()
    {
        XNamespace chartStyleNs = "http://schemas.microsoft.com/office/drawing/2012/chartStyle";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        return new XDocument(
            new XElement(chartStyleNs + "chartStyle",
                new XAttribute(XNamespace.Xmlns + "cs", chartStyleNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XAttribute("id", "410"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "axisTitle"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "categoryAxis"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "chartArea", new XAttribute("mods", "allowNoFillOverride allowNoLineOverride")),
                ToChartStyleEntry(chartStyleNs, drawingNs, "dataLabel"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "dataLabelCallout"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "dataPoint", includeAutoStyleColor: true),
                ToChartStyleEntry(chartStyleNs, drawingNs, "dataPoint3D", includeAutoStyleColor: true),
                ToChartStyleEntry(chartStyleNs, drawingNs, "dataPointLine", includeAutoStyleColor: true),
                ToChartStyleEntry(chartStyleNs, drawingNs, "dataPointMarker", includeAutoStyleColor: true),
                new XElement(chartStyleNs + "dataPointMarkerLayout",
                    new XAttribute("symbol", "circle"),
                    new XAttribute("size", "5")),
                ToChartStyleEntry(chartStyleNs, drawingNs, "dataPointWireframe", includeAutoStyleColor: true),
                ToChartStyleEntry(chartStyleNs, drawingNs, "dataTable"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "downBar"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "dropLine"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "errorBar"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "floor"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "gridlineMajor"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "gridlineMinor"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "hiLoLine"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "leaderLine"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "legend"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "plotArea"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "plotArea3D"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "seriesAxis"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "seriesLine"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "title"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "trendline"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "trendlineLabel"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "upBar"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "valueAxis"),
                ToChartStyleEntry(chartStyleNs, drawingNs, "wall")));
    }

    private static XElement ToChartStyleEntry(
        XNamespace chartStyleNs,
        XNamespace drawingNs,
        string name,
        XAttribute? extraAttribute = null,
        bool includeAutoStyleColor = false) =>
        new(chartStyleNs + name,
            extraAttribute,
            new XElement(chartStyleNs + "lnRef", new XAttribute("idx", "0")),
            new XElement(chartStyleNs + "fillRef",
                new XAttribute("idx", "0"),
                includeAutoStyleColor
                    ? new XElement(chartStyleNs + "styleClr", new XAttribute("val", "auto"))
                    : null),
            new XElement(chartStyleNs + "effectRef", new XAttribute("idx", "0")),
            new XElement(chartStyleNs + "fontRef",
                new XAttribute("idx", "minor"),
                new XElement(drawingNs + "schemeClr", new XAttribute("val", "tx1"))));

    private static XElement ToChartAnchor(
        ChartModel chart,
        Sheet sheet,
        int chartIndex,
        string chartRelId,
        string chartRelationshipType,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace chartExNs,
        XNamespace relNs,
        XNamespace markupCompatNs)
    {
        if (IsChartExRelationship(chartRelationshipType))
            return ToTwoCellChartAnchor(chart, sheet, chartIndex, chartRelId, chartRelationshipType, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs);

        return chart.DrawingAnchorKind switch
        {
            ChartDrawingAnchorKind.OneCell => ToOneCellChartAnchor(chart, sheet, chartIndex, chartRelId, chartRelationshipType, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs),
            ChartDrawingAnchorKind.TwoCell => ToTwoCellChartAnchor(chart, sheet, chartIndex, chartRelId, chartRelationshipType, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs),
            _ => ToAbsoluteChartAnchor(chart, chartIndex, chartRelId, chartRelationshipType, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs)
        };
    }

    private static XElement ToAbsoluteChartAnchor(
        ChartModel chart,
        int chartIndex,
        string chartRelId,
        string chartRelationshipType,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace chartExNs,
        XNamespace relNs,
        XNamespace markupCompatNs) =>
        new(spreadsheetDrawingNs + "absoluteAnchor",
            new XElement(spreadsheetDrawingNs + "pos",
                new XAttribute("x", PixelsToEmus(chart.Left)),
                new XAttribute("y", PixelsToEmus(chart.Top))),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", PixelsToEmus(chart.Width)),
                new XAttribute("cy", PixelsToEmus(chart.Height))),
            ToChartFrameOrAlternateContent(chart, chartIndex, chartRelId, chartRelationshipType, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs),
            new XElement(spreadsheetDrawingNs + "clientData"));

    private static XElement ToOneCellChartAnchor(
        ChartModel chart,
        Sheet sheet,
        int chartIndex,
        string chartRelId,
        string chartRelationshipType,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace chartExNs,
        XNamespace relNs,
        XNamespace markupCompatNs)
    {
        var from = ToAnchorMarker(sheet, chart.Left, chart.Top);
        return new XElement(spreadsheetDrawingNs + "oneCellAnchor",
            ToAnchorMarkerXml("from", from, spreadsheetDrawingNs),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", PixelsToEmus(chart.Width)),
                new XAttribute("cy", PixelsToEmus(chart.Height))),
            ToChartFrameOrAlternateContent(chart, chartIndex, chartRelId, chartRelationshipType, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs),
            new XElement(spreadsheetDrawingNs + "clientData"));
    }

    private static XElement ToTwoCellChartAnchor(
        ChartModel chart,
        Sheet sheet,
        int chartIndex,
        string chartRelId,
        string chartRelationshipType,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace chartExNs,
        XNamespace relNs,
        XNamespace markupCompatNs)
    {
        var from = ToAnchorMarker(sheet, chart.Left, chart.Top);
        var to = ToAnchorMarker(sheet, chart.Left + chart.Width, chart.Top + chart.Height);
        return new XElement(spreadsheetDrawingNs + "twoCellAnchor",
            ToAnchorMarkerXml("from", from, spreadsheetDrawingNs),
            ToAnchorMarkerXml("to", to, spreadsheetDrawingNs),
            ToChartFrameOrAlternateContent(chart, chartIndex, chartRelId, chartRelationshipType, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs),
            new XElement(spreadsheetDrawingNs + "clientData"));
    }

    private static XElement ToChartFrameOrAlternateContent(
        ChartModel chart,
        int chartIndex,
        string chartRelId,
        string chartRelationshipType,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace chartExNs,
        XNamespace relNs,
        XNamespace markupCompatNs)
    {
        var graphicFrame = ToChartGraphicFrame(chart, chartIndex, chartRelId, chartRelationshipType, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs);
        if (!IsChartExRelationship(chartRelationshipType))
            return graphicFrame;

        XNamespace chartExCompatNs = ChartExCompatNamespace;
        return new XElement(markupCompatNs + "AlternateContent",
            new XAttribute(XNamespace.Xmlns + "mc", markupCompatNs),
            new XElement(markupCompatNs + "Choice",
                new XAttribute(XNamespace.Xmlns + "cx1", chartExCompatNs),
                new XAttribute("Requires", "cx1"),
                graphicFrame),
            new XElement(markupCompatNs + "Fallback",
                ToChartExFallbackShape(chart, spreadsheetDrawingNs, drawingNs)));
    }

    private static XElement ToChartGraphicFrame(
        ChartModel chart,
        int chartIndex,
        string chartRelId,
        string chartRelationshipType,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace chartExNs,
        XNamespace relNs)
    {
        var isChartEx = IsChartExRelationship(chartRelationshipType);
        return
        new(spreadsheetDrawingNs + "graphicFrame",
            isChartEx ? new XAttribute("macro", "") : null,
            new XElement(spreadsheetDrawingNs + "nvGraphicFramePr",
                new XElement(spreadsheetDrawingNs + "cNvPr",
                    new XAttribute("id", chartIndex + 1),
                    new XAttribute("name", DrawingName(chart.Name, $"Chart {chartIndex}"))),
                new XElement(spreadsheetDrawingNs + "cNvGraphicFramePr")),
            new XElement(spreadsheetDrawingNs + "xfrm",
                isChartEx
                    ? new object[]
                    {
                        new XElement(drawingNs + "off",
                            new XAttribute("x", "0"),
                            new XAttribute("y", "0")),
                        new XElement(drawingNs + "ext",
                            new XAttribute("cx", "0"),
                            new XAttribute("cy", "0"))
                    }
                    : []),
            new XElement(drawingNs + "graphic",
                new XElement(drawingNs + "graphicData",
                    new XAttribute("uri", ToChartDrawingUri(chartRelationshipType)),
                    new XElement(ToChartDrawingElementName(chartRelationshipType, chartNs, chartExNs), new XAttribute(relNs + "id", chartRelId)))));
    }

    private static XElement ToChartExFallbackShape(
        ChartModel chart,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs) =>
        new(spreadsheetDrawingNs + "sp",
            new XAttribute("macro", ""),
            new XAttribute("textlink", ""),
            new XElement(spreadsheetDrawingNs + "nvSpPr",
                new XElement(spreadsheetDrawingNs + "cNvPr",
                    new XAttribute("id", "0"),
                    new XAttribute("name", "")),
                new XElement(spreadsheetDrawingNs + "cNvSpPr",
                    new XElement(drawingNs + "spLocks", new XAttribute("noTextEdit", "1")))),
            new XElement(spreadsheetDrawingNs + "spPr",
                new XElement(drawingNs + "xfrm",
                    new XElement(drawingNs + "off",
                        new XAttribute("x", PixelsToEmus(chart.Left)),
                        new XAttribute("y", PixelsToEmus(chart.Top))),
                    new XElement(drawingNs + "ext",
                        new XAttribute("cx", PixelsToEmus(chart.Width)),
                        new XAttribute("cy", PixelsToEmus(chart.Height)))),
                new XElement(drawingNs + "prstGeom",
                    new XAttribute("prst", "rect"),
                    new XElement(drawingNs + "avLst")),
                new XElement(drawingNs + "solidFill",
                    new XElement(drawingNs + "prstClr", new XAttribute("val", "white"))),
                new XElement(drawingNs + "ln",
                    new XAttribute("w", "1"),
                    new XElement(drawingNs + "solidFill",
                        new XElement(drawingNs + "prstClr", new XAttribute("val", "green"))))),
            new XElement(spreadsheetDrawingNs + "txBody",
                new XElement(drawingNs + "bodyPr",
                    new XAttribute("vertOverflow", "clip"),
                    new XAttribute("horzOverflow", "clip")),
                new XElement(drawingNs + "lstStyle"),
                new XElement(drawingNs + "p",
                    new XElement(drawingNs + "r",
                        new XElement(drawingNs + "rPr",
                            new XAttribute("lang", "en-US"),
                            new XAttribute("sz", "1100")),
                        new XElement(drawingNs + "t",
                            "This chart isn't available in your version of Excel.\n\nEditing this shape or saving this workbook into a different file format will permanently break the chart.")))));

    private static string ToChartDrawingUri(string chartRelationshipType) =>
        IsChartExRelationship(chartRelationshipType)
            ? ChartExDrawingUri
            : "http://schemas.openxmlformats.org/drawingml/2006/chart";

    private static XName ToChartDrawingElementName(string chartRelationshipType, XNamespace chartNs, XNamespace chartExNs) =>
        IsChartExRelationship(chartRelationshipType)
            ? chartExNs + "chart"
            : chartNs + "chart";

    private static bool IsChartExRelationship(string chartRelationshipType) =>
        string.Equals(chartRelationshipType, ChartExRelationshipType, StringComparison.OrdinalIgnoreCase);

    private static string GetChartSiblingRelationshipTarget(string chartPartPath)
    {
        var slash = chartPartPath.LastIndexOf('/');
        return slash >= 0 ? chartPartPath[(slash + 1)..] : chartPartPath;
    }

    private static XElement ToAnchorMarkerXml(string name, AnchorMarker marker, XNamespace spreadsheetDrawingNs) =>
        new(spreadsheetDrawingNs + name,
            new XElement(spreadsheetDrawingNs + "col", marker.Column),
            new XElement(spreadsheetDrawingNs + "colOff", PixelsToEmus(marker.ColumnOffset)),
            new XElement(spreadsheetDrawingNs + "row", marker.Row),
            new XElement(spreadsheetDrawingNs + "rowOff", PixelsToEmus(marker.RowOffset)));

    private static AnchorMarker ToAnchorMarker(Sheet sheet, double left, double top) =>
        new(
            ToMarkerIndex(left, sheet.DefaultColumnWidth * 8, column => sheet.IsColEffectivelyHidden(column), column => sheet.ColumnWidths.GetValueOrDefault(column, sheet.DefaultColumnWidth) * 8),
            ToMarkerIndex(top, sheet.DefaultRowHeight, row => sheet.IsRowEffectivelyHidden(row), row => sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight)));

    private static MarkerAxis ToMarkerIndex(double pixels, double defaultSize, Func<uint, bool> isHidden, Func<uint, double> getSize)
    {
        var remaining = Math.Max(0, pixels);
        var index = 0u;
        while (index < 16384)
        {
            var oneBasedIndex = index + 1;
            var size = isHidden(oneBasedIndex) ? 0 : Math.Max(0, getSize(oneBasedIndex));
            if (size <= 0)
            {
                index++;
                continue;
            }

            if (remaining < size)
                return new MarkerAxis(index, remaining);

            remaining -= size;
            index++;
        }

        return new MarkerAxis(index, Math.Min(remaining, Math.Max(0, defaultSize)));
    }

    private readonly record struct MarkerAxis(uint Index, double Offset);

    private readonly record struct AnchorMarker(MarkerAxis ColumnAxis, MarkerAxis RowAxis)
    {
        public uint Column => ColumnAxis.Index;
        public double ColumnOffset => ColumnAxis.Offset;
        public uint Row => RowAxis.Index;
        public double RowOffset => RowAxis.Offset;
    }

    private static long PixelsToEmus(double pixels) =>
        (long)Math.Round(Math.Max(0, pixels) * 9525.0);

    private static string DrawingName(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name;
}
