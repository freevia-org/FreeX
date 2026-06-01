using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetChartWriter
{
    private const string ChartExRelationshipType = "http://schemas.microsoft.com/office/2014/relationships/chartEx";
    private const string ChartExDrawingUri = "http://schemas.microsoft.com/office/drawing/2014/chartex";
    private const string ChartExColorStyleContentType = "application/vnd.ms-office.chartcolorstyle+xml";
    private const string ChartExStyleContentType = "application/vnd.ms-office.chartstyle+xml";
    private const string ChartExColorStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartColorStyle";
    private const string ChartExStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartStyle";
    private const string ChartExChoiceNamespace = "http://schemas.microsoft.com/office/drawing/2015/9/8/chartex";

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
        var chartExStyleParts = new Dictionary<int, ChartExStylePackageParts>();
        foreach (var chart in charts)
        {
            var currentChartIndex = chartIndex++;
            chartContentTypes[currentChartIndex] = getChartContentType(chart);
            var chartPath = $"xl/charts/chart{currentChartIndex}.xml";
            archive.GetEntry(chartPath)?.Delete();
            var chartEntry = archive.CreateEntry(chartPath);
            using (var chartStream = chartEntry.Open())
                createChartXml(chart, sheet).Save(chartStream);

            var styleParts = ChartTypeSupport.IsChartExFamily(chart.Type)
                ? WriteChartExStyleParts(archive, currentChartIndex, chart.Type)
                : (ChartExStylePackageParts?)null;
            if (styleParts is { } chartExParts)
                chartExStyleParts[currentChartIndex] = chartExParts;
            WriteChartRelationships(archive, chartPath, chart, styleParts, packageRelNs);

            var chartRelId = $"rIdFreeXChart{currentChartIndex}";
            var chartRelationshipType = getChartRelationshipType(chart);
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
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/xl/charts/chart{index}.xml", contentType);
        foreach (var (_, styleParts) in chartExStyleParts)
        {
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{styleParts.ColorStylePath}", ChartExColorStyleContentType);
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{styleParts.StylePath}", ChartExStyleContentType);
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
        ChartExStylePackageParts? chartExStyleParts,
        XNamespace packageRelNs)
    {
        var relsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        var relationships = relsXml.Root!;
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

        if (chartExStyleParts is { } styleParts)
        {
            relationships.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", XlsxPackageXmlEditor.NextRelationshipId(relsXml, packageRelNs)),
                new XAttribute("Type", ChartExStyleRelationshipType),
                new XAttribute("Target", GetSameDirectoryRelationshipTarget(styleParts.StylePath))));
            relationships.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", XlsxPackageXmlEditor.NextRelationshipId(relsXml, packageRelNs)),
                new XAttribute("Type", ChartExColorStyleRelationshipType),
                new XAttribute("Target", GetSameDirectoryRelationshipTarget(styleParts.ColorStylePath))));
        }

        if (!relationships.HasElements)
        {
            return;
        }

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(chartPath);
        XlsxPackageXmlEditor.ReplaceXml(archive, relsPath, relsXml);
    }

    private static ChartExStylePackageParts WriteChartExStyleParts(ZipArchive archive, int chartIndex, ChartType chartType)
    {
        var colorStylePath = $"xl/charts/colors{chartIndex}.xml";
        var stylePath = $"xl/charts/style{chartIndex}.xml";
        XlsxPackageXmlEditor.ReplaceXml(archive, colorStylePath, CreateChartExColorStyleXml());
        XlsxPackageXmlEditor.ReplaceXml(archive, stylePath, CreateChartExStyleXml(chartType));
        return new ChartExStylePackageParts(colorStylePath, stylePath);
    }

    private static XDocument CreateChartExColorStyleXml()
    {
        XNamespace chartStyleNs = "http://schemas.microsoft.com/office/drawing/2012/chartStyle";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var accents = new[] { "accent1", "accent2", "accent3", "accent4", "accent5", "accent6" }
            .Select(color => new XElement(drawingNs + "schemeClr", new XAttribute("val", color)));

        return new XDocument(
            new XElement(chartStyleNs + "colorStyle",
                new XAttribute(XNamespace.Xmlns + "cs", chartStyleNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XAttribute("meth", "cycle"),
                new XAttribute("id", "10"),
                accents,
                new XElement(chartStyleNs + "variation"),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 60000),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 80000, lumOff: 20000),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 80000),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 60000, lumOff: 40000),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 50000),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 70000, lumOff: 30000),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 70000),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 50000, lumOff: 50000)));
    }

    private static XElement ToChartExColorVariation(
        XNamespace chartStyleNs,
        XNamespace drawingNs,
        int lumMod,
        int? lumOff = null) =>
        new(chartStyleNs + "variation",
            new XElement(drawingNs + "lumMod", new XAttribute("val", lumMod)),
            lumOff is null
                ? null
                : new XElement(drawingNs + "lumOff", new XAttribute("val", lumOff.Value)));

    private static XDocument CreateChartExStyleXml(ChartType chartType)
    {
        _ = chartType;

        // Excel emits the same native chartEx style profile for the supported modern chart family.
        return XDocument.Parse(
            """
            <cs:chartStyle xmlns:cs="http://schemas.microsoft.com/office/drawing/2012/chartStyle" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" id="201"><cs:axisTitle><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:defRPr sz="1000" kern="1200"/></cs:axisTitle><cs:categoryAxis><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="15000"/><a:lumOff val="85000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr><cs:defRPr sz="900" kern="1200"/></cs:categoryAxis><cs:chartArea mods="allowNoFillOverride allowNoLineOverride"><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:solidFill><a:schemeClr val="bg1"/></a:solidFill><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="15000"/><a:lumOff val="85000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr><cs:defRPr sz="1000" kern="1200"/></cs:chartArea><cs:dataLabel><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="75000"/><a:lumOff val="25000"/></a:schemeClr></cs:fontRef><cs:defRPr sz="900" kern="1200"/></cs:dataLabel><cs:dataLabelCallout><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="dk1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:spPr><a:solidFill><a:schemeClr val="lt1"/></a:solidFill><a:ln><a:solidFill><a:schemeClr val="dk1"><a:lumMod val="25000"/><a:lumOff val="75000"/></a:schemeClr></a:solidFill></a:ln></cs:spPr><cs:defRPr sz="900" kern="1200"/><cs:bodyPr rot="0" spcFirstLastPara="1" vertOverflow="clip" horzOverflow="clip" vert="horz" wrap="square" lIns="36576" tIns="18288" rIns="36576" bIns="18288" anchor="ctr" anchorCtr="1"><a:spAutoFit/></cs:bodyPr></cs:dataLabelCallout><cs:dataPoint><cs:lnRef idx="0"/><cs:fillRef idx="1"><cs:styleClr val="auto"/></cs:fillRef><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef></cs:dataPoint><cs:dataPoint3D><cs:lnRef idx="0"/><cs:fillRef idx="1"><cs:styleClr val="auto"/></cs:fillRef><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef></cs:dataPoint3D><cs:dataPointLine><cs:lnRef idx="0"><cs:styleClr val="auto"/></cs:lnRef><cs:fillRef idx="1"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="28575" cap="rnd"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:round/></a:ln></cs:spPr></cs:dataPointLine><cs:dataPointMarker><cs:lnRef idx="0"><cs:styleClr val="auto"/></cs:lnRef><cs:fillRef idx="1"><cs:styleClr val="auto"/></cs:fillRef><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln></cs:spPr></cs:dataPointMarker><cs:dataPointMarkerLayout symbol="circle" size="5"/><cs:dataPointWireframe><cs:lnRef idx="0"><cs:styleClr val="auto"/></cs:lnRef><cs:fillRef idx="1"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="rnd"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:round/></a:ln></cs:spPr></cs:dataPointWireframe><cs:dataTable><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:spPr><a:noFill/><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="15000"/><a:lumOff val="85000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr><cs:defRPr sz="900" kern="1200"/></cs:dataTable><cs:downBar><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="dk1"/></cs:fontRef><cs:spPr><a:solidFill><a:schemeClr val="dk1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></a:solidFill><a:ln w="9525"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></a:solidFill></a:ln></cs:spPr></cs:downBar><cs:dropLine><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="35000"/><a:lumOff val="65000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr></cs:dropLine><cs:errorBar><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr></cs:errorBar><cs:floor><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:noFill/><a:ln><a:noFill/></a:ln></cs:spPr></cs:floor><cs:gridlineMajor><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="15000"/><a:lumOff val="85000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr></cs:gridlineMajor><cs:gridlineMinor><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="5000"/><a:lumOff val="95000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr></cs:gridlineMinor><cs:hiLoLine><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="75000"/><a:lumOff val="25000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr></cs:hiLoLine><cs:leaderLine><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="35000"/><a:lumOff val="65000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr></cs:leaderLine><cs:legend><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:defRPr sz="900" kern="1200"/></cs:legend><cs:plotArea mods="allowNoFillOverride allowNoLineOverride"><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef></cs:plotArea><cs:plotArea3D mods="allowNoFillOverride allowNoLineOverride"><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef></cs:plotArea3D><cs:seriesAxis><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:defRPr sz="900" kern="1200"/></cs:seriesAxis><cs:seriesLine><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="35000"/><a:lumOff val="65000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr></cs:seriesLine><cs:title><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:defRPr sz="1400" b="0" kern="1200" spc="0" baseline="0"/></cs:title><cs:trendline><cs:lnRef idx="0"><cs:styleClr val="auto"/></cs:lnRef><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="19050" cap="rnd"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="sysDot"/></a:ln></cs:spPr></cs:trendline><cs:trendlineLabel><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:defRPr sz="900" kern="1200"/></cs:trendlineLabel><cs:upBar><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="dk1"/></cs:fontRef><cs:spPr><a:solidFill><a:schemeClr val="lt1"/></a:solidFill><a:ln w="9525"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="15000"/><a:lumOff val="85000"/></a:schemeClr></a:solidFill></a:ln></cs:spPr></cs:upBar><cs:valueAxis><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:defRPr sz="900" kern="1200"/></cs:valueAxis><cs:wall><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:noFill/><a:ln><a:noFill/></a:ln></cs:spPr></cs:wall></cs:chartStyle>
            """);
    }

    private static string GetSameDirectoryRelationshipTarget(string targetPath)
    {
        var slash = targetPath.LastIndexOf('/');
        return slash < 0 ? targetPath : targetPath[(slash + 1)..];
    }

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
        if (IsChartExRelationshipType(chartRelationshipType))
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
        if (!IsChartExRelationshipType(chartRelationshipType))
            return graphicFrame;

        return new XElement(markupCompatNs + "AlternateContent",
            new XAttribute(XNamespace.Xmlns + "mc", markupCompatNs),
            new XElement(markupCompatNs + "Choice",
                new XAttribute(XNamespace.Xmlns + "cx1", ChartExChoiceNamespace),
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
        var isChartEx = IsChartExRelationshipType(chartRelationshipType);
        return new XElement(spreadsheetDrawingNs + "graphicFrame",
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
        IsChartExRelationshipType(chartRelationshipType)
            ? ChartExDrawingUri
            : "http://schemas.openxmlformats.org/drawingml/2006/chart";

    private static XName ToChartDrawingElementName(string chartRelationshipType, XNamespace chartNs, XNamespace chartExNs) =>
        IsChartExRelationshipType(chartRelationshipType)
            ? chartExNs + "chart"
            : chartNs + "chart";

    private static bool IsChartExRelationshipType(string chartRelationshipType) =>
        string.Equals(chartRelationshipType, ChartExRelationshipType, StringComparison.OrdinalIgnoreCase);

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

    private readonly record struct ChartExStylePackageParts(string ColorStylePath, string StylePath);
}
