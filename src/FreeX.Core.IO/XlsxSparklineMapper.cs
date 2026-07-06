using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxSparklineMapper
{
    // The URI that identifies the sparkline <ext> inside the worksheet extLst.
    private const string SparklineExtUri = "{05C60535-1F16-4fd2-B633-F4F36F0B64E0}";

    public static IReadOnlyList<SparklineModel> Read(XDocument worksheetXml)
    {
        var extensionList = FindChildByLocalName(worksheetXml.Root, "extLst");
        if (extensionList is null)
            return [];

        var result = new List<SparklineModel>();
        var tempSheet = SheetId.New();
        int groupCounter = 0;

        foreach (var group in extensionList.Descendants().Where(element =>
                     string.Equals(element.Name.LocalName, "sparklineGroup", StringComparison.OrdinalIgnoreCase)))
        {
            int groupId = ++groupCounter;

            // ── type ───────────────────────────────────────────────────────────
            var kind = group.Attribute("type")?.Value switch
            {
                "column" => SparklineKind.Column,
                "stacked" or "winLoss" => SparklineKind.WinLoss,
                _ => SparklineKind.Line
            };

            // ── scalar attributes ──────────────────────────────────────────────
            var lineWeight    = ParseDoubleAttr(group, "lineWeight");
            var showMarkers   = ParseBoolAttr(group, "markers");
            var showHigh      = ParseBoolAttr(group, "high");
            var showLow       = ParseBoolAttr(group, "low");
            var showFirst     = ParseBoolAttr(group, "first");
            var showLast      = ParseBoolAttr(group, "last");
            var showNegative  = ParseBoolAttr(group, "negative");
            var showAxis      = ParseBoolAttr(group, "displayXAxis");
            var displayHidden = ParseBoolAttr(group, "displayHidden");
            var rightToLeft   = ParseBoolAttr(group, "rightToLeft");

            var minAxisType   = ParseAxisScaling(group, "minAxisType");
            var maxAxisType   = ParseAxisScaling(group, "maxAxisType");
            var manualMin     = ParseDoubleAttr(group, "manualMin");
            var manualMax     = ParseDoubleAttr(group, "manualMax");

            var emptyCells    = ParseEmptyCells(group, "displayEmptyCellsAs");

            // ── color sub-elements ─────────────────────────────────────────────
            var seriesColor   = ReadColorElement(group, "colorSeries");
            var negativeColor = ReadColorElement(group, "colorNegative");
            var axisColor     = ReadColorElement(group, "colorAxis");
            var markersColor  = ReadColorElement(group, "colorMarkers");
            var firstColor    = ReadColorElement(group, "colorFirst");
            var lastColor     = ReadColorElement(group, "colorLast");
            var highColor     = ReadColorElement(group, "colorHigh");
            var lowColor      = ReadColorElement(group, "colorLow");

            // ── date axis ──────────────────────────────────────────────────────
            var dateAxisRange = ReadDateAxisRange(group, tempSheet);

            foreach (var sparkline in group.Descendants().Where(element =>
                         string.Equals(element.Name.LocalName, "sparkline", StringComparison.OrdinalIgnoreCase)))
            {
                var formula  = FindChildByLocalName(sparkline, "f")?.Value;
                var location = FindChildByLocalName(sparkline, "sqref")?.Value;
                if (string.IsNullOrWhiteSpace(formula) || string.IsNullOrWhiteSpace(location))
                    continue;

                var bang      = formula.LastIndexOf('!');
                var rangeText = bang >= 0 ? formula[(bang + 1)..] : formula;
                rangeText = rangeText.Replace("$", "", StringComparison.Ordinal);
                location  = location.Replace("$", "", StringComparison.Ordinal);
                try
                {
                    result.Add(new SparklineModel
                    {
                        DataRange        = GridRange.Parse(rangeText, tempSheet),
                        Location         = CellAddress.Parse(location, tempSheet),
                        Kind             = kind,
                        GroupId          = groupId,
                        LineWeight       = lineWeight,
                        ShowMarkers      = showMarkers,
                        ShowHighPoint    = showHigh,
                        ShowLowPoint     = showLow,
                        ShowFirstPoint   = showFirst,
                        ShowLastPoint    = showLast,
                        ShowNegativePoints = showNegative,
                        ShowAxis         = showAxis,
                        DisplayHidden    = displayHidden,
                        RightToLeft      = rightToLeft,
                        MinAxisType      = minAxisType,
                        MaxAxisType      = maxAxisType,
                        ManualMin        = manualMin,
                        ManualMax        = manualMax,
                        DisplayEmptyCellsAs = emptyCells,
                        SeriesColor      = seriesColor,
                        NegativeColor    = negativeColor,
                        AxisColor        = axisColor,
                        MarkersColor     = markersColor,
                        HighPointColor   = highColor,
                        LowPointColor    = lowColor,
                        FirstPointColor  = firstColor,
                        LastPointColor   = lastColor,
                        DateAxisRange    = dateAxisRange,
                    });
                }
                catch
                {
                    // Skip malformed sparkline references.
                }
            }
        }

        return result;
    }

    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry     = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var relsXml     = XlsxPackageXmlEditor.LoadXml(relsEntry);

        XNamespace workbookNs   = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs        = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace x14Ns        = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        XNamespace xmNs         = "http://schemas.microsoft.com/office/excel/2006/main";

        var relTargets = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => XlsxPackagePath.NormalizeWorkbookTarget(e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name  = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                sheet.Sparklines.Count == 0 ||
                !relTargets.TryGetValue(relId, out var worksheetPath))
            {
                continue;
            }

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            // ── IO4: preserve unknown <ext> children; only replace the sparkline ext ──
            var validSparklines = sheet.Sparklines
                .Where(sparkline =>
                    sparkline.DataRange.Start.Sheet == sheet.Id &&
                    sparkline.DataRange.End.Sheet   == sheet.Id &&
                    sparkline.Location.Sheet        == sheet.Id &&
                    Enum.IsDefined(sparkline.Kind))
                .ToList();

            // Build the new sparklineGroups element.
            // IO3: group by GroupId (preserves per-group settings). GroupId == 0 means the
            // sparkline was created independently in-app (never assigned a shared group id at
            // XLSX read time), so each such sparkline is its own singleton group keyed by its
            // unique model Id — grouping them by Kind instead would silently merge unrelated
            // same-kind sparklines (and their distinct colors/markers/axis settings) into one
            // shared x14:sparklineGroup, which is not what independently-inserted sparklines are.
            var sparklineGroupsXml = new XElement(
                x14Ns + "sparklineGroups",
                validSparklines
                    .GroupBy(s => s.GroupId == 0 ? (object)s.Id : (object)s.GroupId)
                    .Select(group =>
                    {
                        var representative = group.First();
                        return ToSparklineGroupXml(sheet, representative, group, x14Ns, xmNs);
                    }));

            var newSparklineExt = new XElement(
                workbookNs + "ext",
                new XAttribute("uri", SparklineExtUri),
                sparklineGroupsXml);

            // Ensure x14/xm namespace declarations exist on root.
            root.SetAttributeValue(XNamespace.Xmlns + "x14", x14Ns.NamespaceName);
            root.SetAttributeValue(XNamespace.Xmlns + "xm",  xmNs.NamespaceName);

            // Locate or create the extLst on the worksheet root.
            // Remove any duplicate extLst elements first (schema allows at most one).
            var allExtLst = root.Elements(workbookNs + "extLst").ToList();
            foreach (var extra in allExtLst.Skip(1))
                extra.Remove();

            XElement extLst;
            if (allExtLst.Count == 0)
            {
                extLst = new XElement(workbookNs + "extLst");
                root.Add(extLst);
            }
            else
            {
                extLst = allExtLst[0];
            }

            // Remove any existing sparkline ext (by URI), keep all other <ext> children.
            extLst.Elements(workbookNs + "ext")
                .Where(e => string.Equals(
                    e.Attribute("uri")?.Value?.Trim(),
                    SparklineExtUri,
                    StringComparison.OrdinalIgnoreCase))
                .ToList()
                .ForEach(e => e.Remove());

            // Also strip any schema-invalid attributes/children from extLst itself
            // (mirrors the sanitisation already tested by XlsxNonChartSchemaValidationTests).
            foreach (var attr in extLst.Attributes()
                         .Where(a => !a.IsNamespaceDeclaration)
                         .ToList())
                attr.Remove();
            foreach (var child in extLst.Elements()
                         .Where(e => !string.Equals(e.Name.LocalName, "ext", StringComparison.OrdinalIgnoreCase))
                         .ToList())
                child.Remove();

            // Clean up stale/empty <ext> entries (no uri or whitespace-only uri).
            extLst.Elements(workbookNs + "ext")
                .Where(e => string.IsNullOrWhiteSpace(e.Attribute("uri")?.Value))
                .ToList()
                .ForEach(e => e.Remove());

            // Remove duplicate sparkline URIs (keep none — we'll add the fresh one).
            extLst.Add(newSparklineExt);

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static XElement? FindChildByLocalName(XElement? element, string localName)
    {
        if (element is null)
            return null;

        foreach (var child in element.Elements())
        {
            if (string.Equals(child.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    private static XElement ToSparklineGroupXml(
        Sheet sheet,
        SparklineModel representative,
        IEnumerable<SparklineModel> sparklines,
        XNamespace x14Ns,
        XNamespace xmNs)
    {
        var el = new XElement(x14Ns + "sparklineGroup",
            new XAttribute("type", ToSparklineType(representative.Kind)));

        // lineWeight
        if (representative.LineWeight.HasValue)
            el.Add(new XAttribute("lineWeight",
                representative.LineWeight.Value.ToString("G", CultureInfo.InvariantCulture)));

        // displayEmptyCellsAs (omit when default Gap)
        if (representative.DisplayEmptyCellsAs != SparklineEmptyCellDisplay.Gap)
            el.Add(new XAttribute("displayEmptyCellsAs",
                ToEmptyCellsAttr(representative.DisplayEmptyCellsAs)));

        // boolean show-flags (omit when false = default)
        if (representative.ShowMarkers)
            el.Add(new XAttribute("markers", "1"));
        if (representative.ShowHighPoint)
            el.Add(new XAttribute("high", "1"));
        if (representative.ShowLowPoint)
            el.Add(new XAttribute("low", "1"));
        if (representative.ShowFirstPoint)
            el.Add(new XAttribute("first", "1"));
        if (representative.ShowLastPoint)
            el.Add(new XAttribute("last", "1"));
        if (representative.ShowNegativePoints)
            el.Add(new XAttribute("negative", "1"));
        if (representative.ShowAxis)
            el.Add(new XAttribute("displayXAxis", "1"));
        if (representative.DisplayHidden)
            el.Add(new XAttribute("displayHidden", "1"));
        if (representative.RightToLeft)
            el.Add(new XAttribute("rightToLeft", "1"));

        // axis scaling (omit when both are Individual = default)
        if (representative.MinAxisType != SparklineAxisScaling.Individual)
            el.Add(new XAttribute("minAxisType", ToAxisScalingAttr(representative.MinAxisType)));
        if (representative.MaxAxisType != SparklineAxisScaling.Individual)
            el.Add(new XAttribute("maxAxisType", ToAxisScalingAttr(representative.MaxAxisType)));
        if (representative.ManualMin.HasValue)
            el.Add(new XAttribute("manualMin",
                representative.ManualMin.Value.ToString("G", CultureInfo.InvariantCulture)));
        if (representative.ManualMax.HasValue)
            el.Add(new XAttribute("manualMax",
                representative.ManualMax.Value.ToString("G", CultureInfo.InvariantCulture)));

        // color sub-elements (order matches OOXML schema)
        AddColorElement(el, x14Ns, "colorSeries",   representative.SeriesColor);
        AddColorElement(el, x14Ns, "colorNegative",  representative.NegativeColor);
        AddColorElement(el, x14Ns, "colorAxis",      representative.AxisColor);
        AddColorElement(el, x14Ns, "colorMarkers",   representative.MarkersColor);
        AddColorElement(el, x14Ns, "colorFirst",     representative.FirstPointColor);
        AddColorElement(el, x14Ns, "colorLast",      representative.LastPointColor);
        AddColorElement(el, x14Ns, "colorHigh",      representative.HighPointColor);
        AddColorElement(el, x14Ns, "colorLow",       representative.LowPointColor);

        // dateAxis (schema order: after the color elements, before the sparklines list)
        if (representative.DateAxisRange is { } dateAxisRange)
        {
            el.Add(new XElement(
                x14Ns + "dateAxis",
                new XElement(xmNs + "f", $"{SheetNameFormatter.QuoteIfNeeded(sheet.Name)}!{dateAxisRange}")));
        }

        // sparklines list
        el.Add(new XElement(
            x14Ns + "sparklines",
            sparklines.Select(sparkline => new XElement(
                x14Ns + "sparkline",
                new XElement(xmNs + "f",     $"{SheetNameFormatter.QuoteIfNeeded(sheet.Name)}!{sparkline.DataRange}"),
                new XElement(xmNs + "sqref", sparkline.Location.ToA1())))));

        return el;
    }

    private static void AddColorElement(XElement parent, XNamespace x14Ns, string localName, CellColor? color)
    {
        if (color is null)
            return;
        parent.Add(new XElement(x14Ns + localName,
            new XAttribute("rgb", $"FF{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}")));
    }

    /// <summary>
    /// Reads the group's optional &lt;x14:dateAxis&gt;&lt;xm:f&gt;range&lt;/xm:f&gt;&lt;/x14:dateAxis&gt;
    /// (Excel's sparkline "Date Axis Type" setting). The formula may be sheet-qualified
    /// (e.g. "Sheet1!$A$1:$A$5"); only the range portion is kept, resolved against
    /// <paramref name="sheet"/> like the sparkline data-range/location references.
    /// </summary>
    private static GridRange? ReadDateAxisRange(XElement group, SheetId sheet)
    {
        var dateAxis = group.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, "dateAxis", StringComparison.OrdinalIgnoreCase));
        var formula = FindChildByLocalName(dateAxis, "f")?.Value;
        if (string.IsNullOrWhiteSpace(formula))
            return null;

        var bang = formula.LastIndexOf('!');
        var rangeText = bang >= 0 ? formula[(bang + 1)..] : formula;
        rangeText = rangeText.Replace("$", "", StringComparison.Ordinal);
        try
        {
            return GridRange.ParseCellOrRange(rangeText, sheet);
        }
        catch
        {
            return null;
        }
    }

    private static CellColor? ReadColorElement(XElement group, string localName)
    {
        var el = group.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
        if (el is null)
            return null;

        // Try rgb attribute first (AARRGGBB hex string)
        var rgb = el.Attribute("rgb")?.Value;
        if (!string.IsNullOrWhiteSpace(rgb) && rgb.Length >= 6)
        {
            // Strip leading alpha bytes if present (AARRGGBB → RRGGBB)
            var hex = rgb.TrimStart('#');
            if (hex.Length == 8)
                hex = hex[2..]; // drop AA
            if (hex.Length == 6 &&
                byte.TryParse(hex[0..2], NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(hex[2..4], NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(hex[4..6], NumberStyles.HexNumber, null, out var b))
            {
                return new CellColor(r, g, b);
            }
        }

        return null;
    }

    // ── Attribute parsers ──────────────────────────────────────────────────────

    private static bool ParseBoolAttr(XElement el, string name)
    {
        var val = el.Attribute(name)?.Value;
        if (val is null) return false;
        // Excel writes "1"/"0" or "true"/"false"
        return val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static double? ParseDoubleAttr(XElement el, string name)
    {
        var val = el.Attribute(name)?.Value;
        if (val is null) return null;
        return double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d : null;
    }

    private static SparklineAxisScaling ParseAxisScaling(XElement el, string name)
    {
        return el.Attribute(name)?.Value?.ToLowerInvariant() switch
        {
            "group"  => SparklineAxisScaling.Group,
            "custom" => SparklineAxisScaling.Custom,
            _        => SparklineAxisScaling.Individual,
        };
    }

    private static SparklineEmptyCellDisplay ParseEmptyCells(XElement el, string name)
    {
        return el.Attribute(name)?.Value?.ToLowerInvariant() switch
        {
            "zero" => SparklineEmptyCellDisplay.Zero,
            "span" => SparklineEmptyCellDisplay.Span,
            _      => SparklineEmptyCellDisplay.Gap,
        };
    }

    private static string ToSparklineType(SparklineKind kind) =>
        kind switch
        {
            SparklineKind.Column  => "column",
            SparklineKind.WinLoss => "stacked",
            _                     => "line"
        };

    private static string ToAxisScalingAttr(SparklineAxisScaling scaling) =>
        scaling switch
        {
            SparklineAxisScaling.Group  => "group",
            SparklineAxisScaling.Custom => "custom",
            _                           => "individual",
        };

    private static string ToEmptyCellsAttr(SparklineEmptyCellDisplay display) =>
        display switch
        {
            SparklineEmptyCellDisplay.Zero => "zero",
            SparklineEmptyCellDisplay.Span => "span",
            _                              => "gap",
        };
}
