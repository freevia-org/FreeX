using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;
using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>
/// Writes a <see cref="ChartShape"/> model as a <c>ppt/charts/chartN.xml</c> part
/// (plus a minimal embedded workbook stub) into a .pptx <see cref="ZipArchive"/>.
///
/// Returns the OPC part path and relationship ID so the caller can wire the chart
/// into the slide's graphicFrame and rels.
/// </summary>
internal static class PptxChartWriter
{
    private static readonly XNamespace C    = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace A    = PptxColorReader.A;
    private static readonly XNamespace R    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgR = "http://schemas.openxmlformats.org/package/2006/relationships";

    internal const string ChartRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    internal const string ChartExRelType =
        "http://schemas.microsoft.com/office/2014/relationships/chartEx";
    internal const string ChartCT =
        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
    internal const string ChartExCT = "application/vnd.ms-office.chartex+xml";
    internal const string ChartWorkbookCT =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string PackageRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package";

    // ── OOXML write settings (UTF-8 no BOM, indented) ────────────────────────
    private static readonly System.Xml.XmlWriterSettings XmlSettings = new()
    {
        Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = true,
        OmitXmlDeclaration = false,
        CloseOutput = false
    };

    /// <summary>
    /// Writes the chart part and its embedded workbook into <paramref name="archive"/>,
    /// using <paramref name="chartIndex"/> to form unique part paths.
    /// </summary>
    /// <returns>The OPC path of the written chart part (e.g. "ppt/charts/chart1.xml").</returns>
    internal static string WriteChartPart(
        ZipArchive archive,
        ChartShape chart,
        int chartIndex,
        PptxPackageSnapshot? packageSnapshot = null)
    {
        var chartPath = $"ppt/charts/chart{chartIndex}.xml";

        // Write chart XML
        var chartDoc = BuildChartDoc(chart);
        if (chart.RegenerateWorkbookOnSave)
        {
            // A chart-own data edit (ReplaceChartDataCommand, dispatched by the chart-data
            // dialog) must not silently drop this chart's PowerPoint-2013+ chartStyle/
            // chartColorStyle sidecars (style1.xml, colors1.xml, …). Those live as sibling
            // relationships in the SAME .rels document as the embedded workbook, so
            // regenerating the workbook relationship wholesale (as this branch used to)
            // replaced the entire rels document with just the one new relationship,
            // orphaning the sidecars. Merge the new relationship INTO the preserved rels
            // instead, mirroring TryRegenerateChartExWorkbook's fix for the ChartEx path.
            var sourceChartPathForMerge = PptxPackageWriter.SourceChartPath(chart, chartIndex);
            var workbookRelId = "rIdWorkbook1";
            byte[]? mergedRelBytes = null;
            var sourceRelsPathForMerge = OpcPathHelper.GetRelationshipPartPath(sourceChartPathForMerge);
            if (packageSnapshot?.TryGetEntry(sourceRelsPathForMerge, out var sourceRelBytesForMerge) == true &&
                TryMergeRegeneratedWorkbookRelationship(
                    chartIndex,
                    sourceChartPathForMerge,
                    sourceRelBytesForMerge,
                    out var mergedWorkbookRelId,
                    out var mergedRelDocBytes))
            {
                workbookRelId = mergedWorkbookRelId;
                mergedRelBytes = mergedRelDocBytes;
            }

            AddExternalData(chartDoc, workbookRelId);
            WriteEntry(archive, chartPath, chartDoc);
            WriteRegeneratedWorkbook(archive, chart, GetRegeneratedWorkbookPath(chartIndex));
            if (mergedRelBytes is not null)
                WriteBytes(archive, OpcPathHelper.GetRelationshipPartPath(chartPath), mergedRelBytes);
            else
                WriteRegeneratedWorkbookRelationship(archive, chartPath, workbookRelId, chartIndex);
            return chartPath;
        }

        var sourceChartPath = PptxPackageWriter.SourceChartPath(chart, chartIndex);
        MergePreservedExternalData(chartDoc, packageSnapshot, sourceChartPath);
        WriteEntry(archive, chartPath, chartDoc);
        WritePreservedWorkbookRelationships(archive, packageSnapshot, sourceChartPath, chartPath);

        return chartPath;
    }

    internal static string WriteChartExPart(
        ZipArchive archive,
        ChartShape chart,
        int chartIndex,
        PptxPackageSnapshot? packageSnapshot = null)
    {
        var chartPath = $"ppt/charts/chartEx{chartIndex}.xml";
        var chartDoc = BuildChartExDoc(chart);
        WriteEntry(archive, chartPath, chartDoc);

        // ChartEx sidecars (style/color/workbook) are retained by the package writer;
        // their relative targets remain valid when the part keeps the charts directory.
        var sourcePath = PptxPackageWriter.SourceChartPath(chart, chartIndex);
        var sourceRelsPath = OpcPathHelper.GetRelationshipPartPath(sourcePath);
        var destinationRelsPath = OpcPathHelper.GetRelationshipPartPath(chartPath);
        if (packageSnapshot?.TryGetEntry(sourceRelsPath, out var relBytes) == true)
        {
            // A ChartEx data edit (chart.RegenerateWorkbookOnSave) already refreshed the
            // on-slide cx:data cache above via BuildChartExDoc/UpdateChartExData, but that
            // cache is a separate OPC part from the chart's own embedded "Edit Data in
            // Excel" workbook (ppt/embeddings/...xlsx), which this verbatim rels copy would
            // otherwise carry forward untouched. Left alone, the embedded workbook keeps
            // serving pre-edit numbers, so the next Excel round trip starts from stale data
            // (finding: ChartEx edits desync the chart from its own backing data). Rewrite
            // just that one relationship's target to a freshly regenerated workbook built
            // from the same chart.Categories/chart.Series the cache was just refreshed
            // from, reusing the chart-type-agnostic workbook writer regular charts already
            // use; every other relationship (chartStyle, chartColorStyle, …) is left as-is.
            if (chart.RegenerateWorkbookOnSave &&
                TryRegenerateChartExWorkbook(archive, chart, chartIndex, sourcePath, relBytes, out var mergedRelBytes))
            {
                relBytes = mergedRelBytes;
            }

            var relEntry = archive.CreateEntry(destinationRelsPath, CompressionLevel.Optimal);
            using var stream = relEntry.Open();
            stream.Write(relBytes, 0, relBytes.Length);
        }

        return chartPath;
    }

    /// <summary>
    /// Rewrites the single "package" (embedded-workbook) relationship in a ChartEx chart's
    /// preserved <c>.rels</c> so it points at a freshly regenerated workbook containing the
    /// current <paramref name="chart"/> data, leaving every other relationship untouched.
    /// Returns false (leaving <paramref name="mergedRelBytes"/> unset) when the source rels
    /// don't parse or contain no embedded-workbook relationship to begin with — there is
    /// nothing to desync in that case, so the original bytes should be used unchanged.
    /// </summary>
    private static bool TryRegenerateChartExWorkbook(
        ZipArchive archive,
        ChartShape chart,
        int chartIndex,
        string sourceChartPath,
        byte[] sourceRelBytes,
        out byte[] mergedRelBytes)
    {
        mergedRelBytes = [];
        var sourceRelsXml = OpcXml.TryLoadXml(sourceRelBytes);
        if (sourceRelsXml is null)
            return false;

        var sourceDirectory = OpcPathHelper.GetDirectoryName(sourceChartPath);
        var relationships = OpcRelationships.Load(sourceRelsXml);
        var workbookRelationshipId = relationships
            .Where(relationship =>
                !relationship.IsExternal &&
                !string.IsNullOrWhiteSpace(relationship.Target) &&
                string.Equals(relationship.Type, PackageRelType, StringComparison.OrdinalIgnoreCase) &&
                OpcPathHelper
                    .ResolveRelativeZipPath(sourceDirectory, relationship.Target)
                    .StartsWith("ppt/embeddings/", StringComparison.OrdinalIgnoreCase))
            .Select(relationship => relationship.Id)
            .FirstOrDefault();
        if (workbookRelationshipId is null)
            return false;

        var regeneratedWorkbookPath = GetRegeneratedWorkbookPath(chartIndex);
        WriteRegeneratedWorkbook(archive, chart, regeneratedWorkbookPath);

        var newTarget = $"../embeddings/{regeneratedWorkbookPath.Split('/').Last()}";
        using var mergedStream = new MemoryStream();
        OpcRelationships.CreateDocument(relationships.Select(relationship =>
                OpcRelationships.CreateRelationship(
                    relationship.Id,
                    relationship.Type,
                    relationship.Id == workbookRelationshipId ? newTarget : relationship.Target,
                    relationship.IsExternal)))
            .Save(mergedStream);
        mergedRelBytes = mergedStream.ToArray();
        return true;
    }

    /// <summary>
    /// Merges a freshly regenerated embedded-workbook relationship into a regular (non-ChartEx)
    /// chart's preserved <c>.rels</c>, leaving every other relationship — notably the
    /// PowerPoint-2013+ chartStyle/chartColorStyle sidecar relationships — untouched. This is
    /// the WriteChartPart counterpart of <see cref="TryRegenerateChartExWorkbook"/>: without it,
    /// editing a chart's own data through the chart-data dialog (ReplaceChartDataCommand, which
    /// sets <c>RegenerateWorkbookOnSave</c>) replaced the chart's ENTIRE rels document with just
    /// the one new workbook relationship, silently dropping style1.xml/colors1.xml. Returns false
    /// (leaving the out parameters unset/default) when the source rels don't parse, in which case
    /// the caller falls back to writing a rels document containing only the new relationship.
    /// </summary>
    private static bool TryMergeRegeneratedWorkbookRelationship(
        int chartIndex,
        string sourceChartPath,
        byte[] sourceRelBytes,
        out string workbookRelId,
        out byte[] mergedRelBytes)
    {
        workbookRelId = "rIdWorkbook1";
        mergedRelBytes = [];
        var sourceRelsXml = OpcXml.TryLoadXml(sourceRelBytes);
        if (sourceRelsXml is null)
            return false;

        var sourceDirectory = OpcPathHelper.GetDirectoryName(sourceChartPath);
        var relationships = OpcRelationships.Load(sourceRelsXml).ToArray();
        var existingWorkbookRelationshipId = relationships
            .Where(relationship =>
                !relationship.IsExternal &&
                !string.IsNullOrWhiteSpace(relationship.Target) &&
                string.Equals(relationship.Type, PackageRelType, StringComparison.OrdinalIgnoreCase) &&
                OpcPathHelper
                    .ResolveRelativeZipPath(sourceDirectory, relationship.Target)
                    .StartsWith("ppt/embeddings/", StringComparison.OrdinalIgnoreCase))
            .Select(relationship => relationship.Id)
            .FirstOrDefault();

        var regeneratedWorkbookPath = GetRegeneratedWorkbookPath(chartIndex);
        var newTarget = $"../embeddings/{regeneratedWorkbookPath.Split('/').Last()}";

        // Drop the pre-edit embedded-workbook relationship (if any) and every other kept
        // relationship's Id stays exactly as it was preserved — chartStyle, chartColorStyle,
        // and anything else survive untouched. The new workbook relationship is always minted
        // with the same fixed Id ("rIdWorkbook1", collision-avoided) that AddExternalData
        // already writes into the freshly built chart XML's <c:externalData r:id="...">, so
        // callers don't need to thread a variable Id back into BuildChartDoc's output.
        var otherRelationships = relationships
            .Where(relationship => relationship.Id != existingWorkbookRelationshipId)
            .ToArray();
        var usedIds = new HashSet<string>(
            otherRelationships.Select(relationship => relationship.Id),
            StringComparer.OrdinalIgnoreCase);
        var candidateId = workbookRelId;
        var suffix = 2;
        while (usedIds.Contains(candidateId))
            candidateId = $"{workbookRelId}_{suffix++}";
        workbookRelId = candidateId;

        var mergedRelationships = otherRelationships
            .Select(relationship => OpcRelationships.CreateRelationship(
                relationship.Id, relationship.Type, relationship.Target, relationship.IsExternal))
            .Append(OpcRelationships.CreateRelationship(workbookRelId, PackageRelType, newTarget, false));

        using var mergedStream = new MemoryStream();
        OpcRelationships.CreateDocument(mergedRelationships).Save(mergedStream);
        mergedRelBytes = mergedStream.ToArray();
        return true;
    }

    internal static string GetWrittenChartPath(ChartShape chart, int chartIndex) =>
        chart.IsChartEx ? $"ppt/charts/chartEx{chartIndex}.xml" : $"ppt/charts/chart{chartIndex}.xml";

    private static XDocument BuildChartExDoc(ChartShape chart)
    {
        if (!string.IsNullOrWhiteSpace(chart.PreservedChartExXml))
        {
            try
            {
                var preserved = XDocument.Parse(chart.PreservedChartExXml, LoadOptions.PreserveWhitespace);
                UpdateChartExTitle(preserved, chart);
                UpdateChartExLegend(preserved, chart);
                UpdateChartExAreaFormatting(preserved, chart);
                UpdateChartExSeriesLayouts(preserved, chart);
                UpdateChartExSeriesShapeProperties(preserved, chart);
                UpdateChartExValueColorScales(preserved, chart);
                UpdateChartExSeriesDataPoints(preserved, chart);
                UpdateChartExSeriesDataLabels(preserved, chart);
                if (chart.RegenerateWorkbookOnSave)
                    UpdateChartExData(preserved, chart);
                if (IsWaterfallChartEx(preserved, chart))
                    UpdateChartExSemantics(preserved, chart);
                return preserved;
            }
            catch (XmlException)
            {
                // Fall through to a minimal valid ChartEx payload.
            }
        }

        XNamespace cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        var values = chart.Series.FirstOrDefault()?.Values ?? [];
        var data = new XElement(cx + "data",
            new XAttribute("id", 0),
            new XElement(cx + "strDim",
                new XAttribute("type", "cat"),
                new XElement(cx + "lvl",
                    new XAttribute("ptCount", chart.Categories.Count),
                    chart.Categories.Select((category, index) =>
                        new XElement(cx + "pt", new XAttribute("idx", index), category)))),
            new XElement(cx + "numDim",
                new XAttribute("type", "val"),
                new XElement(cx + "lvl",
                    new XAttribute("ptCount", values.Count),
                    new XAttribute("formatCode", "General"),
                    values.Select((value, index) =>
                        new XElement(cx + "pt", new XAttribute("idx", index),
                            (value ?? 0).ToString("G", CultureInfo.InvariantCulture))))));

        var series = new XElement(cx + "series",
            new XAttribute("layoutId", "waterfall"),
            new XAttribute("uniqueId", Guid.NewGuid().ToString("B")),
            new XElement(cx + "tx",
                new XElement(cx + "txData",
                    new XElement(cx + "v", chart.Series.FirstOrDefault()?.Name ?? string.Empty))),
            new XElement(cx + "dataId", new XAttribute("val", 0)),
            BuildChartExLayoutPr(chart, cx));

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(cx + "chartSpace",
                new XAttribute(XNamespace.Xmlns + "cx", cx.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                new XElement(cx + "chartData", data),
                new XElement(cx + "chart",
                    new XElement(cx + "plotArea",
                        new XElement(cx + "plotAreaRegion", series)))));
        if (!string.IsNullOrWhiteSpace(chart.Title))
        {
            document.Root?.Element(cx + "chart")?.AddFirst(
                new XElement(cx + "title",
                    chart.ChartExTitlePosition is { } titlePosition
                        ? new XAttribute("pos", ChartExTitlePositionToken(titlePosition))
                        : null,
                    chart.ChartExTitleAlignment is { } titleAlignment
                        ? new XAttribute("align", ChartExTitleAlignmentToken(titleAlignment))
                        : null,
                    chart.TitleOverlay is { } overlay
                        ? new XAttribute("overlay", overlay ? "1" : "0")
                        : null,
                    new XElement(cx + "tx",
                        new XElement(cx + "txData",
                            new XElement(cx + "v", chart.Title))),
                    BuildChartTextPropertiesEl(chart.TitleStyle, cx)));
        }
        if (chart.Legend is { } position)
            document.Root?.Element(cx + "chart")?.Add(BuildChartExLegend(
                position, chart.LegendOverlay, chart.LegendTextStyle, cx));
        return document;
    }

    private static void UpdateChartExAreaFormatting(XDocument document, ChartShape chart)
    {
        XNamespace cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";

        if (chart.ChartExChartAreaEditRequested)
            UpdateChartExShapeProperties(document.Root, chart.ChartAreaFill, chart.ChartAreaOutline, cx);

        if (!chart.ChartExPlotAreaEditRequested)
            return;

        var plotSurface = document.Root?.Element(cx + "chart")?
            .Element(cx + "plotArea")?
            .Element(cx + "plotAreaRegion")?
            .Element(cx + "plotSurface");
        if (plotSurface is null)
            return;

        UpdateChartExShapeProperties(plotSurface, chart.PlotAreaFill, chart.PlotAreaOutline, cx);
    }

    private static void UpdateChartExShapeProperties(
        XElement? owner,
        ShapeFill? fill,
        ShapeOutline? outline,
        XNamespace cx)
    {
        if (owner is null)
            return;

        var spPr = owner.Element(cx + "spPr");
        if (spPr is null)
        {
            if (fill is null && outline is null)
                return;

            spPr = new XElement(cx + "spPr");
            owner.Add(spPr);
        }

        foreach (var fillName in new[] { "noFill", "solidFill", "gradFill", "pattFill", "blipFill" })
            spPr.Element(A + fillName)?.Remove();
        spPr.Element(A + "ln")?.Remove();

        var fillElement = BuildChartAreaFillEl(fill);
        var lineElement = BuildChartAreaOutlineEl(outline);
        if (fillElement is not null)
            spPr.AddFirst(fillElement);
        if (lineElement is not null)
            spPr.Add(lineElement);

        if (spPr.IsEmpty && !spPr.HasAttributes)
            spPr.Remove();
    }

