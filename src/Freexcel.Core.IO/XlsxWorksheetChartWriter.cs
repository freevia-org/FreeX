using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetChartWriter
{
    public static bool HasSupportedCharts(Workbook workbook, Func<ChartModel, bool> isSupportedChart) =>
        workbook.Sheets.Any(sheet => sheet.Charts.Any(isSupportedChart));

    public static void Save(
        Stream xlsxStream,
        Workbook workbook,
        Func<ChartModel, bool> isSupportedChart,
        Func<ChartModel, Sheet, XDocument> createChartXml)
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

            WriteWorksheetCharts(archive, worksheetPath, sheet, supportedCharts, drawingIndex++, ref chartIndex, createChartXml);
        }
    }

    private static void WriteWorksheetCharts(
        ZipArchive archive,
        string worksheetPath,
        Sheet sheet,
        IReadOnlyList<ChartModel> charts,
        int drawingIndex,
        ref int chartIndex,
        Func<ChartModel, Sheet, XDocument> createChartXml)
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

        var drawingPath = $"xl/drawings/drawing{drawingIndex}.xml";
        var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
        archive.GetEntry(drawingPath)?.Delete();
        archive.GetEntry(drawingRelsPath)?.Delete();

        var drawingRelsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        var anchors = new List<XElement>();
        foreach (var chart in charts)
        {
            var currentChartIndex = chartIndex++;
            var chartPath = $"xl/charts/chart{currentChartIndex}.xml";
            archive.GetEntry(chartPath)?.Delete();
            var chartEntry = archive.CreateEntry(chartPath);
            using (var chartStream = chartEntry.Open())
                createChartXml(chart, sheet).Save(chartStream);
            WriteChartExternalDataRelationships(archive, chartPath, chart, packageRelNs);

            var chartRelId = $"rIdFreexcelChart{currentChartIndex}";
            drawingRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", chartRelId),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"),
                new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(drawingPath, chartPath))));

            anchors.Add(ToChartAnchor(chart, sheet, currentChartIndex, chartRelId, spreadsheetDrawingNs, drawingNs, chartNs, relNs));
        }

        XlsxPackageXmlEditor.ReplaceXml(archive, drawingPath, new XDocument(
            new XElement(spreadsheetDrawingNs + "wsDr",
                new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XAttribute(XNamespace.Xmlns + "c", chartNs),
                new XAttribute(XNamespace.Xmlns + "r", relNs),
                anchors)));
        XlsxPackageXmlEditor.ReplaceXml(archive, drawingRelsPath, drawingRelsXml);

        XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{drawingPath}", "application/vnd.openxmlformats-officedocument.drawing+xml");
        for (var i = chartIndex - charts.Count; i < chartIndex; i++)
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/xl/charts/chart{i}.xml", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml");

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
        root.Elements(worksheetNs + "drawing").Remove();
        root.Add(new XElement(worksheetNs + "drawing", new XAttribute(relNs + "id", drawingRelId)));
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
    }

    private static void WriteChartExternalDataRelationships(
        ZipArchive archive,
        string chartPath,
        ChartModel chart,
        XNamespace packageRelNs)
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

        if (relationships.Count == 0)
        {
            return;
        }

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(chartPath);
        XlsxPackageXmlEditor.ReplaceXml(archive, relsPath, new XDocument(new XElement(packageRelNs + "Relationships", relationships)));
    }

    private static XElement ToChartAnchor(
        ChartModel chart,
        Sheet sheet,
        int chartIndex,
        string chartRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace relNs) =>
        chart.DrawingAnchorKind switch
        {
            ChartDrawingAnchorKind.OneCell => ToOneCellChartAnchor(chart, sheet, chartIndex, chartRelId, spreadsheetDrawingNs, drawingNs, chartNs, relNs),
            ChartDrawingAnchorKind.TwoCell => ToTwoCellChartAnchor(chart, sheet, chartIndex, chartRelId, spreadsheetDrawingNs, drawingNs, chartNs, relNs),
            _ => ToAbsoluteChartAnchor(chart, chartIndex, chartRelId, spreadsheetDrawingNs, drawingNs, chartNs, relNs)
        };

    private static XElement ToAbsoluteChartAnchor(
        ChartModel chart,
        int chartIndex,
        string chartRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace relNs) =>
        new(spreadsheetDrawingNs + "absoluteAnchor",
            new XElement(spreadsheetDrawingNs + "pos",
                new XAttribute("x", PixelsToEmus(chart.Left)),
                new XAttribute("y", PixelsToEmus(chart.Top))),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", PixelsToEmus(chart.Width)),
                new XAttribute("cy", PixelsToEmus(chart.Height))),
            new XElement(spreadsheetDrawingNs + "graphicFrame",
                new XElement(spreadsheetDrawingNs + "nvGraphicFramePr",
                    new XElement(spreadsheetDrawingNs + "cNvPr",
                        new XAttribute("id", chartIndex + 1),
                        new XAttribute("name", DrawingName(chart.Name, $"Chart {chartIndex}"))),
                    new XElement(spreadsheetDrawingNs + "cNvGraphicFramePr")),
                new XElement(spreadsheetDrawingNs + "xfrm"),
                new XElement(drawingNs + "graphic",
                    new XElement(drawingNs + "graphicData",
                        new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/chart"),
                        new XElement(chartNs + "chart", new XAttribute(relNs + "id", chartRelId))))),
            new XElement(spreadsheetDrawingNs + "clientData"));

    private static XElement ToOneCellChartAnchor(
        ChartModel chart,
        Sheet sheet,
        int chartIndex,
        string chartRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace relNs)
    {
        var from = ToAnchorMarker(sheet, chart.Left, chart.Top);
        return new XElement(spreadsheetDrawingNs + "oneCellAnchor",
            ToAnchorMarkerXml("from", from, spreadsheetDrawingNs),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", PixelsToEmus(chart.Width)),
                new XAttribute("cy", PixelsToEmus(chart.Height))),
            ToChartGraphicFrame(chart, chartIndex, chartRelId, spreadsheetDrawingNs, drawingNs, chartNs, relNs),
            new XElement(spreadsheetDrawingNs + "clientData"));
    }

    private static XElement ToTwoCellChartAnchor(
        ChartModel chart,
        Sheet sheet,
        int chartIndex,
        string chartRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace relNs)
    {
        var from = ToAnchorMarker(sheet, chart.Left, chart.Top);
        var to = ToAnchorMarker(sheet, chart.Left + chart.Width, chart.Top + chart.Height);
        return new XElement(spreadsheetDrawingNs + "twoCellAnchor",
            ToAnchorMarkerXml("from", from, spreadsheetDrawingNs),
            ToAnchorMarkerXml("to", to, spreadsheetDrawingNs),
            ToChartGraphicFrame(chart, chartIndex, chartRelId, spreadsheetDrawingNs, drawingNs, chartNs, relNs),
            new XElement(spreadsheetDrawingNs + "clientData"));
    }

    private static XElement ToChartGraphicFrame(
        ChartModel chart,
        int chartIndex,
        string chartRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace relNs) =>
        new(spreadsheetDrawingNs + "graphicFrame",
            new XElement(spreadsheetDrawingNs + "nvGraphicFramePr",
                new XElement(spreadsheetDrawingNs + "cNvPr",
                    new XAttribute("id", chartIndex + 1),
                    new XAttribute("name", DrawingName(chart.Name, $"Chart {chartIndex}"))),
                new XElement(spreadsheetDrawingNs + "cNvGraphicFramePr")),
            new XElement(spreadsheetDrawingNs + "xfrm"),
            new XElement(drawingNs + "graphic",
                new XElement(drawingNs + "graphicData",
                    new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/chart"),
                    new XElement(chartNs + "chart", new XAttribute(relNs + "id", chartRelId)))));

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
