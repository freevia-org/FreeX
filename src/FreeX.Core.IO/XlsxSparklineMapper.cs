using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// A sparkline parsed from a single worksheet's XML in isolation (before the full workbook and its
/// sheet-name → <see cref="SheetId"/> map exist), paired with the raw sheet-name qualifiers from its
/// data-range and date-axis <c>&lt;xm:f&gt;</c> formulas. Excel's Sparkline "Edit Data" dialog allows a
/// cross-sheet source range (e.g. a sparkline hosted on Sheet1 whose data is <c>Sheet2!$A$1:$E$1</c>),
/// so the qualifying sheet NAME must be preserved here and resolved to the correct sheet by the caller
/// once the workbook is assembled. A <see langword="null"/> qualifier means the formula had no sheet
/// prefix (the common same-sheet case) — the caller anchors that range to the host sheet.
/// </summary>
internal sealed record XlsxSparklineLayout(
    SparklineModel Sparkline,
    string? DataRangeSheetName,
    string? DateAxisSheetName);

internal static class XlsxSparklineMapper
{
    // The URI that identifies the sparkline <ext> inside the worksheet extLst.
    private const string SparklineExtUri = "{05C60535-1F16-4fd2-B633-F4F36F0B64E0}";

    public static IReadOnlyList<XlsxSparklineLayout> Read(
        XDocument worksheetXml,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        var extensionList = FindChildByLocalName(worksheetXml.Root, "extLst");
        if (extensionList is null)
            return [];

        var result = new List<XlsxSparklineLayout>();
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
            var seriesColor   = ReadColorElement(group, "colorSeries", theme, indexedColors);
            var negativeColor = ReadColorElement(group, "colorNegative", theme, indexedColors);
            var axisColor     = ReadColorElement(group, "colorAxis", theme, indexedColors);
            var markersColor  = ReadColorElement(group, "colorMarkers", theme, indexedColors);
            var firstColor    = ReadColorElement(group, "colorFirst", theme, indexedColors);
            var lastColor     = ReadColorElement(group, "colorLast", theme, indexedColors);
            var highColor     = ReadColorElement(group, "colorHigh", theme, indexedColors);
            var lowColor      = ReadColorElement(group, "colorLow", theme, indexedColors);

            // ── date axis ──────────────────────────────────────────────────────
            var (dateAxisRange, dateAxisSheetName) = ReadDateAxisRange(group, tempSheet);

            foreach (var sparkline in group.Descendants().Where(element =>
                         string.Equals(element.Name.LocalName, "sparkline", StringComparison.OrdinalIgnoreCase)))
            {
                var formula  = FindChildByLocalName(sparkline, "f")?.Value;
                var location = FindChildByLocalName(sparkline, "sqref")?.Value;
                if (string.IsNullOrWhiteSpace(formula) || string.IsNullOrWhiteSpace(location))
                    continue;

                // Preserve any cross-sheet qualifier (e.g. "Sheet2!") from the data formula rather than
                // discarding it: the bare range is parsed against a placeholder sheet here, and the
                // qualifier's sheet NAME is carried on the layout so the caller can resolve it to the
                // real SheetId once the workbook (and its sheet-name map) is assembled.
                var (dataSheetName, rangeText) = SplitSheetQualifiedFormula(formula);
                rangeText = rangeText.Replace("$", "", StringComparison.Ordinal);
                location  = location.Replace("$", "", StringComparison.Ordinal);
                try
                {
                    result.Add(new XlsxSparklineLayout(
                        new SparklineModel
                        {
                            // A sparkline's data-range formula legitimately collapses to a bare single-cell
                            // reference (no colon) whenever the source data is exactly one cell -- e.g. the
                            // user picks a single cell in the Sparkline "Edit Data" dialog, or later deletes
                            // columns until only one remains and Excel auto-shrinks the reference. Use the
                            // tolerant parser (already used for DateAxisRange below) so that shape isn't
                            // silently treated as malformed and the whole sparkline dropped.
                            DataRange        = GridRange.ParseCellOrRange(rangeText, tempSheet),
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
                        },
                        dataSheetName,
                        dateAxisSheetName));
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
            // A sparkline's Location (the cell it occupies) must be on this host sheet -- that's
            // what makes it "this sheet's" sparkline -- but its DataRange (data source) may
            // legitimately live on a DIFFERENT sheet (Excel's Sparkline "Edit Data" dialog allows
            // picking a cross-sheet source range). Requiring DataRange.Sheet == sheet.Id here used
            // to silently drop such cross-sheet sparklines entirely; instead only require that the
            // source sheet still exists in the workbook (ResolveSheetName resolves its name below).
            var validSparklines = sheet.Sparklines
                .Where(sparkline =>
                    sparkline.Location.Sheet == sheet.Id &&
                    Enum.IsDefined(sparkline.Kind) &&
                    ResolveSheetName(workbook, sheet, sparkline.DataRange.Start.Sheet) is not null)
                .ToList();

            // Build the new sparklineGroups element.
            // IO3: group by GroupId (preserves per-group settings). GroupId == 0 means the
            // sparkline was created independently in-app (never assigned a shared group id at
            // XLSX read time), so each such sparkline is its own singleton group keyed by its
            // unique model Id — grouping them by Kind instead would silently merge unrelated
            // same-kind sparklines (and their distinct colors/markers/axis settings) into one
            // shared x14:sparklineGroup, which is not what independently-inserted sparklines are.
            //
            // IO-sparkline-group-edit: the OOXML schema encodes type/markers/colors/axis-scaling
            // etc. as ONE set of attributes per <x14:sparklineGroup> (shared by every member's
            // <x14:sparkline>), but FreeX's editing command mutates a single loaded SparklineModel
            // instance in isolation, so members that started in the same nominal group (same
            // GroupId) can end up disagreeing on those "group-level" fields after an edit. Picking
            // one arbitrary member as the group's representative would then either silently drop
            // the edit (if a stale sibling was picked) or force it onto untouched siblings (if the
            // edited member was picked) — both data loss. Instead, re-split each nominal GroupId
            // bucket by whether its members still agree on every group-level field: members that
            // agree are written together as one shared <x14:sparklineGroup> (so a group-level style
            // edit applied uniformly to all members still round-trips as one group); members that
            // diverge are written as their own singleton <x14:sparklineGroup>, preserving each
            // member's own current settings without touching its siblings.
            var sparklineGroupsXml = new XElement(
                x14Ns + "sparklineGroups",
                validSparklines
                    .GroupBy(s => s.GroupId == 0 ? (object)s.Id : (object)s.GroupId)
                    .SelectMany(nominalGroup => nominalGroup.GroupBy(GroupStyleKeyOf))
                    .Select(styleGroup =>
                    {
                        var representative = styleGroup.First();
                        return ToSparklineGroupXml(workbook, sheet, representative, styleGroup, x14Ns, xmNs);
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

    /// <summary>
    /// Value-equality key over every OOXML "group-level" &lt;x14:sparklineGroup&gt; attribute
    /// (everything except each member's own <see cref="SparklineModel.DataRange"/> and
    /// <see cref="SparklineModel.Location"/>, which are always per-&lt;x14:sparkline&gt;). Two
    /// sparklines that share a nominal <see cref="SparklineModel.GroupId"/> but compare unequal
    /// under this key have diverged (e.g. one member was edited in isolation) and must be written
    /// as separate &lt;x14:sparklineGroup&gt; elements rather than merged under one, since the XLSX
    /// schema has no way to express per-member type/markers/color/axis differences within a
    /// single group.
    /// </summary>
    private readonly record struct GroupStyleKey(
        SparklineKind Kind,
        double? LineWeight,
        bool ShowMarkers,
        bool ShowHighPoint,
        bool ShowLowPoint,
        bool ShowFirstPoint,
        bool ShowLastPoint,
        bool ShowNegativePoints,
        bool ShowAxis,
        bool DisplayHidden,
        bool RightToLeft,
        SparklineAxisScaling MinAxisType,
        SparklineAxisScaling MaxAxisType,
        double? ManualMin,
        double? ManualMax,
        SparklineEmptyCellDisplay DisplayEmptyCellsAs,
        CellColor? SeriesColor,
        CellColor? NegativeColor,
        CellColor? AxisColor,
        CellColor? MarkersColor,
        CellColor? HighPointColor,
        CellColor? LowPointColor,
        CellColor? FirstPointColor,
        CellColor? LastPointColor,
        GridRange? DateAxisRange);

    private static GroupStyleKey GroupStyleKeyOf(SparklineModel s) => new(
        s.Kind,
        s.LineWeight,
        s.ShowMarkers,
        s.ShowHighPoint,
        s.ShowLowPoint,
        s.ShowFirstPoint,
        s.ShowLastPoint,
        s.ShowNegativePoints,
        s.ShowAxis,
        s.DisplayHidden,
        s.RightToLeft,
        s.MinAxisType,
        s.MaxAxisType,
        s.ManualMin,
        s.ManualMax,
        s.DisplayEmptyCellsAs,
        s.SeriesColor,
        s.NegativeColor,
        s.AxisColor,
        s.MarkersColor,
        s.HighPointColor,
        s.LowPointColor,
        s.FirstPointColor,
        s.LastPointColor,
        s.DateAxisRange);

    private static XElement ToSparklineGroupXml(
        Workbook workbook,
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

        // date axis (schema order: after the color elements, before the sparklines list).
        // Per CT_SparklineGroup there is no wrapper element -- the range is a bare <xm:f> that is a
        // direct child of the group, gated by the group's own dateAxis="1" boolean attribute.
        // The date-axis range may reference a different sheet than the host sheet, same as a
        // sparkline's data range; if that sheet no longer exists, omit it rather than write a
        // dangling/misattributed reference.
        if (representative.DateAxisRange is { } dateAxisRange &&
            ResolveSheetName(workbook, sheet, dateAxisRange.Start.Sheet) is { } dateAxisSheetName)
        {
            el.Add(new XAttribute("dateAxis", "1"));
            el.Add(new XElement(xmNs + "f", $"{SheetNameFormatter.QuoteIfNeeded(dateAxisSheetName)}!{dateAxisRange}"));
        }

        // sparklines list. Each sparkline's DataRange may live on a different sheet than the host
        // sheet (Excel allows a cross-sheet sparkline source range); resolve the range's OWN sheet
        // name rather than always qualifying with the host sheet's name, or the written formula
        // would silently point at the wrong sheet's data. The caller's validSparklines filter
        // already guarantees the source sheet still exists, so this always resolves.
        el.Add(new XElement(
            x14Ns + "sparklines",
            sparklines.Select(sparkline =>
            {
                var dataSheetName = ResolveSheetName(workbook, sheet, sparkline.DataRange.Start.Sheet)
                    ?? sheet.Name;
                return new XElement(
                    x14Ns + "sparkline",
                    new XElement(xmNs + "f",     $"{SheetNameFormatter.QuoteIfNeeded(dataSheetName)}!{sparkline.DataRange}"),
                    new XElement(xmNs + "sqref", sparkline.Location.ToA1()));
            })));

        return el;
    }

    /// <summary>
    /// Resolves the sheet name to use as the qualifying prefix for a sparkline data-range or
    /// date-axis formula (e.g. "Sheet2!$A$1:$E$1"). The referenced range may live on a different
    /// sheet than <paramref name="hostSheet"/> (Excel's Sparkline "Edit Data" dialog allows picking
    /// a cross-sheet source range), so this must resolve the range's ACTUAL sheet rather than
    /// always assuming the host sheet -- otherwise the written formula silently points at the
    /// wrong sheet's data. Returns null when the referenced sheet no longer exists in the workbook.
    /// </summary>
    private static string? ResolveSheetName(Workbook workbook, Sheet hostSheet, SheetId rangeSheet) =>
        rangeSheet == hostSheet.Id ? hostSheet.Name : workbook.GetSheet(rangeSheet)?.Name;

    private static void AddColorElement(XElement parent, XNamespace x14Ns, string localName, CellColor? color)
    {
        if (color is null)
            return;
        parent.Add(new XElement(x14Ns + localName,
            new XAttribute("rgb", $"FF{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}")));
    }

    /// <summary>
    /// Reads the group's optional date-axis range (Excel's sparkline "Date Axis Type" setting).
    /// Per CT_SparklineGroup there is no wrapper element -- the range is a bare &lt;xm:f&gt; that is a
    /// direct child of the group (after the color elements, before &lt;x14:sparklines&gt;), gated by
    /// the group's own <c>dateAxis="1"</c> boolean attribute. The formula may be sheet-qualified
    /// (e.g. "Sheet1!$A$1:$A$5") and — like the sparkline data range — may point at a DIFFERENT sheet
    /// than the host. The bare range is parsed against the placeholder <paramref name="sheet"/> and the
    /// qualifier's sheet NAME is returned alongside so the caller can resolve it to the real SheetId.
    /// Returns <c>(null, null)</c> when there is no date axis or the range is malformed.
    /// </summary>
    private static (GridRange? Range, string? SheetName) ReadDateAxisRange(XElement group, SheetId sheet)
    {
        var dateAxisAttr = group.Attribute("dateAxis")?.Value;
        var hasDateAxis = string.Equals(dateAxisAttr, "1", StringComparison.Ordinal) ||
            string.Equals(dateAxisAttr, "true", StringComparison.OrdinalIgnoreCase);
        if (!hasDateAxis)
            return (null, null);

        var formula = FindChildByLocalName(group, "f")?.Value;
        if (string.IsNullOrWhiteSpace(formula))
            return (null, null);

        var (sheetName, rangeText) = SplitSheetQualifiedFormula(formula);
        rangeText = rangeText.Replace("$", "", StringComparison.Ordinal);
        try
        {
            return (GridRange.ParseCellOrRange(rangeText, sheet), sheetName);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Splits a sparkline <c>&lt;xm:f&gt;</c> formula into its optional sheet-name qualifier and the bare
    /// range text: <c>Sheet2!$A$1:$E$1</c> → (<c>Sheet2</c>, <c>$A$1:$E$1</c>);
    /// <c>'My Data'!A1:B2</c> → (<c>My Data</c>, <c>A1:B2</c>); an unqualified <c>$A$1:$E$1</c> →
    /// (<see langword="null"/>, <c>$A$1:$E$1</c>). The sheet name is un-quoted per the OOXML rule
    /// (outer single quotes stripped, embedded <c>''</c> collapsed to <c>'</c>) so the caller can match
    /// it against the workbook's sheet names. Mirrors the same-purpose logic in
    /// <see cref="XlsxChartSeriesRangeReader.TryParseFormulaRange"/> so charts and sparklines resolve
    /// cross-sheet qualifiers identically.
    /// </summary>
    private static (string? SheetName, string RangeText) SplitSheetQualifiedFormula(string formula)
    {
        var bang = formula.LastIndexOf('!');
        if (bang < 0)
            return (null, formula);

        var sheetName = formula[..bang]
            .Trim()
            .Trim('\'')
            .Replace("''", "'", StringComparison.Ordinal);
        var rangeText = formula[(bang + 1)..];
        return (sheetName.Length == 0 ? null : sheetName, rangeText);
    }

    private static CellColor? ReadColorElement(
        XElement group,
        string localName,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        var el = group.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
        if (el is null)
            return null;

        // Resolves rgb, theme+tint, and indexed colors alike (Excel writes theme colors by default from
        // the Insert Sparklines dialog, e.g. <x14:colorSeries theme="4" tint="-0.4999"/>); without this the
        // color was silently dropped, rendering with FreeX's hardcoded default instead of the file's accent.
        return XlsxColorReader.TryReadCellColor(el, theme, indexedColors, out var color) ? color : null;
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