    private static XElement BuildChartExLegend(
        LegendPosition position,
        bool? overlay,
        ChartTextStyle? textStyle,
        XNamespace cx) =>
        new(cx + "legend",
            new XAttribute("pos", ChartExLegendPosition(position)),
            overlay is { } value ? new XAttribute("overlay", value ? "1" : "0") : null,
            BuildChartTextPropertiesEl(textStyle, cx));

    private static string ChartExLegendPosition(LegendPosition position) =>
        position switch
        {
            LegendPosition.Left => "l",
            LegendPosition.Top => "t",
            LegendPosition.Bottom => "b",
            _ => "r",
        };

    private static string ChartExTitlePositionToken(ChartExTitlePosition position) =>
        position switch
        {
            ChartExTitlePosition.Bottom => "b",
            ChartExTitlePosition.Left => "l",
            ChartExTitlePosition.Right => "r",
            _ => "t",
        };

    private static string ChartExTitleAlignmentToken(ChartExTitleAlignment alignment) =>
        alignment switch
        {
            ChartExTitleAlignment.Near => "near",
            ChartExTitleAlignment.Far => "far",
            _ => "ctr",
        };

    private static void UpdateChartExLegend(XDocument document, ChartShape chart)
    {
        XNamespace cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        if (chart.Legend is not { } position)
        {
            // A null high-level value can mean that a preserved native legend
            // has not been materialized yet. Remove it only after an explicit
            // authoring command requested the clear.
            if (chart.ChartExLegendEditRequested)
                document.Root?.Element(cx + "chart")?.Element(cx + "legend")?.Remove();
            return;
        }

        var chartElement = document.Root?.Element(cx + "chart");
        if (chartElement is null)
            return;

        var legend = chartElement.Element(cx + "legend");
        if (legend is null)
        {
            chartElement.Add(BuildChartExLegend(
                position, chart.LegendOverlay, chart.LegendTextStyle, cx));
            return;
        }

        legend.SetAttributeValue("pos", ChartExLegendPosition(position));
        if (chart.LegendOverlay is { } overlay)
            legend.SetAttributeValue("overlay", overlay ? "1" : "0");

        if (chart.LegendTextStyle is not null)
        {
            legend.Element(cx + "txPr")?.Remove();
            var textProperties = BuildChartTextPropertiesEl(chart.LegendTextStyle, cx);
            if (textProperties is not null)
            {
                var anchor = legend.Elements().FirstOrDefault(element =>
                    element.Name == cx + "offset" || element.Name == cx + "extLst");
                if (anchor is null)
                    legend.Add(textProperties);
                else
                    anchor.AddBeforeSelf(textProperties);
            }
        }
    }

    private static void UpdateChartExTitle(XDocument document, ChartShape chart)
    {
        XNamespace cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        var chartElement = document.Root?.Element(cx + "chart");
        if (chartElement is null)
            return;

        var title = chartElement.Element(cx + "title");
        if (chart.Title is null)
        {
            // Preserve an untouched native title when the model has not
            // materialized it; explicit authoring clears set the edit marker.
            if (chart.ChartExTitleEditRequested)
                title?.Remove();
            return;
        }

        if (title is null)
        {
            chartElement.AddFirst(new XElement(cx + "title",
                chart.ChartExTitlePosition is { } titlePosition
                    ? new XAttribute("pos", ChartExTitlePositionToken(titlePosition))
                    : null,
                chart.ChartExTitleAlignment is { } titleAlignment
                    ? new XAttribute("align", ChartExTitleAlignmentToken(titleAlignment))
                    : null,
                chart.TitleOverlay is { } overlay
                    ? new XAttribute("overlay", overlay ? "1" : "0")
                    : null,
                new XElement(cx + "tx",
                    new XElement(cx + "txData",
                        new XElement(cx + "v", chart.Title))),
                BuildChartTextPropertiesEl(chart.TitleStyle, cx)));
            return;
        }

        var value = title.Descendants(cx + "v").FirstOrDefault();
        if (value is not null)
        {
            value.Value = chart.Title;
        }
        else
        {
            var richRuns = title.Descendants(A + "t").ToList();
            if (richRuns.Count > 0)
            {
                richRuns[0].Value = chart.Title;
                foreach (var run in richRuns.Skip(1))
                    run.Value = string.Empty;
            }
            else
            {
                var txData = title.Descendants(cx + "txData").FirstOrDefault();
                if (txData is not null)
                    txData.Add(new XElement(cx + "v", chart.Title));
            }
        }

        if (chart.TitleOverlay is { } overlayValue)
            title.SetAttributeValue("overlay", overlayValue ? "1" : "0");

        if (chart.ChartExTitlePosition is { } positionValue)
            title.SetAttributeValue("pos", ChartExTitlePositionToken(positionValue));
        if (chart.ChartExTitleAlignment is { } alignmentValue)
            title.SetAttributeValue("align", ChartExTitleAlignmentToken(alignmentValue));

        if (chart.TitleStyle is not null)
        {
            title.Element(cx + "txPr")?.Remove();
            var textProperties = BuildChartTextPropertiesEl(chart.TitleStyle, cx);
            if (textProperties is not null)
            {
                var anchor = title.Elements().FirstOrDefault(element =>
                    element.Name == cx + "offset" || element.Name == cx + "extLst");
                if (anchor is null)
                    title.Add(textProperties);
                else
                    anchor.AddBeforeSelf(textProperties);
            }
        }
    }

    private static bool IsWaterfallChartEx(XDocument document, ChartShape chart)
    {
        XNamespace cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        var layoutId = document
            .Descendants(cx + "series")
            .Select(series => series.Attribute("layoutId")?.Value)
            .FirstOrDefault();
        return string.Equals(layoutId, "waterfall", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(chart.ChartExLayoutId ?? layoutId, "waterfall", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Applies an explicit chart-data edit to a preserved native ChartEx payload.
    ///
    /// ChartEx families share the chartData dimensions, but their plot semantics do
    /// not. Keep this update deliberately narrow: only a single-series payload with
    /// the dimensions understood by the reader is safe to edit without inventing or
    /// deleting family-specific nodes. Ambiguous payloads remain verbatim rather than
    /// being silently downgraded by a generic classic-chart writer.
    /// </summary>
    private static void UpdateChartExData(XDocument document, ChartShape chart)
    {
        XNamespace cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        var dataElements = document.Descendants(cx + "chartData")
            .Elements(cx + "data")
            .ToList();
        var series = document.Descendants(cx + "plotAreaRegion")
            .Elements(cx + "series")
            .ToList();

        if (series.Count == 0 || series.Count != chart.Series.Count)
            return;

        var categoryData = FindChartExCategoryData(dataElements, cx);
        var categoryLevel = categoryData?.Element(cx + "strDim")?.Element(cx + "lvl");
        if (categoryLevel is null)
            return;

        var dataById = dataElements
            .Select(data => (Data: data, Id: TryParseChartExId(data.Attribute("id")?.Value)))
            .ToList();
        if (dataById.Any(item => item.Id is null) || dataById.Select(item => item.Id!.Value).Distinct().Count() != dataById.Count)
            return;

        var seriesValues = new List<XElement>(series.Count);
        foreach (var seriesElement in series)
        {
            var dataId = TryParseChartExId(seriesElement.Element(cx + "dataId")?.Attribute("val")?.Value);
            if (dataId is null)
                return;

            var referencedData = dataById
                .FirstOrDefault(item => item.Id == dataId)
                .Data;
            if (referencedData is null)
                return;

            var valueLevel = FindChartExValueDataLevel(referencedData, cx);
            if (valueLevel is null)
                return;

            seriesValues.Add(valueLevel);
        }

        ReplaceChartExPoints(categoryLevel, chart.Categories, static value => value);
        for (var index = 0; index < series.Count; index++)
        {
            ReplaceChartExPoints(
                seriesValues[index],
                chart.Series[index].Values,
                value => value?.ToString("G", CultureInfo.InvariantCulture));

            var valueElement = series[index].Element(cx + "tx")?.Element(cx + "txData")?.Element(cx + "v");
            if (valueElement is not null)
                valueElement.Value = chart.Series[index].Name ?? string.Empty;
        }
    }

    /// <summary>
    /// Applies only the explicitly modeled per-series ChartEx layout identifiers.
    /// Family-specific children remain verbatim; an absent model value leaves an
    /// existing native attribute untouched.
    /// </summary>
    private static void UpdateChartExSeriesLayouts(XDocument document, ChartShape chart)
    {
        XNamespace cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        var series = document.Descendants(cx + "plotAreaRegion")
            .Elements(cx + "series")
            .ToList();

        if (series.Count == 0 || series.Count != chart.Series.Count)
            return;

        for (var index = 0; index < series.Count; index++)
        {
            var layoutId = chart.Series[index].ChartExLayoutId;
            if (!string.IsNullOrWhiteSpace(layoutId))
                series[index].SetAttributeValue("layoutId", layoutId);
        }
    }

    private static void UpdateChartExValueColorScales(XDocument document, ChartShape chart)
    {
        XNamespace cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        var series = document.Descendants(cx + "plotAreaRegion")
            .Elements(cx + "series")
            .ToList();

        if (series.Count == 0 || series.Count != chart.Series.Count)
            return;

        for (var index = 0; index < series.Count; index++)
        {
            var scale = chart.Series[index].ValueColorScale;
            if (scale is null)
                continue;

            series[index].Element(cx + "valueColors")?.Remove();
            series[index].Element(cx + "valueColorPositions")?.Remove();

            var anchor = series[index].Elements().FirstOrDefault(element =>
                element.Name == cx + "dataPt"
                || element.Name == cx + "dataLabels"
                || element.Name == cx + "dataId"
                || element.Name == cx + "layoutPr");

            var valueColors = new XElement(cx + "valueColors");
            AddChartExValueColor(valueColors, "minColor", scale.MinColor, cx);
            AddChartExValueColor(valueColors, "midColor", scale.MidColor, cx);
            AddChartExValueColor(valueColors, "maxColor", scale.MaxColor, cx);
            if (!valueColors.IsEmpty)
                InsertBeforeOrAdd(series[index], anchor, valueColors);

            var positions = new XElement(cx + "valueColorPositions",
                new XAttribute("count", (scale.PositionCount is 2 or 3)
                    ? scale.PositionCount.Value.ToString(CultureInfo.InvariantCulture)
                    : scale.MidPosition is null ? "2" : "3"));
            AddChartExValueColorPosition(positions, "min", scale.MinPosition, cx);
            AddChartExValueColorPosition(positions, "mid", scale.MidPosition, cx);
            AddChartExValueColorPosition(positions, "max", scale.MaxPosition, cx);
            if (positions.Elements().Any())
                InsertBeforeOrAdd(series[index], anchor, positions);
        }
    }

    private static void UpdateChartExSeriesShapeProperties(XDocument document, ChartShape chart)
    {
        XNamespace cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        var series = document.Descendants(cx + "plotAreaRegion")
            .Elements(cx + "series")
            .ToList();

        if (series.Count == 0 || series.Count != chart.Series.Count)
            return;

        for (var index = 0; index < series.Count; index++)
        {
            var source = chart.Series[index];
            if (source.Fill is null && source.FillColor is null && source.LineStyle is null)
                continue;

            var shapeProperties = series[index].Element(cx + "spPr");
            if (shapeProperties is null)
            {
                shapeProperties = new XElement(cx + "spPr");
                var anchor = series[index].Elements().FirstOrDefault(element =>
                    element.Name == cx + "valueColors"
                    || element.Name == cx + "valueColorPositions"
                    || element.Name == cx + "dataPt"
                    || element.Name == cx + "dataLabels"
                    || element.Name == cx + "dataId"
                    || element.Name == cx + "layoutPr");
                InsertBeforeOrAdd(series[index], anchor, shapeProperties);
            }

            if (source.Fill is not null || source.FillColor is not null)
            {
                foreach (var child in shapeProperties.Elements()
                             .Where(element => element.Name == A + "noFill"
                                || element.Name == A + "solidFill"
                                || element.Name == A + "gradFill"
                                || element.Name == A + "pattFill")
                             .ToList())
                    child.Remove();

                var fill = BuildChartFillEl(source.Fill, source.FillColor);
                if (fill is not null)
                    shapeProperties.AddFirst(fill);
            }

            if (source.LineStyle is not null)
                MergeChartExSeriesLine(shapeProperties, source.LineStyle);
        }
    }

    private static void MergeChartExSeriesLine(XElement shapeProperties, ChartLineStyle style)
    {
        var modeled = BuildLineStyleEl(style);
        if (modeled is null)
            return;

        var line = shapeProperties.Element(A + "ln");
        if (line is null)
        {
            shapeProperties.Add(modeled);
            return;
        }

        line.Attribute("w")?.Remove();
        if (modeled.Attribute("w") is { } width)
            line.Add(new XAttribute("w", width.Value));

        foreach (var child in line.Elements()
                     .Where(element => element.Name == A + "noFill"
                        || element.Name == A + "solidFill"
                        || element.Name == A + "gradFill"
                        || element.Name == A + "pattFill"
                        || element.Name == A + "prstDash")
                     .ToList())
            child.Remove();

        foreach (var child in modeled.Elements())
            line.Add(new XElement(child));
    }

    private static void AddChartExValueColor(
        XElement parent,
        string name,
        ThemeAwareColor? color,
        XNamespace cx)
    {
        if (color is null)
            return;

        parent.Add(new XElement(cx + name,
            new XElement(A + "solidFill", BuildColorEl(color))));
    }

    private static void AddChartExValueColorPosition(
        XElement parent,
        string name,
        ChartValueColorPosition? position,
        XNamespace cx)
    {
        if (position is null)
            return;

        XElement value;
        if (position.IsExtreme)
            value = new XElement(cx + "extremeValue");
        else if (position.Number is double number)
            value = new XElement(cx + "number", new XAttribute("val", number.ToString("G", CultureInfo.InvariantCulture)));
        else if (position.Percent is double percent)
            value = new XElement(cx + "percent", new XAttribute("val", percent.ToString("G", CultureInfo.InvariantCulture)));
        else
            return;

        parent.Add(new XElement(cx + name, value));
    }

    private static void InsertBeforeOrAdd(XElement parent, XElement? anchor, XElement child)
    {
        if (anchor is not null)
            anchor.AddBeforeSelf(child);
        else
            parent.Add(child);
    }

    /// <summary>
    /// Updates only the modeled ChartEx series data-label payload. A null model
    /// value leaves the native element untouched so unsupported family metadata
    /// remains verbatim.
    /// </summary>
    private static void UpdateChartExSeriesDataLabels(XDocument document, ChartShape chart)
    {
        XNamespace cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        var series = document.Descendants(cx + "plotAreaRegion")
            .Elements(cx + "series")
            .ToList();

        if (series.Count == 0 || series.Count != chart.Series.Count)
            return;

        for (var index = 0; index < series.Count; index++)
        {
            var source = chart.Series[index];
            var pointLabels = source.PointStyles
                .Where(pair => pair.Value.DataLabels is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value.DataLabels!);
            if (source.DataLabels is null && pointLabels.Count == 0)
                continue;

            var element = series[index].Element(cx + "dataLabels");
            if (element is null)
            {
                element = new XElement(cx + "dataLabels");
                var dataId = series[index].Element(cx + "dataId");
                if (dataId is not null)
                    dataId.AddBeforeSelf(element);
                else
                    series[index].Add(element);
            }

            foreach (var childName in new[]
                     { "numFmt", "txPr", "visibility", "separator", "dataLabel", "dataLabelHidden" })
            {
                element.Elements(cx + childName).Remove();
            }
            element.Attribute("pos")?.Remove();
            var labels = source.DataLabels;
            if (labels is not null)
                AddChartExDataLabelContent(element, labels, cx);

            foreach (var pair in pointLabels.OrderBy(pair => pair.Key))
            {
                var point = pair.Value;
                if (point.Delete == true)
                {
                    element.Add(new XElement(cx + "dataLabelHidden",
                        new XAttribute("idx", pair.Key)));
                    continue;
                }

                element.Add(new XElement(cx + "dataLabel",
                    new XAttribute("idx", pair.Key)));
                AddChartExDataLabelContent(
                    element.Elements(cx + "dataLabel").Last(), point, cx);
            }
        }
    }

    /// <summary>
    /// Updates only modeled native ChartEx point shape properties. A point with
    /// no fill/stroke edit remains verbatim, including its extension payload.
    /// </summary>
    private static void UpdateChartExSeriesDataPoints(XDocument document, ChartShape chart)
    {
        XNamespace cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        var series = document.Descendants(cx + "plotAreaRegion")
            .Elements(cx + "series")
            .ToList();

        if (series.Count == 0 || series.Count != chart.Series.Count)
            return;

        for (var index = 0; index < series.Count; index++)
        {
            var source = chart.Series[index];
            var pointStyles = source.PointStyles
                .Where(pair => HasChartExPointShapeFormatting(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            if (pointStyles.Count == 0)
                continue;

            foreach (var pair in pointStyles.OrderBy(pair => pair.Key))
            {
                var point = series[index].Elements(cx + "dataPt")
                    .FirstOrDefault(element =>
                        TryParseChartExId(element.Attribute("idx")?.Value) == pair.Key);
                if (point is null)
                {
                    point = new XElement(cx + "dataPt",
                        new XAttribute("idx", pair.Key));
                    var anchor = series[index].Elements()
                        .FirstOrDefault(element =>
                            element.Name == cx + "dataLabels"
                            || element.Name == cx + "dataId");
                    if (anchor is not null)
                        anchor.AddBeforeSelf(point);
                    else
                        series[index].Add(point);
                }

                var shapeProperties = BuildPointShapePropertiesEl(null, pair.Value);
                if (shapeProperties is null)
                    continue;

                var chartExShapeProperties = new XElement(cx + "spPr", shapeProperties.Elements());
                var existing = point.Element(cx + "spPr");
                if (existing is not null)
                    existing.ReplaceWith(chartExShapeProperties);
                else
                    point.AddFirst(chartExShapeProperties);
            }
        }
    }

    private static bool HasChartExPointShapeFormatting(ChartPointStyle style) =>
        style.Fill is not null
        || style.FillColor is not null
        || style.StrokeColor is not null
        || style.StrokeWidthPt is not null;

    private static void AddChartExDataLabelContent(
        XElement element,
        ChartDataLabels labels,
        XNamespace cx)
    {
        if (labels.NumberFormat is not null)
            element.Add(new XElement(cx + "numFmt",
                new XAttribute("formatCode", labels.NumberFormat),
                new XAttribute("sourceLinked", "0")));

        var textProperties = BuildChartTextPropertiesEl(labels.TextStyle);
        if (textProperties is not null)
            element.Add(new XElement(cx + "txPr", textProperties.Nodes()));

        if (labels.ShowSeriesName
            || labels.ShowCategoryName
            || labels.ShowValue
            || labels.ShowPercent
            || labels.ShowLegendKey
            || labels.ShowBubbleSize
            || labels.ShowLeaderLines.HasValue)
        {
            var visibility = new XElement(cx + "visibility");
            if (labels.ShowSeriesName)
                visibility.SetAttributeValue("seriesName", "true");
            if (labels.ShowCategoryName)
                visibility.SetAttributeValue("categoryName", "true");
            if (labels.ShowValue)
                visibility.SetAttributeValue("value", "true");
            if (labels.ShowPercent)
                visibility.SetAttributeValue("percent", "true");
            if (labels.ShowLegendKey)
                visibility.SetAttributeValue("legendKey", "true");
            if (labels.ShowBubbleSize)
                visibility.SetAttributeValue("bubbleSize", "true");
            if (labels.ShowLeaderLines is { } leaderLines)
                visibility.SetAttributeValue("leaderLines", leaderLines ? "true" : "false");
            element.Add(visibility);
        }

        if (labels.Separator is not null)
            element.Add(new XElement(cx + "separator", labels.Separator));

        if (labels.Position is { } position)
        {
            var token = position switch
            {
                DataLabelPosition.Center => "ctr",
                DataLabelPosition.InsideEnd => "inEnd",
                DataLabelPosition.OutsideEnd => "outEnd",
                DataLabelPosition.InsideBase => "inBase",
                DataLabelPosition.BestFit => "bestFit",
                DataLabelPosition.Above => "t",
                DataLabelPosition.Below => "b",
                DataLabelPosition.Left => "l",
                DataLabelPosition.Right => "r",
                _ => null
            };
            if (token is not null)
                element.SetAttributeValue("pos", token);
        }
    }

    private static XElement? FindChartExCategoryData(
        IReadOnlyList<XElement> dataElements,
        XNamespace cx)
    {
        var stringData = dataElements
            .Where(data => data.Element(cx + "strDim") is not null)
            .ToList();
        if (stringData.Count == 1)
            return stringData[0];

        var categoryData = stringData
            .Where(data => string.Equals(
                data.Element(cx + "strDim")?.Attribute("type")?.Value,
                "cat",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        return categoryData.Count == 1 ? categoryData[0] : null;
    }

    private static XElement? FindChartExValueDataLevel(XElement data, XNamespace cx)
    {
        var numericDimensions = data.Elements(cx + "numDim").ToList();
        if (numericDimensions.Count == 1)
            return numericDimensions[0].Element(cx + "lvl");

        var valueDimension = numericDimensions
            .Where(dimension => string.Equals(
                dimension.Attribute("type")?.Value,
                "val",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        return valueDimension.Count == 1 ? valueDimension[0].Element(cx + "lvl") : null;
    }

    private static int? TryParseChartExId(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;

    private static void ReplaceChartExPoints<T>(
        XElement level,
        IEnumerable<T> values,
        Func<T, string?> format)
    {
        var materialized = values.ToList();
        level.SetAttributeValue("ptCount", materialized.Count);
        level.Elements().Where(element => element.Name == level.Name.Namespace + "pt").Remove();

        var pointIndex = 0;
        foreach (var value in materialized)
        {
            var text = format(value);
            if (text is null)
            {
                pointIndex++;
                continue;
            }

            level.Add(new XElement(level.Name.Namespace + "pt",
                new XAttribute("idx", pointIndex), text));
            pointIndex++;
        }
    }

    private static void UpdateChartExSemantics(XDocument document, ChartShape chart)
    {
        XNamespace cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        var series = document.Descendants(cx + "series").FirstOrDefault();
        if (series is null)
            return;

        var layoutPr = series.Element(cx + "layoutPr");
        if (layoutPr is null)
        {
            layoutPr = new XElement(cx + "layoutPr");
            series.Add(layoutPr);
        }

        var visibility = layoutPr.Element(cx + "visibility");
        if (visibility is null)
        {
            visibility = new XElement(cx + "visibility");
            layoutPr.AddFirst(visibility);
        }
        visibility.SetAttributeValue("connectorLines", chart.ShowWaterfallConnectorLines ? "1" : "0");

        layoutPr.Element(cx + "subtotals")?.Remove();
        if (chart.WaterfallTotalPointIndices is { Count: > 0 } totals)
            layoutPr.Add(new XElement(cx + "subtotals",
                totals.Distinct().OrderBy(index => index)
                    .Select(index => new XElement(cx + "idx", new XAttribute("val", index)))));
    }

    private static XElement BuildChartExLayoutPr(ChartShape chart, XNamespace cx) =>
        new XElement(cx + "layoutPr",
            new XElement(cx + "visibility",
                new XAttribute("connectorLines", chart.ShowWaterfallConnectorLines ? "1" : "0")),
            chart.WaterfallTotalPointIndices is { Count: > 0 } totals
                ? new XElement(cx + "subtotals",
                    totals.Distinct().OrderBy(index => index)
                        .Select(index => new XElement(cx + "idx", new XAttribute("val", index))))
                : null);

    internal static string GetRegeneratedWorkbookPath(int chartIndex) =>
        $"ppt/embeddings/chartWorkbook{chartIndex}.xlsx";

    // ── chart.xml ────────────────────────────────────────────────────────────

    private static XDocument BuildChartDoc(ChartShape chart)
    {
        var plotArea = BuildPlotArea(chart);
        var legendEl = BuildLegendEl(chart);

        var titleEl = chart.Title is not null && !chart.HasAutomaticTitle
            ? BuildTitleEl(chart.Title, chart.TitleStyle, overlay: chart.TitleOverlay == true)
            : null;

        var chartSpace = new XElement(C + "chartSpace",
            NsAttr("c", C), NsAttr("a", A), NsAttr("r", R),
            chart.ChartDate1904 is { } date1904
                ? new XElement(C + "date1904", new XAttribute("val", BoolValue(date1904)))
                : null,
            string.IsNullOrWhiteSpace(chart.ChartLanguage)
                ? null
                : new XElement(C + "lang", new XAttribute("val", chart.ChartLanguage)),
            chart.RoundedCorners is { } roundedCorners
                ? new XElement(C + "roundedCorners", new XAttribute("val", BoolValue(roundedCorners)))
                : null,
            chart.StyleId is { } styleId
                ? new XElement(C + "style", new XAttribute("val", styleId))
                : null,
            TryParsePreservedPivotSource(chart.PreservedPivotSourceXml),
            BuildChartProtectionEl(chart),
            new XElement(C + "chart",
                titleEl,
                new XElement(C + "autoTitleDeleted", new XAttribute("val", chart.Title is null ? "1" : "0")),
                BuildView3DEl(chart),
                plotArea,
                legendEl,
                new XElement(C + "plotVisOnly", new XAttribute("val", BoolValue(chart.PlotVisibleOnly ?? true))),
                BuildDisplayBlanksAsEl(chart),
                BuildShowDataLabelsOverMaximumEl(chart)),
            BuildChartShapePropertiesEl(chart.ChartAreaFill, chart.ChartAreaOutline),
            BuildChartTextPropertiesEl(chart.TextStyle),
            BuildWaterfallTotalsExtension(chart),
            TryParsePreservedChartSpaceExtensions(chart.PreservedChartSpaceExtensionsXml));

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            chartSpace);
    }

    private static XElement? TryParsePreservedChartSpaceExtensions(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var extensionList = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            return extensionList.Name == C + "extLst" ? extensionList : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static XElement? TryParsePreservedPivotSource(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var pivotSource = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            return pivotSource.Name == C + "pivotSource" ? pivotSource : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static XElement? TryParsePreservedChartProtection(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var protection = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            return protection.Name == C + "protection" ? protection : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static XElement? BuildChartProtectionEl(ChartShape chart)
    {
        var protection = TryParsePreservedChartProtection(chart.PreservedChartProtectionXml);
        if (protection is null &&
            chart.ChartObjectProtected is null &&
            chart.ChartDataProtected is null &&
            chart.ChartFormattingProtected is null &&
            chart.ChartSelectionProtected is null)
        {
            return null;
        }

        protection ??= new XElement(C + "protection");
        SetProtectionAttribute(protection, "chartObject", chart.ChartObjectProtected);
        SetProtectionAttribute(protection, "data", chart.ChartDataProtected);
        SetProtectionAttribute(protection, "formatting", chart.ChartFormattingProtected);
        SetProtectionAttribute(protection, "selection", chart.ChartSelectionProtected);
        return protection;
    }

    private static void SetProtectionAttribute(XElement protection, string name, bool? value)
    {
        if (value is { } explicitValue)
            protection.SetAttributeValue(name, BoolValue(explicitValue));
    }

    private static XElement? BuildChartShapePropertiesEl(ShapeFill? fill, ShapeOutline? outline)
    {
        var fillEl = BuildChartAreaFillEl(fill);
        var lineEl = BuildChartAreaOutlineEl(outline);
        return fillEl is null && lineEl is null ? null : new XElement(C + "spPr", fillEl, lineEl);
    }

    private static XElement? BuildView3DEl(ChartShape chart)
    {
        if (chart.View3D is not { } view) return null;

        return new XElement(C + "view3D",
            OptionalIntElement("rotX", view.RotationX),
            OptionalIntElement("hPercent", view.HeightPercent),
            OptionalIntElement("rotY", view.RotationY),
            OptionalIntElement("depthPercent", view.DepthPercent),
            OptionalBoolElement("rAngAx", view.RightAngleAxes),
            OptionalIntElement("perspective", view.Perspective));
    }

    private static XElement? OptionalIntElement(string name, int? value) =>
        value is { } v ? new XElement(C + name, new XAttribute("val", v)) : null;

    private static XElement? OptionalBoolElement(string name, bool? value) =>
        value is { } v ? new XElement(C + name, new XAttribute("val", BoolValue(v))) : null;

    private static XElement BuildTitleEl(
        string title,
        ChartTextStyle? style = null,
        bool overlay = false) =>
        new XElement(C + "title",
            new XElement(C + "tx",
                new XElement(C + "rich",
                    new XElement(A + "bodyPr"),
                    new XElement(A + "lstStyle"),
                    new XElement(A + "p",
                        style is null ? null : new XElement(A + "pPr", BuildChartDefaultRunPropertiesEl(style)),
                        new XElement(A + "r",
                            new XElement(A + "t", title))))),
            new XElement(C + "overlay", new XAttribute("val", BoolValue(overlay))));

    // Axis ID constants for the primary and secondary axis pairs.
    // Primary: catAx id=1, valAx id=2. Secondary: catAx id=4 (hidden), valAx id=3.
    private const int PrimaryCatAxId   = 1;
    private const int PrimaryValAxId   = 2;
    private const int SecondaryValAxId = 3;
    private const int SecondaryCatAxId = 4;  // hidden phantom cat axis for the secondary plot group
    private const int PrimarySerAxId   = 5;

    private static XElement BuildPlotArea(ChartShape chart)
    {
        bool isScatterLike = chart.ChartType is ChartType.Scatter or ChartType.Bubble;
        bool noCatAx       = chart.ChartType is ChartType.Pie or ChartType.Doughnut or ChartType.OfPie or ChartType.Funnel or ChartType.Unknown;

        // CA1: split series by OnSecondaryAxis only when there IS a SecondaryValueAxis and at
        // least one secondary series. All other charts use a single group (no regression).
        bool hasSecondary = chart.SecondaryValueAxis is not null
                            && !noCatAx
                            && !isScatterLike
                            && chart.Series.Any(s => s.OnSecondaryAxis);

        var primarySeries = hasSecondary
            ? chart.Series.Where(s => !s.OnSecondaryAxis).ToList()
            : chart.Series.ToList();
        var secondarySeries = hasSecondary
            ? chart.Series.Where(s => s.OnSecondaryAxis).ToList()
            : new List<ChartSeries>();

        // Build series elements; re-index by their global position so idx/order stay consistent.
        int serOffset = 0;
        List<XElement> primarySeriesEls;
        if (isScatterLike)
            primarySeriesEls = primarySeries.Select((s, i) => BuildScatterSeriesEl(chart, s, serOffset + i)).ToList();
        else
            primarySeriesEls = primarySeries
                .Select((s, i) => BuildSeriesEl(chart, s, serOffset + i, SeriesSchemaFor(chart.ChartType))).ToList();
        serOffset += primarySeries.Count;

        // The secondary plot group is always a c:lineChart (below), so its series are
        // CT_LineSer regardless of what the primary chart type is.
        var secondarySeriesEls = secondarySeries
            .Select((s, i) => BuildSeriesEl(chart, s, serOffset + i, SeriesSchema.Line)).ToList();

        // Build the primary chart-type element (references the primary axis pair).
        XElement? primaryChartTypeEl = BuildChartTypeEl(
            chart, primarySeriesEls, isScatterLike,
            catAxId: PrimaryCatAxId, valAxId: PrimaryValAxId);

        // CA1: inject chart-level data labels into the PRIMARY plot-type element only.
        if (primaryChartTypeEl is not null)
        {
            var chartDlblsEl = BuildDataLabelsEl(chart.DataLabels, chart.ChartType);
            if (chartDlblsEl is not null)
                AddDataLabelsInSchemaOrder(primaryChartTypeEl, chartDlblsEl);
        }

        // CA1: secondary plot group — always a lineChart referencing the secondary axis pair.
        // PowerPoint requires every valAx to be referenced by at least one plot group.
        XElement? secondaryChartTypeEl = null;
        if (hasSecondary)
        {
            secondaryChartTypeEl = new XElement(C + "lineChart",
                new XElement(C + "grouping",   new XAttribute("val", "standard")),
                new XElement(C + "varyColors", new XAttribute("val", "0")),
                secondarySeriesEls,
                new XElement(C + "axId", new XAttribute("val", SecondaryCatAxId)),
                new XElement(C + "axId", new XAttribute("val", SecondaryValAxId)));
        }

        // ── Primary axis elements ─────────────────────────────────────────────
        var catAxEl  = !noCatAx && !isScatterLike
            ? BuildCatAxEl(chart.CategoryAxis, PrimaryCatAxId, PrimaryValAxId)
            : null;
        var valAxEl  = !noCatAx
            ? BuildValAxEl(chart.ValueAxis, PrimaryValAxId, PrimaryCatAxId)
            : null;
        var serAxEl = chart.ChartType is ChartType.Surface or ChartType.Surface3D
            ? BuildSerAxEl(PrimarySerAxId, PrimaryValAxId)
            : null;
        // Scatter/bubble X value axis lives at bottom (axPos="b").
        var xValAxEl = isScatterLike
            ? BuildValAxEl(chart.CategoryAxis, PrimaryCatAxId, PrimaryValAxId, axPos: "b")
            : null;

        // ── Secondary axis elements (only when a real secondary group was emitted) ─
        // Secondary catAx: hidden phantom axis (delete=1) so PowerPoint knows what axis the
        // secondary valAx crosses; it must appear before the secondary valAx in the plotArea.
        XElement? secCatAxEl = null;
        XElement? secValAxEl = null;
        if (hasSecondary)
        {
            secCatAxEl = new XElement(C + "catAx",
                new XElement(C + "axId",  new XAttribute("val", SecondaryCatAxId)),
                new XElement(C + "scaling",
                    new XElement(C + "orientation", new XAttribute("val", "minMax"))),
                new XElement(C + "delete",  new XAttribute("val", "1")),  // hidden
                new XElement(C + "axPos",   new XAttribute("val", "b")),
                new XElement(C + "crossAx", new XAttribute("val", SecondaryValAxId)));

            secValAxEl = BuildValAxEl(
                chart.SecondaryValueAxis!, SecondaryValAxId, SecondaryCatAxId,
                axPos: "r", crosses: "max");
        }

        var dataTableEl = BuildDataTableEl(chart.DataTable);

        return new XElement(C + "plotArea",
            BuildManualLayoutEl(chart.PlotAreaManualLayout),
            primaryChartTypeEl,
            secondaryChartTypeEl,
            xValAxEl,
            catAxEl,
            valAxEl,
            serAxEl,
            secCatAxEl,
            secValAxEl,
            dataTableEl,
            BuildChartShapePropertiesEl(chart.PlotAreaFill, chart.PlotAreaOutline));
    }

    private static XElement? BuildLegendEl(ChartShape chart)
    {
        if (!chart.Legend.HasValue)
            return null;

        return new XElement(C + "legend",
            new XElement(C + "legendPos",
                new XAttribute("val", chart.Legend.Value switch
                {
                    LegendPosition.Left   => "l",
                    LegendPosition.Top    => "t",
                    LegendPosition.Bottom => "b",
                    _                     => "r"
                })),
            BuildManualLayoutEl(chart.LegendManualLayout),
            chart.LegendOverlay.HasValue
                ? new XElement(C + "overlay", new XAttribute("val", BoolValue(chart.LegendOverlay.Value)))
                : null,
            BuildChartTextPropertiesEl(chart.LegendTextStyle));
    }

    private static XElement? BuildManualLayoutEl(ChartManualLayout? layout)
    {
        if (layout is null)
            return null;

        var manualLayout = new XElement(C + "manualLayout");
        if (!string.IsNullOrWhiteSpace(layout.LayoutTarget))
            manualLayout.Add(new XElement(C + "layoutTarget", new XAttribute("val", layout.LayoutTarget)));

        manualLayout.Add(
            new XElement(C + "xMode", new XAttribute("val", ToManualLayoutModeValue(layout.XMode, layout.RawXModeToken))),
            new XElement(C + "yMode", new XAttribute("val", ToManualLayoutModeValue(layout.YMode, layout.RawYModeToken))),
            new XElement(C + "wMode", new XAttribute("val", ToManualLayoutModeValue(layout.WidthMode, layout.RawWidthModeToken))),
            new XElement(C + "hMode", new XAttribute("val", ToManualLayoutModeValue(layout.HeightMode, layout.RawHeightModeToken))),
            ManualLayoutValueEl("x", layout.X),
            ManualLayoutValueEl("y", layout.Y),
            ManualLayoutValueEl("w", layout.Width),
            ManualLayoutValueEl("h", layout.Height));

        return manualLayout.HasElements
            ? new XElement(C + "layout", manualLayout)
            : null;
    }

    private static XElement? ManualLayoutValueEl(string localName, double? value) =>
        value.HasValue
            ? new XElement(C + localName, new XAttribute("val", value.Value.ToString("G", CultureInfo.InvariantCulture)))
            : null;

    private static string ToManualLayoutModeValue(ChartManualLayoutMode mode, string? rawToken) =>
        mode == ChartManualLayoutMode.Unsupported && !string.IsNullOrWhiteSpace(rawToken)
            ? rawToken
            : mode == ChartManualLayoutMode.Edge ? "edge" : "factor";

    /// <summary>Dispatches to the correct chart-type builder using the given axId pair.</summary>
    private static XElement? BuildChartTypeEl(
        ChartShape chart, List<XElement> seriesEls, bool isScatterLike,
        int catAxId, int valAxId)
    {
        var chartType = chart.ChartType switch
        {
            ChartType.BarClustered or ChartType.BarStacked or ChartType.BarStacked100 =>
                BuildBarChartEl(chart, seriesEls, isBar: true,  catAxId, valAxId),
            ChartType.ColumnClustered or ChartType.ColumnStacked or ChartType.ColumnStacked100 =>
                BuildBarChartEl(chart, seriesEls, isBar: false, catAxId, valAxId),
            ChartType.Line or ChartType.LineMarkers =>
                BuildLineChartEl(chart, seriesEls, catAxId, valAxId),
            ChartType.Pie =>
                BuildPieChartEl(chart, seriesEls),
            ChartType.OfPie =>
                BuildOfPieChartEl(chart, seriesEls),
            ChartType.Doughnut =>
                BuildDoughnutChartEl(chart, seriesEls),
            ChartType.Area or ChartType.AreaStacked =>
                BuildAreaChartEl(chart, seriesEls, catAxId, valAxId),
            ChartType.Scatter =>
                BuildScatterChartEl(chart, seriesEls, catAxId, valAxId),
            ChartType.Bubble =>
                BuildBubbleChartEl(chart, seriesEls, catAxId, valAxId),
            ChartType.Radar =>
                BuildRadarChartEl(chart, seriesEls, catAxId, valAxId),
            ChartType.Stock =>
                BuildStockChartEl(chart, seriesEls, catAxId, valAxId),
            ChartType.Funnel =>
                BuildFunnelChartEl(chart, seriesEls),
            ChartType.Waterfall =>
                BuildWaterfallChartEl(chart, seriesEls),
            ChartType.Surface or ChartType.Surface3D =>
                BuildSurfaceChartEl(chart, seriesEls, catAxId, valAxId, PrimarySerAxId),
            _ =>
                BuildBarChartEl(chart, seriesEls, isBar: false, catAxId, valAxId)
        };

        if (chart.SeriesLinesSpecified && chart.ChartType != ChartType.OfPie)
        {
            var lastSeries = chartType.Elements(C + "ser").LastOrDefault();
            var seriesLines = BuildSeriesLinesEl(chart);
            if (lastSeries is not null)
                lastSeries.AddAfterSelf(seriesLines);
            else
                chartType.AddFirst(seriesLines);
        }

        return chartType;
    }

    private static XElement BuildFunnelChartEl(ChartShape chart, List<XElement> seriesEls) =>
        new(
            C + "funnelChart",
            BuildVaryColorsEl(chart),
            seriesEls,
            chart.BarGapWidthPercent is { } gapWidth
                ? new XElement(C + "gapWidth", new XAttribute("val", Math.Clamp(gapWidth, 0, 500)))
                : null);

    private static XElement BuildWaterfallChartEl(ChartShape chart, List<XElement> seriesEls) =>
        new(
            C + "waterfallChart",
            BuildVaryColorsEl(chart),
            seriesEls,
            new XElement(C + "showConnectorLines", new XAttribute("val", BoolValue(chart.ShowWaterfallConnectorLines))),
            chart.BarGapWidthPercent is { } gapWidth
                ? new XElement(C + "gapWidth", new XAttribute("val", Math.Clamp(gapWidth, 0, 500)))
                : null);

    private static XElement? BuildWaterfallTotalsExtension(ChartShape chart)
    {
        if (chart.ChartType != ChartType.Waterfall || chart.WaterfallTotalPointIndices is not { } totals)
            return null;

        XNamespace freep = "http://freex.dev/freep/2026/presentation";
        return new XElement(C + "extLst",
            new XElement(C + "ext",
                new XAttribute("uri", "{B8A4E8F4-6B9E-4A4B-9F7D-0D3F9C9C6A11}"),
                new XElement(freep + "waterfallTotals",
                    totals.Distinct().OrderBy(index => index)
                        .Select(index => new XElement(freep + "idx", new XAttribute("val", index))))));
    }

    private static XElement BuildSeriesLinesEl(ChartShape chart)
    {
        var spPr = chart.SeriesLineStyle is null
            ? null
            : new XElement(C + "spPr", BuildLineStyleEl(chart.SeriesLineStyle));
        return new XElement(C + "serLines", spPr);
    }

    private static XElement BuildBarChartEl(ChartShape chart, List<XElement> seriesEls, bool isBar,
        int catAxId = PrimaryCatAxId, int valAxId = PrimaryValAxId)
    {
        var grouping = chart.ChartType switch
        {
            ChartType.ColumnStacked or ChartType.BarStacked => "stacked",
            ChartType.ColumnStacked100 or ChartType.BarStacked100 => "percentStacked",
            _ => "clustered"
        };

        var chartElementName = chart.ThreeDStyle is ChartThreeDStyle.Column or ChartThreeDStyle.Bar ||
            chart.BarGapDepthPercent.HasValue ? "bar3DChart" : "barChart";

        return new XElement(C + chartElementName,
            new XElement(C + "barDir", new XAttribute("val", isBar ? "bar" : "col")),
            new XElement(C + "grouping", new XAttribute("val", grouping)),
            BuildVaryColorsEl(chart),
            seriesEls,
            BuildBarGapWidthEl(chart),
            chart.BarGapDepthPercent.HasValue ? null : BuildBarOverlapEl(chart),
            BuildBarGapDepthEl(chart),
            new XElement(C + "axId", new XAttribute("val", catAxId)),
            new XElement(C + "axId", new XAttribute("val", valAxId)));
    }

    private static XElement? BuildBarGapWidthEl(ChartShape chart) =>
        chart.BarGapWidthPercent is { } value
            ? new XElement(C + "gapWidth", new XAttribute("val", Math.Clamp(value, 0, 500)))
            : null;

    private static XElement? BuildBarOverlapEl(ChartShape chart) =>
        chart.BarOverlapPercent is { } value
            ? new XElement(C + "overlap", new XAttribute("val", Math.Clamp(value, -100, 100)))
            : null;

    private static XElement? BuildBarGapDepthEl(ChartShape chart) =>
        chart.BarGapDepthPercent is { } value
            ? new XElement(C + "gapDepth", new XAttribute("val", Math.Clamp(value, 0, 500)))
            : null;

    private static XElement BuildLineChartEl(ChartShape chart, List<XElement> seriesEls,
        int catAxId = PrimaryCatAxId, int valAxId = PrimaryValAxId) =>
        new XElement(C + (chart.ThreeDStyle == ChartThreeDStyle.Line ? "line3DChart" : "lineChart"),
            new XElement(C + "grouping", new XAttribute("val", "standard")),
            BuildVaryColorsEl(chart),
            seriesEls,
            chart.ShowDropLines ? new XElement(C + "dropLines") : null,
            chart.ShowUpDownBars
                ? new XElement(C + "upDownBars",
                    chart.UpDownBarGapWidthPercent is { } gapWidth
                        ? new XElement(C + "gapWidth", new XAttribute("val", Math.Clamp(gapWidth, 0, 500)))
                        : null,
                    new XElement(C + "upBars",
                        chart.UpBarFill is null ? null : new XElement(C + "spPr", BuildChartFillEl(chart.UpBarFill, null))),
                    new XElement(C + "downBars",
                        chart.DownBarFill is null ? null : new XElement(C + "spPr", BuildChartFillEl(chart.DownBarFill, null))))
                : null,
            new XElement(C + "axId", new XAttribute("val", catAxId)),
            new XElement(C + "axId", new XAttribute("val", valAxId)));

    private static XElement BuildPieChartEl(ChartShape chart, List<XElement> seriesEls) =>
        new XElement(C + (chart.ThreeDStyle == ChartThreeDStyle.Pie ? "pie3DChart" : "pieChart"),
            BuildVaryColorsEl(chart),
            seriesEls,
            BuildFirstSliceAngleEl(chart),
            chart.LeaderLinesSpecified ? new XElement(C + "leaderLines") : null);

    private static XElement BuildOfPieChartEl(ChartShape chart, List<XElement> seriesEls) =>
        new XElement(C + "ofPieChart",
            new XElement(C + "ofPieType", new XAttribute("val", chart.OfPieType == OfPieType.Bar ? "bar" : "pie")),
            BuildVaryColorsEl(chart),
            seriesEls,
            chart.BarGapWidthPercent is { } gapWidth
                ? new XElement(C + "gapWidth", new XAttribute("val", Math.Clamp(gapWidth, 0, 500)))
                : null,
            chart.OfPieSplitType is { } splitType
                ? new XElement(C + "splitType", new XAttribute("val", splitType switch
                {
                    OfPieSplitType.Custom => "cust",
                    OfPieSplitType.Percent => "percent",
                    OfPieSplitType.Position => "pos",
                    OfPieSplitType.Value => "val",
                    _ => "auto"
                }))
                : null,
            chart.OfPieSplitPosition is { } splitPosition
                ? new XElement(C + "splitPos", new XAttribute("val", splitPosition.ToString("G", CultureInfo.InvariantCulture)))
                : null,
            chart.OfPieCustomPointIndices.Count > 0
                ? new XElement(C + "custSplit",
                    chart.OfPieCustomPointIndices
                        .Distinct()
                        .Where(index => index >= 0)
                        .Select(index => new XElement(C + "secondPiePt", new XAttribute("val", index))))
                : null,
            chart.OfPieSecondPieSizePercent is { } secondPieSize
                ? new XElement(C + "secondPieSize", new XAttribute("val", Math.Clamp(secondPieSize, 5, 200)))
                : null,
            chart.OfPieSeriesLinesSpecified ? new XElement(C + "serLines") : null,
            chart.LeaderLinesSpecified ? new XElement(C + "leaderLines") : null);

    private static XElement BuildAreaChartEl(ChartShape chart, List<XElement> seriesEls,
        int catAxId = PrimaryCatAxId, int valAxId = PrimaryValAxId) =>
        new XElement(C + (chart.ThreeDStyle == ChartThreeDStyle.Area ? "area3DChart" : "areaChart"),
            new XElement(C + "grouping",
                new XAttribute("val", chart.ChartType == ChartType.AreaStacked ? "stacked" : "standard")),
            BuildVaryColorsEl(chart),
            seriesEls,
            new XElement(C + "axId", new XAttribute("val", catAxId)),
            new XElement(C + "axId", new XAttribute("val", valAxId)));

    private static XElement BuildScatterChartEl(ChartShape chart, List<XElement> seriesEls,
        int catAxId = PrimaryCatAxId, int valAxId = PrimaryValAxId) =>
        new XElement(C + "scatterChart",
            new XElement(C + "scatterStyle",
                new XAttribute("val", chart.ScatterStyle switch
                {
                    ScatterStyle.Marker       => "marker",
                    ScatterStyle.Line         => "line",
                    ScatterStyle.Smooth       => "smooth",
                    ScatterStyle.SmoothMarker => "smoothMarker",
                    _                         => "lineMarker"
                })),
            BuildVaryColorsEl(chart),
            seriesEls,
            new XElement(C + "axId", new XAttribute("val", catAxId)),
            new XElement(C + "axId", new XAttribute("val", valAxId)));

    private static XElement BuildDoughnutChartEl(ChartShape chart, List<XElement> seriesEls) =>
        new XElement(C + "doughnutChart",
            BuildVaryColorsEl(chart),
            seriesEls,
            BuildFirstSliceAngleEl(chart),
            new XElement(C + "holeSize",
                new XAttribute("val", chart.DoughnutHolePercent.ToString(CultureInfo.InvariantCulture))),
            chart.LeaderLinesSpecified ? new XElement(C + "leaderLines") : null);

    private static XElement? BuildFirstSliceAngleEl(ChartShape chart)
    {
        if (chart.ChartType is not (ChartType.Pie or ChartType.Doughnut) ||
            chart.FirstSliceAngleDegrees is not { } angle)
            return null;

        return new XElement(C + "firstSliceAng",
            new XAttribute("val", Math.Clamp(angle, 0, 360).ToString(CultureInfo.InvariantCulture)));
    }

    private static XElement BuildRadarChartEl(ChartShape chart, List<XElement> seriesEls,
        int catAxId = PrimaryCatAxId, int valAxId = PrimaryValAxId) =>
        new XElement(C + "radarChart",
            new XElement(C + "radarStyle",
                new XAttribute("val", chart.RadarStyle switch
                {
                    RadarStyle.Marker => "marker",
                    RadarStyle.Filled => "filled",
                    _                 => "standard"
                })),
            BuildVaryColorsEl(chart),
            seriesEls,
            new XElement(C + "axId", new XAttribute("val", catAxId)),
            new XElement(C + "axId", new XAttribute("val", valAxId)));

    private static XElement BuildBubbleChartEl(ChartShape chart, List<XElement> seriesEls,
        int catAxId = PrimaryCatAxId, int valAxId = PrimaryValAxId) =>
        new XElement(C + "bubbleChart",
            BuildVaryColorsEl(chart),
            seriesEls,
            BuildBubbleScaleEl(chart),
            new XElement(C + "showNegBubbles", new XAttribute("val", chart.ShowNegativeBubbles ? "1" : "0")),
            new XElement(C + "sizeRepresents", new XAttribute("val",
                chart.BubbleSizeRepresents == BubbleSizeRepresentation.Width ? "w" : "area")),
            new XElement(C + "axId", new XAttribute("val", catAxId)),
            new XElement(C + "axId", new XAttribute("val", valAxId)));

    private static XElement BuildBubbleScaleEl(ChartShape chart) =>
        new(C + "bubbleScale",
            new XAttribute("val", Math.Clamp(chart.BubbleScalePercent, 0, 300).ToString(CultureInfo.InvariantCulture)));

    private static XElement BuildStockChartEl(ChartShape chart, List<XElement> seriesEls,
        int catAxId = PrimaryCatAxId, int valAxId = PrimaryValAxId) =>
        new XElement(C + "stockChart",
            BuildVaryColorsEl(chart),
            seriesEls,
            chart.ShowDropLines ? new XElement(C + "dropLines") : null,
            chart.HasHighLowLines ? new XElement(C + "hiLowLines") : null,
            BuildStockUpDownBarsEl(chart),
            new XElement(C + "axId", new XAttribute("val", catAxId)),
            new XElement(C + "axId", new XAttribute("val", valAxId)));

    private static XElement? BuildStockUpDownBarsEl(ChartShape chart) =>
        chart.ShowUpDownBars
            ? new XElement(C + "upDownBars",
                chart.UpDownBarGapWidthPercent is { } gapWidth
                    ? new XElement(C + "gapWidth", new XAttribute("val", Math.Clamp(gapWidth, 0, 500)))
                    : null,
                new XElement(C + "upBars",
                    chart.UpBarFill is null ? null : new XElement(C + "spPr", BuildChartFillEl(chart.UpBarFill, null))),
                new XElement(C + "downBars",
                    chart.DownBarFill is null ? null : new XElement(C + "spPr", BuildChartFillEl(chart.DownBarFill, null))))
            : null;

    private static XElement BuildSurfaceChartEl(ChartShape chart, List<XElement> seriesEls,
        int catAxId = PrimaryCatAxId, int valAxId = PrimaryValAxId, int serAxId = PrimarySerAxId) =>
        new XElement(C + (chart.ChartType == ChartType.Surface3D ? "surface3DChart" : "surfaceChart"),
            BuildVaryColorsEl(chart),
            chart.ChartType == ChartType.Surface3D ? BuildWireframeEl(chart) : null,
            seriesEls,
            new XElement(C + "axId", new XAttribute("val", catAxId)),
            new XElement(C + "axId", new XAttribute("val", valAxId)),
            new XElement(C + "axId", new XAttribute("val", serAxId)));

    private static XElement? BuildWireframeEl(ChartShape chart) =>
        chart.Wireframe || chart.WireframeSpecified
            ? new XElement(C + "wireframe", new XAttribute("val", chart.Wireframe ? "1" : "0"))
            : null;

    private static XElement BuildSerAxEl(int axId, int crossAxId) =>
        new XElement(C + "serAx",
            new XElement(C + "axId", new XAttribute("val", axId)),
            new XElement(C + "scaling",
                new XElement(C + "orientation", new XAttribute("val", "minMax"))),
            new XElement(C + "delete", new XAttribute("val", "0")),
            new XElement(C + "axPos", new XAttribute("val", "r")),
            new XElement(C + "majorTickMark", new XAttribute("val", "none")),
            new XElement(C + "minorTickMark", new XAttribute("val", "none")),
            new XElement(C + "tickLblPos", new XAttribute("val", "nextTo")),
            new XElement(C + "crossAx", new XAttribute("val", crossAxId)),
            new XElement(C + "crosses", new XAttribute("val", "autoZero")));

    private static XElement? BuildVaryColorsEl(ChartShape chart) =>
        chart.VaryColors
            ? new XElement(C + "varyColors", new XAttribute("val", "1"))
            : null;

    private static XElement? BuildDisplayBlanksAsEl(ChartShape chart) =>
        chart.DisplayBlanksAs is { } value
            ? new XElement(C + "dispBlanksAs", new XAttribute("val", value switch
            {
                ChartDisplayBlanksAs.Span => "span",
                ChartDisplayBlanksAs.Gap => "gap",
                ChartDisplayBlanksAs.Zero => "zero",
                _ => "gap"
            }))
            : null;

    private static XElement? BuildShowDataLabelsOverMaximumEl(ChartShape chart) =>
        chart.ShowDataLabelsOverMaximum is { } value
            ? new XElement(C + "showDLblsOverMax", new XAttribute("val", BoolValue(value)))
            : null;

    // CA2+CA3: Build dLbls in CT_DLbls schema order and gate dLblPos by chart type.
    // CT_DLbls order: numFmt, spPr, txPr, dLblPos, showLegendKey, showVal,
    //                 showCatName, showSerName, showPercent, showBubbleSize, separator.
    private static XElement? BuildDataLabelsEl(
        ChartDataLabels? labels,
        ChartType chartType = ChartType.ColumnClustered,
        IReadOnlyDictionary<int, ChartDataLabels>? pointLabels = null)
    {
        if ((labels is null || !labels.HasAny) && (pointLabels is null || pointLabels.Count == 0))
            return null;

        var el = new XElement(C + "dLbls");

        if (labels is null || !labels.HasAny)
        {
            foreach (var pair in pointLabels!.OrderBy(pair => pair.Key))
            {
                var pointPayload = BuildDataLabelsEl(pair.Value, chartType);
                if (pointPayload is null)
                    continue;

                el.Add(new XElement(C + "dLbl",
                    new XElement(C + "idx", new XAttribute("val", pair.Key)),
                    pointPayload.Elements().Select(element => new XElement(element))));
            }
            return el;
        }

        // CA2: numFmt FIRST (before dLblPos and show* flags).
        if (labels.Delete is { } deleted)
            el.Add(new XElement(C + "delete", new XAttribute("val", BoolValue(deleted))));

        if (!string.IsNullOrEmpty(labels.NumberFormat))
            el.Add(new XElement(C + "numFmt",
                new XAttribute("formatCode", labels.NumberFormat),
                new XAttribute("sourceLinked", "0")));

        var textProperties = BuildChartTextPropertiesEl(labels.TextStyle);
        if (textProperties is not null)
            el.Add(textProperties);

        // CA2: dLblPos SECOND (before show* flags).
        // CA3: Gate by chart type / grouping — only emit positions valid for the target type.
        if (labels.Position.HasValue)
        {
            string? posVal = labels.Position.Value switch
            {
                DataLabelPosition.Center     => "ctr",
                DataLabelPosition.InsideEnd  => "inEnd",
                DataLabelPosition.OutsideEnd => "outEnd",
                DataLabelPosition.InsideBase => "inBase",
                DataLabelPosition.BestFit    => "bestFit",
                DataLabelPosition.Above      => "t",
                DataLabelPosition.Below      => "b",
                DataLabelPosition.Left       => "l",
                DataLabelPosition.Right      => "r",
                _                            => null
            };

            // CA3: restrict dLblPos per chart-type validity rules.
            posVal = GateDLblPos(posVal, chartType);
            if (posVal is not null)
                el.Add(new XElement(C + "dLblPos", new XAttribute("val", posVal)));
        }

        // CA2: show* flags LAST (in schema order).
        if (labels.ShowLegendKey)
            el.Add(new XElement(C + "showLegendKey", new XAttribute("val", "1")));
        if (labels.ShowValue)
            el.Add(new XElement(C + "showVal", new XAttribute("val", "1")));
        if (labels.ShowCategoryName)
            el.Add(new XElement(C + "showCatName", new XAttribute("val", "1")));
        if (labels.ShowSeriesName)
            el.Add(new XElement(C + "showSerName", new XAttribute("val", "1")));
        if (labels.ShowPercent)
            el.Add(new XElement(C + "showPercent", new XAttribute("val", "1")));
        if (labels.ShowBubbleSize)
            el.Add(new XElement(C + "showBubbleSize", new XAttribute("val", "1")));
        if (labels.ShowLeaderLines is { } showLeaderLines)
            el.Add(new XElement(C + "showLeaderLines", new XAttribute("val", BoolValue(showLeaderLines))));
        if (labels.Separator is not null)
            el.Add(new XElement(C + "separator", labels.Separator));

        if (pointLabels is not null)
        {
            foreach (var pair in pointLabels.OrderBy(pair => pair.Key))
            {
                var pointPayload = BuildDataLabelsEl(pair.Value, chartType);
                if (pointPayload is null)
                    continue;

                el.Add(new XElement(C + "dLbl",
                    new XElement(C + "idx", new XAttribute("val", pair.Key)),
                    pointPayload.Elements().Select(element => new XElement(element))));
            }
        }

        return el;
    }

    private static void AddDataLabelsInSchemaOrder(XElement chartTypeEl, XElement dataLabelsEl)
    {
        var boundary = chartTypeEl.Elements()
            .FirstOrDefault(element =>
                element.Name == C + "firstSliceAng" ||
                element.Name == C + "holeSize" ||
                element.Name == C + "axId" ||
                element.Name == C + "extLst");

        if (boundary is not null)
            boundary.AddBeforeSelf(dataLabelsEl);
        else
            chartTypeEl.Add(dataLabelsEl);
    }

    private static XElement? BuildDataTableEl(ChartDataTableSettings? dataTable)
    {
        if (dataTable is null) return null;

        return new XElement(C + "dTable",
            new XElement(C + "showHorzBorder", new XAttribute("val", BoolValue(dataTable.ShowHorizontalBorder))),
            new XElement(C + "showVertBorder", new XAttribute("val", BoolValue(dataTable.ShowVerticalBorder))),
            new XElement(C + "showOutline",    new XAttribute("val", BoolValue(dataTable.ShowOutlineBorder))),
            new XElement(C + "showKeys",       new XAttribute("val", BoolValue(dataTable.ShowLegendKeys))),
            BuildDataTableShapePropertiesEl(dataTable.BackgroundFill, dataTable.BorderOutline),
            BuildChartTextPropertiesEl(dataTable.TextStyle));
    }

    private static XElement? BuildDataTableShapePropertiesEl(ShapeFill? fill, ShapeOutline? outline)
    {
        var fillEl = BuildDataTableFillEl(fill);
        var line = BuildDataTableOutlineEl(outline);
        return fillEl is null && line is null ? null : new XElement(C + "spPr", fillEl, line);
    }

    private static XElement? BuildChartAreaFillEl(ShapeFill? fill) =>
        fill switch
        {
            null => null,
            ShapeFill.None => new XElement(A + "noFill"),
            ShapeFill.Solid s => new XElement(A + "solidFill", BuildColorEl(s.Color)),
            ShapeFill.Gradient g => BuildGradFillEl(g),
            ShapeFill.Pattern p => new XElement(A + "pattFill",
                new XAttribute("prst", p.Preset),
                new XElement(A + "fgClr", BuildColorEl(p.ForegroundColor)),
                new XElement(A + "bgClr", BuildColorEl(p.BackgroundColor))),
            _ => null
        };

    private static XElement? BuildChartAreaOutlineEl(ShapeOutline? outline) =>
        outline switch
        {
            null => null,
            ShapeOutline.None => new XElement(A + "ln", new XElement(A + "noFill")),
            ShapeOutline.Visible v => new XElement(A + "ln",
                new XAttribute("w", DrawingMlCoordinateUnits.PointsToEmu(v.WidthPt)),
                new XElement(A + "solidFill", BuildColorEl(v.Color)),
                v.Dash != OutlineDash.Solid
                    ? new XElement(A + "prstDash", new XAttribute("val", ToDashStr(v.Dash)))
                    : null),
            ShapeOutline.GradientVisible gv => new XElement(A + "ln",
                new XAttribute("w", DrawingMlCoordinateUnits.PointsToEmu(gv.WidthPt)),
                BuildGradFillEl(gv.Gradient),
                gv.Dash != OutlineDash.Solid
                    ? new XElement(A + "prstDash", new XAttribute("val", ToDashStr(gv.Dash)))
                    : null),
            _ => null
        };

    private static XElement? BuildDataTableFillEl(ShapeFill? fill) =>
        fill switch
        {
            null => null,
            ShapeFill.None => new XElement(A + "noFill"),
            ShapeFill.Solid s => new XElement(A + "solidFill", BuildColorEl(s.Color)),
            ShapeFill.Gradient g => BuildGradFillEl(g),
            ShapeFill.Pattern p => new XElement(A + "pattFill",
                new XAttribute("prst", p.Preset),
                new XElement(A + "fgClr", BuildColorEl(p.ForegroundColor)),
                new XElement(A + "bgClr", BuildColorEl(p.BackgroundColor))),
            _ => null
        };

    private static XElement? BuildDataTableOutlineEl(ShapeOutline? outline) =>
        outline switch
        {
            null => null,
            ShapeOutline.None => new XElement(A + "ln", new XElement(A + "noFill")),
            ShapeOutline.Visible v => new XElement(A + "ln",
                new XAttribute("w", DrawingMlCoordinateUnits.PointsToEmu(v.WidthPt)),
                new XElement(A + "solidFill", BuildColorEl(v.Color)),
                v.Dash != OutlineDash.Solid
                    ? new XElement(A + "prstDash", new XAttribute("val", ToDashStr(v.Dash)))
                    : null),
            // Wave: gradient outline — mirrors PptxPackageWriter.BuildOutlineEl so a gradient
            // data-table border round-trips instead of being dropped in favor of default gray.
            ShapeOutline.GradientVisible gv => new XElement(A + "ln",
                new XAttribute("w", DrawingMlCoordinateUnits.PointsToEmu(gv.WidthPt)),
                BuildGradFillEl(gv.Gradient),
                gv.Dash != OutlineDash.Solid
                    ? new XElement(A + "prstDash", new XAttribute("val", ToDashStr(gv.Dash)))
                    : null),
            _ => null
        };

    /// <summary>
    /// Builds an <c>a:gradFill</c> element from a <see cref="ShapeFill.Gradient"/>.
    /// Mirrors <c>PptxPackageWriter.BuildGradFillEl</c> (kept as a local copy here, matching
    /// this file's existing convention of duplicating small color/fill helpers rather than
    /// exposing them across writer classes — see <see cref="BuildColorEl"/> above).
    /// </summary>
    private static XElement BuildGradFillEl(ShapeFill.Gradient g)
    {
        // Stops MUST be in ascending position order per OOXML CT_GradientStopList.
        // a:gsLst requires at least 2 stops; synthesise when the model has fewer.
        var stops = g.Stops.OrderBy(s => s.Position).ToList();
        if (stops.Count == 0)
        {
            stops = new List<GradientStop>
            {
                new GradientStop(0.0, ThemeAwareColor.White),
                new GradientStop(1.0, ThemeAwareColor.Black),
            };
        }
        else if (stops.Count == 1)
        {
            var singleColor = stops[0].Color;
            stops = new List<GradientStop>
            {
                new GradientStop(0.0, singleColor),
                new GradientStop(1.0, singleColor),
            };
        }

        var gsLst = new XElement(A + "gsLst");
        foreach (var stop in stops)
        {
            int pos = (int)Math.Round(stop.Position * 100000);
            // CT_GradientStop: a:gs must contain a color element directly (srgbClr/schemeClr/…),
            // NOT wrapped in a:solidFill — that wrapper is invalid per ECMA-376 schema.
            gsLst.Add(new XElement(A + "gs",
                new XAttribute("pos", pos),
                BuildColorEl(stop.Color)));
        }

        XElement kindEl;
        if (g.Kind == GradientKind.Radial)
        {
            kindEl = new XElement(A + "path",
                new XAttribute("path", "circle"),
                new XElement(A + "fillToRect",
                    new XAttribute("l", "50000"),
                    new XAttribute("t", "50000"),
                    new XAttribute("r", "50000"),
                    new XAttribute("b", "50000")));
        }
        else
        {
            kindEl = new XElement(A + "lin",
                new XAttribute("ang", (long)Math.Round(g.AngleDegrees * 60000)),
                new XAttribute("scaled", "0"));
        }

        return new XElement(A + "gradFill", gsLst, kindEl);
    }

    private static XElement? BuildChartTextPropertiesEl(
        ChartTextStyle? style,
        XNamespace? chartNamespace = null)
    {
        if (style?.IsImplicitDefault == true)
            return null;

        var defRPr = BuildChartDefaultRunPropertiesEl(style);
        if (defRPr is null)
            return null;

        var chartNs = chartNamespace ?? C;
        return new XElement(chartNs + "txPr",
            new XElement(A + "bodyPr"),
            new XElement(A + "lstStyle"),
            new XElement(A + "p",
                new XElement(A + "pPr", defRPr),
                new XElement(A + "endParaRPr")));
    }

    private static XElement? BuildChartDefaultRunPropertiesEl(ChartTextStyle? style)
    {
        if (style is null)
            return null;

        var attrs = new List<XAttribute>();
        if (style.FontSizePt.HasValue)
            attrs.Add(new XAttribute("sz", (int)Math.Round(style.FontSizePt.Value * 100)));
        if (style.Bold.HasValue)
            attrs.Add(new XAttribute("b", style.Bold.Value ? "1" : "0"));
        if (style.Italic.HasValue)
            attrs.Add(new XAttribute("i", style.Italic.Value ? "1" : "0"));

        var fill = style.Color is not null
            ? new XElement(A + "solidFill", BuildColorEl(style.Color))
            : null;

        // CT_TextCharacterProperties child order (ECMA-376): ln → fill group →
        // effectLst → a:latin/ea/cs → hlinkClick. a:latin goes AFTER the fill group.
        var latin = style.FontFamily is not null
            ? new XElement(A + "latin", new XAttribute("typeface", style.FontFamily))
            : null;

        return attrs.Count == 0 && fill is null && latin is null
            ? null
            : new XElement(A + "defRPr", attrs, fill, latin);
    }

    private static string ToDashStr(OutlineDash dash) => dash switch
    {
        OutlineDash.Dash => "dash",
        OutlineDash.Dot => "dot",
        OutlineDash.DashDot => "dashDot",
        OutlineDash.LongDash => "lgDash",
        OutlineDash.LongDashDot => "lgDashDot",
        OutlineDash.LongDashDotDot => "lgDashDotDot",
        OutlineDash.SystemDash => "sysDash",
        OutlineDash.SystemDot => "sysDot",
        OutlineDash.SystemDashDot => "sysDashDot",
        _ => "solid"
    };

    /// <summary>
    /// CA3: Returns a valid dLblPos value for the given chart type, or null to suppress the element.
    /// Rules:
    ///   Stacked bar/column  → only "ctr" is valid (OOXML §21.2.2.44); coerce any other to null (suppress).
    ///   Pie / Doughnut      → only ctr / inEnd / outEnd / bestFit; suppress inBase/directional.
    ///   All other types     → pass through as-is.
    /// </summary>
    private static string? GateDLblPos(string? posVal, ChartType chartType)
    {
        if (posVal is null) return null;

        bool isStackedBar = chartType is ChartType.BarStacked or ChartType.BarStacked100
                                      or ChartType.ColumnStacked or ChartType.ColumnStacked100;
        if (isStackedBar)
        {
            // Only "ctr" is valid for stacked; outEnd/inEnd/inBase → suppress.
            return posVal == "ctr" ? "ctr" : null;
        }

        bool isPieLike = chartType is ChartType.Pie or ChartType.Doughnut or ChartType.OfPie;
        if (isPieLike)
        {
            // Pie allows: ctr, inEnd, outEnd, bestFit.  Suppress directional (t/b/l/r) and inBase.
            return posVal is "ctr" or "inEnd" or "outEnd" or "bestFit" ? posVal : null;
        }

        return posVal;
    }

    // ── Series schema gating (ECMA-376 CT_*Ser content models) ───────────────
    //
    // Every plot-type element accepts its own CT_*Ser type, and the optional series
    // children below are only declared on *some* of them. A ChartSeries can easily carry
    // a value that its current chart type cannot express — e.g. SmoothLine survives a
    // chart-type change from Line to Radar, or arrives from a foreign .pptx — so the
    // writer must gate on the schema the c:ser is actually going to live in. Emitting
    // c:smooth inside a CT_RadarSer (or c:marker inside a CT_BarSer) makes PowerPoint
    // report the deck as corrupt and repair it on open.

    /// <summary>The ECMA-376 series content model the emitted <c>c:ser</c> will live in.</summary>
    private enum SeriesSchema
    {
        /// <summary>CT_LineSer — c:lineChart/c:line3DChart/c:stockChart.</summary>
        Line,
        /// <summary>CT_ScatterSer — c:scatterChart.</summary>
        Scatter,
        /// <summary>CT_BubbleSer — c:bubbleChart.</summary>
        Bubble,
        /// <summary>CT_BarSer — c:barChart/c:bar3DChart (and the funnel/waterfall bar-likes).</summary>
        Bar,
        /// <summary>CT_PieSer — c:pieChart/c:doughnutChart/c:ofPieChart.</summary>
        Pie,
        /// <summary>CT_AreaSer — c:areaChart/c:area3DChart.</summary>
        Area,
        /// <summary>CT_RadarSer — c:radarChart.</summary>
        Radar,
        /// <summary>CT_SurfaceSer — c:surfaceChart/c:surface3DChart.</summary>
        Surface,
    }

    private static class SeriesSchemaSupport
    {
        /// <summary>c:smooth is declared on CT_LineSer and CT_ScatterSer only.</summary>
        internal static bool SupportsSmooth(SeriesSchema schema) =>
            schema is SeriesSchema.Line or SeriesSchema.Scatter;

        /// <summary>c:marker is declared on CT_LineSer, CT_ScatterSer and CT_RadarSer only.</summary>
        internal static bool SupportsMarker(SeriesSchema schema) =>
            schema is SeriesSchema.Line or SeriesSchema.Scatter or SeriesSchema.Radar;

        /// <summary>c:invertIfNegative is declared on CT_BarSer and CT_BubbleSer only.</summary>
        internal static bool SupportsInvertIfNegative(SeriesSchema schema) =>
            schema is SeriesSchema.Bar or SeriesSchema.Bubble;

        /// <summary>
        /// c:trendline and c:errBars are absent from CT_PieSer, CT_RadarSer and CT_SurfaceSer
        /// (PowerPoint offers neither on those chart types either).
        /// </summary>
        internal static bool SupportsTrendlineAndErrorBars(SeriesSchema schema) =>
            schema is not (SeriesSchema.Pie or SeriesSchema.Radar or SeriesSchema.Surface);

        /// <summary>
        /// CT_SurfaceSer is idx, order, tx, spPr, cat, val only — it has no c:dPt or c:dLbls,
        /// because a surface mesh has no per-point formatting.
        /// </summary>
        internal static bool SupportsDataPointsAndLabels(SeriesSchema schema) =>
            schema is not SeriesSchema.Surface;
    }

    /// <summary>Maps a chart type to the CT_*Ser content model its primary plot group emits.</summary>
    private static SeriesSchema SeriesSchemaFor(ChartType chartType) => chartType switch
    {
        ChartType.Line or ChartType.LineMarkers or ChartType.Stock => SeriesSchema.Line,
        ChartType.Scatter                                          => SeriesSchema.Scatter,
        ChartType.Bubble                                           => SeriesSchema.Bubble,
        ChartType.Pie or ChartType.OfPie or ChartType.Doughnut     => SeriesSchema.Pie,
        ChartType.Area or ChartType.AreaStacked                    => SeriesSchema.Area,
        ChartType.Radar                                            => SeriesSchema.Radar,
        ChartType.Surface or ChartType.Surface3D                   => SeriesSchema.Surface,
        // Bar/column, funnel, waterfall and the Unknown fallback all render as bar-likes.
        _                                                          => SeriesSchema.Bar,
    };

    // ── Scatter/bubble series element (uses xVal/yVal/bubbleSize instead of cat/val) ────

    private static XElement BuildScatterSeriesEl(ChartShape chart, ChartSeries series, int index)
    {
        var schema = SeriesSchemaFor(chart.ChartType);
        var el = new XElement(C + "ser",
            new XElement(C + "idx",   new XAttribute("val", index)),
            new XElement(C + "order", new XAttribute("val", index)));

        // ID2: only address the regenerated workbook's cells when this chart will actually get
        // one written (RegenerateWorkbookOnSave). A preserved chart keeps whatever c:f its
        // original XML had (MergePreservedExternalData/roundtrip already carries that through
        // the cache-only path here) — we must not fabricate a range that points nowhere.
        var layout = chart.RegenerateWorkbookOnSave ? BuildWorksheetLayout(chart) : (WorksheetLayout?)null;
        var pointCount = Math.Max(series.Values.Count, Math.Max(series.XValues.Count, series.BubbleSizes.Count));
        var lastRow = pointCount + 1; // header is row 1, data starts row 2

        // Series name
        el.Add(BuildSeriesNameEl(
            layout is { } nameLayout && index < nameLayout.ValueColumns.Count
                ? CellRef(nameLayout.ValueColumns[index], 1)
                : series.FormulaReferences.SeriesName,
            series.Name));

        var spPr = BuildSeriesShapePropertiesEl(series);
        if (spPr is not null)
            el.Add(spPr);

        // CT_BubbleSer declares invertIfNegative *before* dPt and has no c:marker at all;
        // CT_ScatterSer is the mirror image (marker, no invertIfNegative).
        if (SeriesSchemaSupport.SupportsInvertIfNegative(schema) &&
            series.InvertIfNegative is { } invertIfNegative)
        {
            el.Add(new XElement(C + "invertIfNegative", new XAttribute("val", BoolValue(invertIfNegative))));
        }

        if (SeriesSchemaSupport.SupportsMarker(schema))
        {
            var marker = BuildMarkerStyleEl(series.MarkerStyle);
            if (marker is not null)
                el.Add(marker);
        }

        // CT_ScatterSer and CT_BubbleSer both declare dPt/dLbls/trendline/errBars, so nothing
        // below needs a schema gate — unlike the cat/val builder, which also serves pie,
        // radar and surface.
        AddPointStyleElements(el, series);

        // Per-series data labels
        var serDlblsEl2 = BuildDataLabelsEl(series.DataLabels, chart.ChartType, PointDataLabels(series));
        if (serDlblsEl2 is not null) el.Add(serDlblsEl2);

        var trendline = BuildTrendlineEl(series.Trendline);
        if (trendline is not null) el.Add(trendline);

        var errBars = BuildErrorBarsEl(series.ErrorBars);
        if (errBars is not null) el.Add(errBars);

        // X values (c:xVal)
        if (series.XValues.Count > 0)
        {
            el.Add(BuildNumericDataSourceEl("xVal",
                layout is { } xLayout && index < xLayout.XColumns.Count
                    ? ColumnRangeRef(xLayout.XColumns[index], lastRow)
                    : series.FormulaReferences.XValues,
                series.XValues));
        }

        // Y values (c:yVal)
        if (series.Values.Count > 0)
        {
            el.Add(BuildNumericDataSourceEl("yVal",
                layout is { } yLayout && index < yLayout.ValueColumns.Count
                    ? ColumnRangeRef(yLayout.ValueColumns[index], lastRow)
                    : series.FormulaReferences.YValues,
                series.Values));
        }

        // Bubble sizes (c:bubbleSize) — only for bubble charts
        if (series.BubbleSizes.Count > 0)
        {
            el.Add(BuildNumericDataSourceEl("bubbleSize",
                layout is { } sizeLayout && sizeLayout.BubbleSizeColumns.Count > index
                    ? ColumnRangeRef(sizeLayout.BubbleSizeColumns[index], lastRow)
                    : series.FormulaReferences.BubbleSizes,
                series.BubbleSizes));
        }

        // c:smooth follows yVal in CT_ScatterSer; CT_BubbleSer has bubbleSize/bubble3D there
        // instead and forbids c:smooth outright.
        var smooth = BuildSmoothLineEl(series, schema);
        if (smooth is not null)
            el.Add(smooth);

        return el;
    }

    // ── Series element ────────────────────────────────────────────────────────

    private static XElement BuildSeriesEl(ChartShape chart, ChartSeries series, int index,
        SeriesSchema schema)
    {
        var el = new XElement(C + "ser",
            new XElement(C + "idx", new XAttribute("val", index)),
            new XElement(C + "order", new XAttribute("val", index)));

        // ID2: only address the regenerated workbook's cells when one will actually be written
        // (RegenerateWorkbookOnSave). Preserved charts keep whatever c:f their source XML had.
        var layout = chart.RegenerateWorkbookOnSave ? BuildWorksheetLayout(chart) : (WorksheetLayout?)null;
        var lastRow = Math.Max(chart.Categories.Count, series.Values.Count) + 1; // header is row 1

        // Series name
        el.Add(BuildSeriesNameEl(
            layout is { } nameLayout && index < nameLayout.ValueColumns.Count
                ? CellRef(nameLayout.ValueColumns[index], 1)
                : series.FormulaReferences.SeriesName,
            series.Name));

        var spPr = BuildSeriesShapePropertiesEl(series);
        if (spPr is not null)
            el.Add(spPr);

        // CT_BarSer declares invertIfNegative and no marker; CT_LineSer/CT_RadarSer are the
        // mirror image. Only one of the two can ever be emitted for a given schema.
        if (SeriesSchemaSupport.SupportsInvertIfNegative(schema) &&
            series.InvertIfNegative is { } invertIfNegative)
        {
            el.Add(new XElement(C + "invertIfNegative", new XAttribute("val", BoolValue(invertIfNegative))));
        }

        if (SeriesSchemaSupport.SupportsMarker(schema))
        {
            var marker = BuildMarkerStyleEl(series.MarkerStyle);
            if (marker is not null)
                el.Add(marker);
        }

        if (SeriesSchemaSupport.SupportsDataPointsAndLabels(schema))
        {
            AddPointStyleElements(el, series);

            // Per-series data labels
            var serDlblsEl = BuildDataLabelsEl(series.DataLabels, chart.ChartType, PointDataLabels(series));
            if (serDlblsEl is not null) el.Add(serDlblsEl);
        }

        if (SeriesSchemaSupport.SupportsTrendlineAndErrorBars(schema))
        {
            var trendline = BuildTrendlineEl(series.Trendline);
            if (trendline is not null) el.Add(trendline);

            var errBars = BuildErrorBarsEl(series.ErrorBars);
            if (errBars is not null) el.Add(errBars);
        }

        // Categories
        if (chart.Categories.Count > 0)
        {
            el.Add(BuildCategoryDataSourceEl(
                layout is { } catLayout
                    ? ColumnRangeRef(catLayout.CategoryColumn, lastRow)
                    : series.FormulaReferences.Category,
                chart.Categories));
        }

        // Values
        if (series.Values.Count > 0)
        {
            el.Add(BuildNumericDataSourceEl("val",
                layout is { } valLayout && index < valLayout.ValueColumns.Count
                    ? ColumnRangeRef(valLayout.ValueColumns[index], lastRow)
                    : series.FormulaReferences.Values,
                series.Values));
        }

        // c:smooth closes CT_LineSer; CT_BarSer/CT_PieSer/CT_AreaSer/CT_RadarSer/CT_SurfaceSer
        // do not declare it, so a stale SmoothLine on those types must be dropped.
        var smooth = BuildSmoothLineEl(series, schema);
        if (smooth is not null)
            el.Add(smooth);

        return el;
    }

    private static XElement? BuildSmoothLineEl(ChartSeries series, SeriesSchema schema) =>
        SeriesSchemaSupport.SupportsSmooth(schema) && series.SmoothLine.HasValue
            ? new XElement(C + "smooth", new XAttribute("val", BoolValue(series.SmoothLine.Value)))
            : null;

    private static XElement? BuildErrorBarsEl(ChartErrorBars? bars)
    {
        if (bars is null)
            return null;

        return new XElement(C + "errBars",
            new XElement(C + "errDir", new XAttribute("val", bars.Direction == ChartErrorDirection.X ? "x" : "y")),
            new XElement(C + "errBarType", new XAttribute("val", bars.BarType switch
            {
                ChartErrorBarType.Minus => "minus",
                ChartErrorBarType.Plus => "plus",
                _ => "both",
            })),
            new XElement(C + "errValType", new XAttribute("val", bars.ValueType == ChartErrorValueType.Percentage ? "percentage" : "fixedVal")),
            new XElement(C + "noEndCap", new XAttribute("val", BoolValue(bars.NoEndCap))),
            new XElement(C + "val", new XAttribute("val", bars.Value.ToString("G", CultureInfo.InvariantCulture))));
    }

    private static XElement? BuildTrendlineEl(ChartTrendline? trendline)
    {
        if (trendline is null)
            return null;

        var type = trendline.Type switch
        {
            ChartTrendlineType.Exponential => "exp",
            ChartTrendlineType.Logarithmic => "log",
            ChartTrendlineType.Polynomial => "poly",
            ChartTrendlineType.Power => "power",
            ChartTrendlineType.MovingAverage => "movingAvg",
            _ => "linear",
        };
        var children = new List<object>
        {
            new XElement(C + "trendlineType", new XAttribute("val", type)),
        };
        if (trendline.PolynomialOrder is { } order)
            children.Add(new XElement(C + "order", new XAttribute("val", order)));
        if (trendline.MovingAveragePeriod is { } period)
            children.Add(new XElement(C + "period", new XAttribute("val", period)));
        if (trendline.Forward is { } forward)
            children.Add(new XElement(C + "forward", new XAttribute("val", forward.ToString("G", CultureInfo.InvariantCulture))));
        if (trendline.Backward is { } backward)
            children.Add(new XElement(C + "backward", new XAttribute("val", backward.ToString("G", CultureInfo.InvariantCulture))));
        if (trendline.DisplayEquation)
            children.Add(new XElement(C + "dispEq", new XAttribute("val", "1")));
        if (trendline.DisplayRSquared)
            children.Add(new XElement(C + "dispRSqr", new XAttribute("val", "1")));
        return new XElement(C + "trendline", children);
    }

    private static XElement? BuildSeriesShapePropertiesEl(ChartSeries series)
    {
        var children = new List<object>();
        var fill = BuildChartFillEl(series.Fill, series.FillColor);
        if (fill is not null)
            children.Add(fill);

        var line = BuildLineStyleEl(series.LineStyle);
        if (line is not null)
            children.Add(line);

        return children.Count == 0 ? null : new XElement(C + "spPr", children);
    }

    private static XElement? BuildLineStyleEl(ChartLineStyle? style)
    {
        if (style is null)
            return null;

        var line = new XElement(A + "ln");
        if (style.WidthPt.HasValue)
            line.Add(new XAttribute("w", DrawingMlCoordinateUnits.PointsToEmu(style.WidthPt.Value)));

        if (style.NoFill)
            line.Add(new XElement(A + "noFill"));
        else if (style.Color is not null)
            line.Add(new XElement(A + "solidFill", BuildColorEl(style.Color)));

        if (style.Dash != OutlineDash.Solid)
            line.Add(new XElement(A + "prstDash", new XAttribute("val", ToDashStr(style.Dash))));

        return line;
    }

    private static XElement? BuildMarkerStyleEl(ChartMarkerStyle? style)
    {
        if (style is null)
            return null;

        var marker = new XElement(C + "marker");
        if (style.Symbol.HasValue)
            marker.Add(new XElement(C + "symbol", new XAttribute("val", ToMarkerSymbolValue(style.Symbol.Value))));

        if (style.SizePt.HasValue)
            marker.Add(new XElement(C + "size", new XAttribute("val", Math.Clamp((int)Math.Round(style.SizePt.Value), 2, 72))));

        var spPr = BuildMarkerShapePropertiesEl(style);
        if (spPr is not null)
            marker.Add(spPr);

        return marker.HasElements ? marker : null;
    }

    private static XElement? BuildMarkerShapePropertiesEl(ChartMarkerStyle style)
    {
        var children = new List<object>();
        if (style.NoFill)
            children.Add(new XElement(A + "noFill"));
        else
        {
            var fill = BuildChartFillEl(style.Fill, style.FillColor);
            if (fill is not null)
                children.Add(fill);
        }

        ChartLineStyle? lineStyle = null;
        if (style.NoStroke || style.StrokeColor is not null || style.StrokeWidthPt.HasValue)
        {
            lineStyle = new ChartLineStyle
            {
                Color = style.StrokeColor,
                WidthPt = style.StrokeWidthPt,
                NoFill = style.NoStroke
            };
        }

        var line = BuildLineStyleEl(lineStyle);
        if (line is not null)
            children.Add(line);

        return children.Count == 0 ? null : new XElement(C + "spPr", children);
    }

    private static void AddPointStyleElements(XElement seriesEl, ChartSeries series)
    {
        foreach (var pointIndex in series.PointColors.Keys.Concat(series.PointStyles.Keys).Distinct().OrderBy(static index => index))
        {
            series.PointStyles.TryGetValue(pointIndex, out var style);
            series.PointColors.TryGetValue(pointIndex, out var pointColor);
            var dPt = new XElement(C + "dPt",
                new XElement(C + "idx", new XAttribute("val", pointIndex)));

            // CT_DPt sequence is idx, invertIfNegative, marker, bubble3D, explosion, spPr —
            // c:marker must precede c:explosion/c:spPr or PowerPoint repairs the deck.
            // (Unlike c:ser, CT_DPt declares c:marker for every chart type.)
            var marker = BuildMarkerStyleEl(style?.Marker);
            if (marker is not null)
                dPt.Add(marker);

            if (style?.ExplosionPercent is { } explosion)
            {
                dPt.Add(new XElement(C + "explosion",
                    new XAttribute("val", Math.Clamp(explosion, 0, 100))));
            }

            var spPr = BuildPointShapePropertiesEl(pointColor, style);
            if (spPr is not null)
                dPt.Add(spPr);

            if (dPt.Elements().Skip(1).Any())
                seriesEl.Add(dPt);
        }
    }

    private static IReadOnlyDictionary<int, ChartDataLabels>? PointDataLabels(ChartSeries series)
    {
        var labels = series.PointStyles
            .Where(pair => pair.Value.DataLabels is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.DataLabels!);
        return labels.Count == 0 ? null : labels;
    }

    private static XElement? BuildPointShapePropertiesEl(ThemeAwareColor? pointColor, ChartPointStyle? style)
    {
        var children = new List<object>();
        var fill = BuildChartFillEl(style?.Fill, style?.FillColor ?? pointColor);
        if (fill is not null)
            children.Add(fill);

        ChartLineStyle? lineStyle = null;
        if (style?.StrokeColor is not null || style?.StrokeWidthPt is not null)
        {
            lineStyle = new ChartLineStyle
            {
                Color = style.StrokeColor,
                WidthPt = style.StrokeWidthPt
            };
        }

        var line = BuildLineStyleEl(lineStyle);
        if (line is not null)
            children.Add(line);

        return children.Count == 0 ? null : new XElement(C + "spPr", children);
    }

    private static XElement? BuildChartFillEl(ShapeFill? fill, ThemeAwareColor? solidFallback) =>
        fill switch
        {
            ShapeFill.None => new XElement(A + "noFill"),
            ShapeFill.Gradient gradient => BuildGradFillEl(gradient),
            ShapeFill.Pattern pattern => BuildPattFillEl(pattern),
            ShapeFill.Solid solid => new XElement(A + "solidFill", BuildColorEl(solid.Color)),
            _ when solidFallback is not null => new XElement(A + "solidFill", BuildColorEl(solidFallback)),
            _ => null
        };

    private static XElement BuildPattFillEl(ShapeFill.Pattern pattern) =>
        new XElement(A + "pattFill",
            new XAttribute("prst", pattern.Preset),
            new XElement(A + "fgClr", BuildColorEl(pattern.ForegroundColor)),
            new XElement(A + "bgClr", BuildColorEl(pattern.BackgroundColor)));

    private static string ToMarkerSymbolValue(ChartMarkerSymbol symbol) =>
        symbol switch
        {
            ChartMarkerSymbol.Auto => "auto",
            ChartMarkerSymbol.Circle => "circle",
            ChartMarkerSymbol.Dash => "dash",
            ChartMarkerSymbol.Diamond => "diamond",
            ChartMarkerSymbol.Dot => "dot",
            ChartMarkerSymbol.None => "none",
            ChartMarkerSymbol.Picture => "picture",
            ChartMarkerSymbol.Plus => "plus",
            ChartMarkerSymbol.Square => "square",
            ChartMarkerSymbol.Star => "star",
            ChartMarkerSymbol.Triangle => "triangle",
            ChartMarkerSymbol.X => "x",
            _ => "auto"
        };

    // ── c:f formula-range helpers (ID2) ──────────────────────────────────────────

    /// <summary>Builds a single-cell c:f range, e.g. "ChartData!$B$1" (series name header).</summary>
    private static string CellRef(int oneBasedColumn, int row) =>
        $"{RegeneratedSheetName}!${ColumnName(oneBasedColumn)}${row.ToString(CultureInfo.InvariantCulture)}";

    private static XElement? BuildFormulaEl(string? formula) =>
        string.IsNullOrWhiteSpace(formula)
            ? null
            : new XElement(C + "f", formula);

    // ── Data sources (CT_NumDataSource / CT_AxDataSource / CT_SerTx) ─────────
    //
    // c:f is REQUIRED inside c:numRef and c:strRef, so a chart with no workbook range to point
    // at (neither RegenerateWorkbookOnSave nor a preserved FormulaReferences entry) cannot use
    // the *Ref form at all. Each of these content models offers a literal alternative that
    // carries the same cached points without a formula — c:numLit, c:strLit, and a bare c:v on
    // c:tx — and that is what the writer falls back to instead of emitting a c:f-less ref.

    /// <summary>Builds the numeric points shared by c:numCache and c:numLit (both CT_NumData).</summary>
    private static IEnumerable<object> BuildNumericDataChildren(IReadOnlyList<double?> values) =>
        new object[]
        {
            new XElement(C + "formatCode", "General"),
            new XElement(C + "ptCount", new XAttribute("val", values.Count)),
        }.Concat(values
            .Select((v, vi) => v.HasValue
                ? new XElement(C + "pt",
                    new XAttribute("idx", vi),
                    new XElement(C + "v", v.Value.ToString("G", CultureInfo.InvariantCulture)))
                : null)
            .Where(e => e is not null)
            .Cast<object>());

    /// <summary>
    /// Builds a c:val/c:xVal/c:yVal/c:bubbleSize wrapper: c:numRef when a formula is available,
    /// c:numLit otherwise.
    /// </summary>
    private static XElement BuildNumericDataSourceEl(
        string wrapperName, string? formula, IReadOnlyList<double?> values)
    {
        var formulaEl = BuildFormulaEl(formula);
        return new XElement(C + wrapperName,
            formulaEl is null
                ? new XElement(C + "numLit", BuildNumericDataChildren(values))
                : new XElement(C + "numRef",
                    formulaEl,
                    new XElement(C + "numCache", BuildNumericDataChildren(values))));
    }

    /// <summary>Builds the string points shared by c:strCache and c:strLit (both CT_StrData).</summary>
    private static IEnumerable<object> BuildStringDataChildren(IReadOnlyList<string> values) =>
        new object[] { new XElement(C + "ptCount", new XAttribute("val", values.Count)) }
            .Concat(values.Select((value, vi) => new XElement(C + "pt",
                new XAttribute("idx", vi),
                new XElement(C + "v", value))));

    /// <summary>Builds a c:cat wrapper: c:strRef when a formula is available, c:strLit otherwise.</summary>
    private static XElement BuildCategoryDataSourceEl(string? formula, IReadOnlyList<string> categories)
    {
        var formulaEl = BuildFormulaEl(formula);
        return new XElement(C + "cat",
            formulaEl is null
                ? new XElement(C + "strLit", BuildStringDataChildren(categories))
                : new XElement(C + "strRef",
                    formulaEl,
                    new XElement(C + "strCache", BuildStringDataChildren(categories))));
    }

    /// <summary>
    /// Builds c:tx. CT_SerTx is a choice of c:strRef or a bare c:v, so a series with no name
    /// formula uses the literal form rather than a c:strRef missing its required c:f.
    /// </summary>
    private static XElement BuildSeriesNameEl(string? formula, string name)
    {
        var formulaEl = BuildFormulaEl(formula);
        return new XElement(C + "tx",
            formulaEl is null
                ? new XElement(C + "v", name)
                : new XElement(C + "strRef",
                    formulaEl,
                    new XElement(C + "strCache", BuildStringDataChildren(new[] { name }))));
    }

    /// <summary>Builds a column c:f range from row 2 through <paramref name="lastRow"/>, e.g. "ChartData!$B$2:$B$4".</summary>
    private static string ColumnRangeRef(int oneBasedColumn, int lastRow)
    {
        var col = ColumnName(oneBasedColumn);
        var effectiveLastRow = Math.Max(lastRow, 2);
        return $"{RegeneratedSheetName}!${col}$2:${col}${effectiveLastRow.ToString(CultureInfo.InvariantCulture)}";
    }

    // ── Axis elements ─────────────────────────────────────────────────────────

    // CT_CatAx sequence: axId, scaling, delete, axPos, majorGridlines, minorGridlines, title,
    // numFmt, majorTickMark, minorTickMark, tickLblPos, spPr, txPr, crossAx, crosses|crossesAt,
    // auto, lblAlgn, lblOffset, tickLblSkip, tickMarkSkip, noMultiLvlLbl.
    private static XElement BuildCatAxEl(ChartAxis axis, int axId, int crossAxId) =>
        new XElement(C + "catAx",
            new XElement(C + "axId", new XAttribute("val", axId)),
            new XElement(C + "scaling",
                new XElement(C + "orientation", new XAttribute("val", axis.ReverseOrder ? "maxMin" : "minMax"))),
            new XElement(C + "delete",
                new XAttribute("val", axis.Delete ? "1" : "0")),
            new XElement(C + "axPos", new XAttribute("val", "b")),
            axis.HasMajorGridlines
                ? new XElement(C + "majorGridlines")
                : null,
            axis.HasMinorGridlines
                ? new XElement(C + "minorGridlines")
                : null,
            axis.Title is not null ? BuildTitleEl(axis.Title, axis.TitleStyle) : null,
            BuildAxisNumFmtEl(axis),
            BuildAxisTickElements(axis),
            new XElement(C + "crossAx", new XAttribute("val", crossAxId)),
            BuildAxisCrossingElement(axis, null),
            BuildCategoryAxisTrailingElements(axis));

    // BV2: axPos parameter — scatter/bubble X value axis must use "b" (bottom), Y stays "l" (left).
    // CA1: crosses parameter — secondary valAx crosses at "max" (right-side position).
    // CT_ValAx sequence: axId, scaling, delete, axPos, majorGridlines, minorGridlines, title,
    // numFmt, majorTickMark, minorTickMark, tickLblPos, spPr, txPr, crossAx, crosses|crossesAt,
    // crossBetween, majorUnit, minorUnit, dispUnits.
    private static XElement BuildValAxEl(ChartAxis axis, int axId, int crossAxId,
        string axPos = "l", string? crosses = null)
    {
        var scalingEl = new XElement(C + "scaling",
            new XElement(C + "orientation", new XAttribute("val", axis.ReverseOrder ? "maxMin" : "minMax")));
        if (axis.Min.HasValue)
            scalingEl.Add(new XElement(C + "min",
                new XAttribute("val", axis.Min.Value.ToString("G", CultureInfo.InvariantCulture))));
        if (axis.Max.HasValue)
            scalingEl.Add(new XElement(C + "max",
                new XAttribute("val", axis.Max.Value.ToString("G", CultureInfo.InvariantCulture))));

        return new XElement(C + "valAx",
            new XElement(C + "axId", new XAttribute("val", axId)),
            scalingEl,
            new XElement(C + "delete",
                new XAttribute("val", axis.Delete ? "1" : "0")),
            new XElement(C + "axPos", new XAttribute("val", axPos)),
            axis.HasMajorGridlines
                ? new XElement(C + "majorGridlines")
                : null,
            axis.HasMinorGridlines
                ? new XElement(C + "minorGridlines")
                : null,
            axis.Title is not null ? BuildTitleEl(axis.Title, axis.TitleStyle) : null,
            BuildAxisNumFmtEl(axis),
            BuildAxisTickElements(axis),
            new XElement(C + "crossAx", new XAttribute("val", crossAxId)),
            BuildAxisCrossingElement(axis, crosses),
            BuildValueAxisTrailingElements(axis));
    }

    /// <summary>
    /// CT_ValAx tail after c:crossAx/c:crosses: crossBetween, majorUnit, minorUnit, dispUnits.
    /// c:crossBetween lives on CT_ValAx only — a category axis that carries one cannot express it.
    /// </summary>
    private static IEnumerable<XElement> BuildValueAxisTrailingElements(ChartAxis axis)
    {
        var crossBetween = axis.CrossBetween;
        if (crossBetween.HasValue || !string.IsNullOrWhiteSpace(axis.RawCrossBetweenToken))
            yield return new XElement(C + "crossBetween", new XAttribute("val", TokenValue(
                crossBetween, axis.RawCrossBetweenToken, CrossBetweenValue)));
        if (axis.MajorUnit is { } majorUnit)
            yield return new XElement(C + "majorUnit", new XAttribute("val", majorUnit.ToString("G", CultureInfo.InvariantCulture)));
        if (axis.MinorUnit is { } minorUnit)
            yield return new XElement(C + "minorUnit", new XAttribute("val", minorUnit.ToString("G", CultureInfo.InvariantCulture)));

        var displayUnit = DisplayUnitValue(axis);
        var customDisplayUnit = axis.DisplayUnit == ChartAxisDisplayUnit.Custom
            && axis.CustomDisplayUnit is { } custom
            && custom > 0
            ? custom
            : (double?)null;
        if (displayUnit is not null || customDisplayUnit is not null)
            yield return new XElement(C + "dispUnits",
                displayUnit is not null
                    ? new XElement(C + "builtInUnit", new XAttribute("val", displayUnit))
                    : new XElement(C + "customUnit", new XAttribute(
                        "val", customDisplayUnit!.Value.ToString("G", CultureInfo.InvariantCulture))));
    }

    /// <summary>
    /// CT_CatAx tail after c:crossAx/c:crosses: auto, lblAlgn, lblOffset, tickLblSkip,
    /// tickMarkSkip, noMultiLvlLbl. None of these exist on CT_ValAx.
    /// </summary>
    private static IEnumerable<XElement> BuildCategoryAxisTrailingElements(ChartAxis axis)
    {
        if (axis.AutoCrossing is { } autoCrossing)
            yield return new XElement(C + "auto", new XAttribute("val", BoolValue(autoCrossing)));
        var labelAlignment = axis.LabelAlignment;
        if (labelAlignment.HasValue || !string.IsNullOrWhiteSpace(axis.RawLabelAlignmentToken))
            yield return new XElement(C + "lblAlgn", new XAttribute("val", TokenValue(
                labelAlignment, axis.RawLabelAlignmentToken, LabelAlignmentValue)));
        if (axis.LabelOffsetPercent is { } offset)
            yield return new XElement(C + "lblOffset", new XAttribute("val", Math.Clamp(offset, 0, 100)));
        if (axis.NoMultiLevelLabels is { } noMultiLevelLabels)
            yield return new XElement(C + "noMultiLvlLbl", new XAttribute("val", BoolValue(noMultiLevelLabels)));
    }

    private static string? DisplayUnitValue(ChartAxis axis) => axis.DisplayUnit switch
    {
        ChartAxisDisplayUnit.None => null,
        ChartAxisDisplayUnit.Hundreds => "hundreds",
        ChartAxisDisplayUnit.Thousands => "thousands",
        ChartAxisDisplayUnit.TenThousands => "tenThousands",
        ChartAxisDisplayUnit.HundredThousands => "hundredThousands",
        ChartAxisDisplayUnit.Millions => "millions",
        ChartAxisDisplayUnit.TenMillions => "tenMillions",
        ChartAxisDisplayUnit.HundredMillions => "hundredMillions",
        ChartAxisDisplayUnit.Billions => "billions",
        ChartAxisDisplayUnit.Trillions => "trillions",
        ChartAxisDisplayUnit.Custom => null,
        ChartAxisDisplayUnit.Unsupported => string.IsNullOrWhiteSpace(axis.RawDisplayUnitToken)
            ? null
            : axis.RawDisplayUnitToken,
        _ => null,
    };

    /// <summary>
    /// The shared EG_AxShared tick block, which sits between c:numFmt and c:crossAx on every
    /// axis kind (catAx, valAx, serAx, dateAx).
    /// </summary>
    private static IEnumerable<XElement> BuildAxisTickElements(ChartAxis axis)
    {
        var major = axis.MajorTickMark;
        if (major.HasValue || !string.IsNullOrWhiteSpace(axis.RawMajorTickMarkToken))
            yield return new XElement(C + "majorTickMark", new XAttribute("val", TokenValue(
                major, axis.RawMajorTickMarkToken, TickMarkValue)));
        var minor = axis.MinorTickMark;
        if (minor.HasValue || !string.IsNullOrWhiteSpace(axis.RawMinorTickMarkToken))
            yield return new XElement(C + "minorTickMark", new XAttribute("val", TokenValue(
                minor, axis.RawMinorTickMarkToken, TickMarkValue)));
        var position = axis.TickLabelPosition;
        if (position.HasValue || !string.IsNullOrWhiteSpace(axis.RawTickLabelPositionToken))
            yield return new XElement(C + "tickLblPos", new XAttribute("val", TokenValue(
                position, axis.RawTickLabelPositionToken, TickLabelPositionValue)));
    }

    private static XElement? BuildAxisCrossingElement(ChartAxis axis, string? fallback)
    {
        if (axis.CrossesAt is { } crossesAt)
            return new XElement(C + "crossesAt", new XAttribute("val", crossesAt.ToString("G", CultureInfo.InvariantCulture)));

        var crossing = axis.Crosses is { } authored
            ? AxisCrossingValue(authored)
            : axis.RawCrossesToken ?? fallback;
        return crossing is null
            ? null
            : new XElement(C + "crosses", new XAttribute("val", crossing));
    }

    private static string TokenValue<T>(T? value, string? rawToken, Func<T, string> knownValue)
        where T : struct => rawToken ?? (value is { } known ? knownValue(known) : string.Empty);

    private static string TickMarkValue(ChartTickMark value) => value switch
    {
        ChartTickMark.None  => "none",
        ChartTickMark.Cross => "cross",
        ChartTickMark.In    => "in",
        ChartTickMark.Out   => "out",
        _                   => "none"
    };

    private static string TickLabelPositionValue(ChartTickLabelPosition value) => value switch
    {
        ChartTickLabelPosition.None   => "none",
        ChartTickLabelPosition.Low    => "low",
        ChartTickLabelPosition.High   => "high",
        ChartTickLabelPosition.NextTo => "nextTo",
        _                            => "nextTo"
    };

    private static string CrossBetweenValue(ChartCrossBetween value) => value switch
    {
        ChartCrossBetween.Between => "between",
        ChartCrossBetween.MidCat  => "midCat",
        _                         => "between"
    };

    private static string LabelAlignmentValue(ChartLabelAlignment value) => value switch
    {
        ChartLabelAlignment.Left   => "l",
        ChartLabelAlignment.Center => "ctr",
        ChartLabelAlignment.Right  => "r",
        _                          => "ctr"
    };

    private static string AxisCrossingValue(ChartAxisCrossing value) => value switch
    {
        ChartAxisCrossing.AutoZero => "autoZero",
        ChartAxisCrossing.Min      => "min",
        ChartAxisCrossing.Max      => "max",
        _                          => "autoZero"
    };

    private static XElement? BuildAxisNumFmtEl(ChartAxis axis)
    {
        if (string.IsNullOrWhiteSpace(axis.NumberFormatCode))
            return null;

        var el = new XElement(C + "numFmt",
            new XAttribute("formatCode", axis.NumberFormatCode));
        if (axis.NumberFormatSourceLinked.HasValue)
            el.Add(new XAttribute(
                "sourceLinked",
                axis.NumberFormatSourceLinked.Value ? "1" : "0"));

        return el;
    }

    // ── Color helpers ─────────────────────────────────────────────────────────

    private static XElement BuildColorEl(ThemeAwareColor color)
    {
        XElement el;
        if (color.SchemeColor is { } sc)
        {
            el = new XElement(A + "schemeClr",
                new XAttribute("val", PptxColorReader.ToSchemeColorString(sc.Slot)));
            if (Math.Abs(sc.LumMod - 1.0) > 1e-9)
                el.Add(new XElement(A + "lumMod",
                    new XAttribute("val", (long)Math.Round(sc.LumMod * 100000))));
            if (Math.Abs(sc.LumOff) > 1e-9)
                el.Add(new XElement(A + "lumOff",
                    new XAttribute("val", (long)Math.Round(sc.LumOff * 100000))));
        }
        else
        {
            el = new XElement(A + "srgbClr",
                new XAttribute("val", $"{color.Resolved.R:X2}{color.Resolved.G:X2}{color.Resolved.B:X2}"));
        }

        AddAlphaEl(el, color.Alpha);
        return el;
    }

    private static void AddAlphaEl(XElement colorEl, byte alpha)
    {
        if (alpha < byte.MaxValue)
            colorEl.Add(new XElement(A + "alpha", new XAttribute("val", (long)Math.Round(alpha / 255.0 * 100000))));
    }

    // ── Zip helpers ───────────────────────────────────────────────────────────

    private static void WriteEntry(ZipArchive archive, string path, XDocument doc)
    {
        // Chart titles and cached category/series text are user-typed, so they can carry characters
        // XML cannot represent. This writer serializes its own parts, so it needs the same guard as
        // PptxPackageWriter.WriteEntry — without it one such character aborts the entire save.
        Free.Shared.Opc.OoxmlXmlText.SanitizeInPlace(doc);

        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = System.Xml.XmlWriter.Create(stream, XmlSettings);
        doc.Save(writer);
    }

    private static void WriteBytes(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void AddExternalData(XDocument chartDoc, string workbookRelId)
    {
        if (chartDoc.Root is null)
            return;

        chartDoc.Root.Element(C + "externalData")?.Remove();
        chartDoc.Root.Add(new XElement(C + "externalData",
            new XAttribute(R + "id", workbookRelId),
            new XElement(C + "autoUpdate", new XAttribute("val", "0"))));
    }

    private static void WriteRegeneratedWorkbook(
        ZipArchive archive,
        ChartShape chart,
        string workbookPath)
    {
        using var workbookStream = new MemoryStream();
        using (var workbookArchive = new ZipArchive(workbookStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(workbookArchive, "[Content_Types].xml", BuildWorkbookContentTypes());
            WriteEntry(workbookArchive, "_rels/.rels", BuildWorkbookRootRels());
            WriteEntry(workbookArchive, "xl/workbook.xml", BuildWorkbookXml());
            WriteEntry(workbookArchive, "xl/_rels/workbook.xml.rels", BuildWorkbookRels());
            WriteEntry(workbookArchive, "xl/styles.xml", BuildWorkbookStylesXml());
            WriteEntry(workbookArchive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(chart));
        }

        WriteBytes(archive, workbookPath, workbookStream.ToArray());
    }

    private static void WriteRegeneratedWorkbookRelationship(
        ZipArchive archive,
        string chartPath,
        string workbookRelId,
        int chartIndex)
    {
        var workbookPath = GetRegeneratedWorkbookPath(chartIndex);
        var relationship = OpcRelationships.CreateRelationship(
            workbookRelId,
            PackageRelType,
            $"../embeddings/{workbookPath.Split('/').Last()}",
            false);

        WriteEntry(
            archive,
            OpcPathHelper.GetRelationshipPartPath(chartPath),
            OpcRelationships.CreateDocument(new[] { relationship }));
    }

    private static XDocument BuildWorkbookContentTypes()
    {
        var ct = OpcMediaTypes.ContentTypesNamespace;
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ct + "Types",
                new XElement(ct + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", OpcMediaTypes.RelationshipsContentType)),
                new XElement(ct + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(ct + "Override",
                    new XAttribute("PartName", "/xl/workbook.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(ct + "Override",
                    new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
                new XElement(ct + "Override",
                    new XAttribute("PartName", "/xl/styles.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"))));
    }

    private static XDocument BuildWorkbookRootRels()
    {
        XNamespace rels = "http://schemas.openxmlformats.org/package/2006/relationships";
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(rels + "Relationships",
                new XElement(rels + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml"))));
    }

    private static XDocument BuildWorkbookXml()
    {
        XNamespace ss = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ss + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", rel.NamespaceName),
                new XElement(ss + "sheets",
                    new XElement(ss + "sheet",
                        new XAttribute("name", RegeneratedSheetName),
                        new XAttribute("sheetId", "1"),
                        new XAttribute(rel + "id", "rId1")))));
    }

    private static XDocument BuildWorkbookRels()
    {
        XNamespace rels = "http://schemas.openxmlformats.org/package/2006/relationships";
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(rels + "Relationships",
                new XElement(rels + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", "worksheets/sheet1.xml")),
                new XElement(rels + "Relationship",
                    new XAttribute("Id", "rId2"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                    new XAttribute("Target", "styles.xml"))));
    }

    private static XDocument BuildWorkbookStylesXml()
    {
        XNamespace ss = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ss + "styleSheet",
                new XElement(ss + "fonts",
                    new XAttribute("count", "1"),
                    new XElement(ss + "font")),
                new XElement(ss + "fills",
                    new XAttribute("count", "1"),
                    new XElement(ss + "fill")),
                new XElement(ss + "borders",
                    new XAttribute("count", "1"),
                    new XElement(ss + "border")),
                new XElement(ss + "cellStyleXfs",
                    new XAttribute("count", "1"),
                    new XElement(ss + "xf",
                        new XAttribute("numFmtId", "0"),
                        new XAttribute("fontId", "0"),
                        new XAttribute("fillId", "0"),
                        new XAttribute("borderId", "0"))),
                new XElement(ss + "cellXfs",
                    new XAttribute("count", "1"),
                    new XElement(ss + "xf",
                        new XAttribute("numFmtId", "0"),
                        new XAttribute("fontId", "0"),
                        new XAttribute("fillId", "0"),
                        new XAttribute("borderId", "0"),
                        new XAttribute("xfId", "0")))));
    }

    /// <summary>
    /// Regenerated-workbook sheet name. Must match the c:f ranges emitted by
    /// <see cref="BuildSeriesEl"/>/<see cref="BuildScatterSeriesEl"/> (ID2) and the
    /// sheet name registered in <see cref="BuildWorkbookXml"/>.
    /// </summary>
    private const string RegeneratedSheetName = "ChartData";

    /// <summary>
    /// ID1: describes, per column role, which 1-based worksheet column a chart element maps to.
    /// Shared between <see cref="BuildWorksheetXml"/> (which lays the columns out) and the
    /// c:f range builders in <see cref="BuildSeriesEl"/>/<see cref="BuildScatterSeriesEl"/> (ID2),
    /// so the cached data and the formula ranges always address the same cells.
    /// </summary>
    private readonly record struct WorksheetLayout(
        bool IsScatterLike,
        int CategoryColumn,
        IReadOnlyList<int> XColumns,
        IReadOnlyList<int> ValueColumns,
        IReadOnlyList<int> BubbleSizeColumns);

    /// <summary>
    /// ID1/ID2: computes the worksheet column layout for a chart's regenerated workbook.
    /// Category charts: col A = categories, cols B.. = one column per series' Y values.
    /// Scatter charts: two columns per series (X, then Y).
    /// Bubble charts: three columns per series (X, Y, then bubble size).
    /// </summary>
    private static WorksheetLayout BuildWorksheetLayout(ChartShape chart)
    {
        bool isBubble  = chart.ChartType == ChartType.Bubble;
        bool isScatter = chart.ChartType == ChartType.Scatter || isBubble;

        var xColumns     = new List<int>();
        var valueColumns = new List<int>();
        var sizeColumns  = new List<int>();

        if (isScatter)
        {
            var nextColumn = 1;
            for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
            {
                xColumns.Add(nextColumn++);
                valueColumns.Add(nextColumn++);
                if (isBubble)
                    sizeColumns.Add(nextColumn++);
            }

            return new WorksheetLayout(true, CategoryColumn: 0, xColumns, valueColumns, sizeColumns);
        }

        // Category charts: col A is categories, cols B.. are series values.
        for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
            valueColumns.Add(seriesIndex + 2);

        return new WorksheetLayout(false, CategoryColumn: 1, xColumns, valueColumns, sizeColumns);
    }

    private static XDocument BuildWorksheetXml(ChartShape chart)
    {
        XNamespace ss = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = new List<XElement>();
        var layout = BuildWorksheetLayout(chart);

        if (layout.IsScatterLike)
        {
            // Scatter/bubble: X (+ Y, + size) columns per series; header row names each series
            // over its Y (value) column.
            var headerCells = new List<XElement>();
            for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
                headerCells.Add(InlineStringCell(ss, layout.ValueColumns[seriesIndex], 1, chart.Series[seriesIndex].Name));
            rows.Add(new XElement(ss + "row", new XAttribute("r", "1"), headerCells));

            var pointCount = chart.Series.Count == 0
                ? 0
                : chart.Series.Max(series => Math.Max(
                    series.Values.Count,
                    Math.Max(series.XValues.Count, series.BubbleSizes.Count)));

            for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                var cells = new List<XElement>();
                for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
                {
                    var series = chart.Series[seriesIndex];

                    if (pointIndex < series.XValues.Count && series.XValues[pointIndex].HasValue)
                        cells.Add(NumberCell(ss, layout.XColumns[seriesIndex], pointIndex + 2, series.XValues[pointIndex]!.Value));

                    if (pointIndex < series.Values.Count && series.Values[pointIndex].HasValue)
                        cells.Add(NumberCell(ss, layout.ValueColumns[seriesIndex], pointIndex + 2, series.Values[pointIndex]!.Value));

                    if (layout.BubbleSizeColumns.Count > 0 &&
                        pointIndex < series.BubbleSizes.Count && series.BubbleSizes[pointIndex].HasValue)
                        cells.Add(NumberCell(ss, layout.BubbleSizeColumns[seriesIndex], pointIndex + 2, series.BubbleSizes[pointIndex]!.Value));
                }

                cells.Sort((a, b) => string.CompareOrdinal(a.Attribute("r")?.Value, b.Attribute("r")?.Value));
                rows.Add(new XElement(ss + "row",
                    new XAttribute("r", (pointIndex + 2).ToString(CultureInfo.InvariantCulture)),
                    cells));
            }

            return new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(ss + "worksheet",
                    new XElement(ss + "sheetData", rows)));
        }

        // Category charts (bar/line/pie/area/etc.): col A = categories, cols B.. = series values.
        var catHeaderCells = new List<XElement>();
        for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
            catHeaderCells.Add(InlineStringCell(ss, layout.ValueColumns[seriesIndex], 1, chart.Series[seriesIndex].Name));
        rows.Add(new XElement(ss + "row", new XAttribute("r", "1"), catHeaderCells));

        var catPointCount = Math.Max(
            chart.Categories.Count,
            chart.Series.Count == 0 ? 0 : chart.Series.Max(series => series.Values.Count));
        for (var pointIndex = 0; pointIndex < catPointCount; pointIndex++)
        {
            var cells = new List<XElement>();
            var category = pointIndex < chart.Categories.Count
                ? chart.Categories[pointIndex]
                : string.Empty;
            cells.Add(InlineStringCell(ss, layout.CategoryColumn, pointIndex + 2, category));

            for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
            {
                var values = chart.Series[seriesIndex].Values;
                if (pointIndex < values.Count && values[pointIndex].HasValue)
                    cells.Add(NumberCell(ss, layout.ValueColumns[seriesIndex], pointIndex + 2, values[pointIndex]!.Value));
            }

            rows.Add(new XElement(ss + "row",
                new XAttribute("r", (pointIndex + 2).ToString(CultureInfo.InvariantCulture)),
                cells));
        }

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ss + "worksheet",
                new XElement(ss + "sheetData", rows)));
    }

    private static XElement InlineStringCell(XNamespace ss, int column, int row, string value) =>
        new XElement(ss + "c",
            new XAttribute("r", CellReference(column, row)),
            new XAttribute("t", "inlineStr"),
            new XElement(ss + "is",
                new XElement(ss + "t", value)));

    private static XElement NumberCell(XNamespace ss, int column, int row, double value) =>
        new XElement(ss + "c",
            new XAttribute("r", CellReference(column, row)),
            new XElement(ss + "v", value.ToString("G", CultureInfo.InvariantCulture)));

    private static string CellReference(int column, int row) =>
        ColumnName(column) + row.ToString(CultureInfo.InvariantCulture);

    private static string BoolValue(bool value) => value ? "1" : "0";

    private static string ColumnName(int oneBasedColumn)
    {
        var column = oneBasedColumn;
        var name = string.Empty;
        while (column > 0)
        {
            column--;
            name = (char)('A' + column % 26) + name;
            column /= 26;
        }

        return name;
    }

    private static void MergePreservedExternalData(
        XDocument chartDoc,
        PptxPackageSnapshot? packageSnapshot,
        string chartPath)
    {
        var sourceChart = TryReadSnapshotXml(packageSnapshot, chartPath);
        var sourceExternalData = sourceChart?.Root?.Element(C + "externalData");
        if (sourceExternalData is null || chartDoc.Root is null)
            return;

        chartDoc.Root.Element(C + "externalData")?.Remove();
        chartDoc.Root.Add(new XElement(sourceExternalData));
    }

    private static void WritePreservedWorkbookRelationships(
        ZipArchive archive,
        PptxPackageSnapshot? packageSnapshot,
        string sourceChartPath,
        string outputChartPath)
    {
        var sourceRelsPath = OpcPathHelper.GetRelationshipPartPath(sourceChartPath);
        var outputRelsPath = OpcPathHelper.GetRelationshipPartPath(outputChartPath);
        var sourceRels = TryReadSnapshotXml(packageSnapshot, sourceRelsPath);
        if (sourceRels is null)
            return;

        // A preserved (untouched) chart's own rels can reference more than its embedded
        // workbook: the chartStyle/chartColorStyle relationships that carry the chart's
        // PowerPoint-2013+ style and color-scheme sidecars live here too. Keep every
        // relationship whose internal target part still exists in the snapshot (or that is
        // external, which never resolves against the package and is preserved as-is) —
        // not just the workbook one — so those sidecars stay wired up after a save that
        // never touched this chart. (Mirrors the wholesale-preserve approach
        // WriteChartExPart already uses for ChartEx sidecars.)
        var sourceDirectory = OpcPathHelper.GetDirectoryName(sourceChartPath);
        var preservedRelationships = OpcRelationships.Load(sourceRels)
            .Where(relationship =>
                relationship.IsExternal ||
                (!string.IsNullOrWhiteSpace(relationship.Target) &&
                 packageSnapshot?.TryGetEntry(
                     OpcPathHelper.ResolveRelativeZipPath(sourceDirectory, relationship.Target),
                     out _) == true))
            .ToArray();
        if (preservedRelationships.Length == 0)
            return;

        WriteEntry(
            archive,
            outputRelsPath,
            OpcRelationships.CreateDocument(preservedRelationships.Select(relationship =>
                OpcRelationships.CreateRelationship(
                    relationship.Id,
                    relationship.Type,
                    relationship.Target,
                    relationship.IsExternal))));
    }

    private static XDocument? TryReadSnapshotXml(PptxPackageSnapshot? packageSnapshot, string path) =>
        packageSnapshot is not null && packageSnapshot.TryGetEntry(path, out var bytes)
            ? OpcXml.TryLoadXml(bytes)
            : null;

    private static XAttribute NsAttr(string prefix, XNamespace ns) =>
        new XAttribute(XNamespace.Xmlns + prefix, ns.NamespaceName);
}
