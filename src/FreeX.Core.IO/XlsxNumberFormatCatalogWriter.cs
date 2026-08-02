using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxNumberFormatCatalogWriter
{
    public static PivotNumberFormatIdMap Save(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var stylesEntry = archive.GetEntry("xl/styles.xml") ?? archive.CreateEntry("xl/styles.xml");
        var stylesXml = XlsxPackageXmlEditor.LoadXml(stylesEntry);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = stylesXml.Root;
        if (root is null)
            return PivotNumberFormatIdMap.Empty;

        var (catalog, pivotFormatCodesById) = BuildNumberFormatCatalog(workbook);
        if (catalog.Count == 0)
            return PivotNumberFormatIdMap.Empty;

        var numFmts = root.Element(workbookNs + "numFmts");
        if (numFmts is null)
        {
            numFmts = new XElement(workbookNs + "numFmts");
            var firstFormatPeer = FindFirstFormatPeer(root, workbookNs);
            if (firstFormatPeer is null)
                root.AddFirst(numFmts);
            else
                firstFormatPeer.AddBeforeSelf(numFmts);
        }

        var remap = new Dictionary<int, int>();
        var usedIds = numFmts.Elements(workbookNs + "numFmt")
            .Select(element => XlsxXmlAttributeReader.ReadIntAttribute(element, "numFmtId"))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();
        var nextId = Math.Max(164, usedIds.Count == 0 ? 164 : usedIds.Max() + 1);
        foreach (var (numberFormatId, formatCode) in catalog.OrderBy(pair => pair.Key))
        {
            var existing = FindNumberFormatById(numFmts, workbookNs, numberFormatId);
            if (existing is not null &&
                string.Equals(existing.Attribute("formatCode")?.Value, formatCode, StringComparison.Ordinal))
            {
                remap[numberFormatId] = numberFormatId;
                continue;
            }

            if (existing is not null)
            {
                var equivalent = FindEquivalentNumberFormat(numFmts, workbookNs, formatCode);
                if (equivalent is not null && XlsxXmlAttributeReader.ReadIntAttribute(equivalent, "numFmtId") is { } equivalentId)
                {
                    remap[numberFormatId] = equivalentId;
                    continue;
                }

                while (usedIds.Contains(nextId))
                    nextId++;
                remap[numberFormatId] = nextId;
                usedIds.Add(nextId);
                numFmts.Add(new XElement(
                    workbookNs + "numFmt",
                    new XAttribute("numFmtId", nextId.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("formatCode", formatCode)));
                nextId++;
                continue;
            }

            // No numFmt currently occupies this catalog id (e.g. ClosedXML's rebuild assigned the
            // same formatCode a different id). Before minting a brand-new entry, check whether an
            // equivalent formatCode already exists under another (already-referenced) id — otherwise
            // every round trip where the live id drifts adds one more orphaned duplicate.
            var equivalentForNewId = FindEquivalentNumberFormat(numFmts, workbookNs, formatCode);
            if (equivalentForNewId is not null && XlsxXmlAttributeReader.ReadIntAttribute(equivalentForNewId, "numFmtId") is { } equivalentIdForNewId)
            {
                remap[numberFormatId] = equivalentIdForNewId;
                continue;
            }

            remap[numberFormatId] = numberFormatId;
            usedIds.Add(numberFormatId);
            numFmts.Add(new XElement(
                workbookNs + "numFmt",
                new XAttribute("numFmtId", numberFormatId.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("formatCode", formatCode)));
        }

        // R118-io-numfmt-pivot-sentinel-collision: PivotValueFieldPlanner hardcodes the SAME sentinel
        // numFmtId (164) for every distinct custom format string a user types into Value Field Settings,
        // so `catalog` above (a plain id -> single-code dictionary) can only ever carry ONE of two
        // colliding pivot data fields' format codes -- the loop just above already resolved that single
        // surviving code's final id. Any OTHER distinct format code that pivot data fields declared under
        // the same sentinel id never got a numFmt entry (or a remap target) at all; give each of them
        // their own entry/id here, keyed by (sentinelId, formatCode) so XlsxPivotTableWriter and
        // XlsxFileAdapter.SavePostProcessing can resolve each dataField by its OWN format-code text
        // instead of purely by the shared sentinel id.
        var pivotCodeRemap = new Dictionary<(int NumberFormatId, string FormatCode), int>();
        foreach (var (numberFormatId, formatCodes) in pivotFormatCodesById)
        {
            if (remap.TryGetValue(numberFormatId, out var primaryFinalId))
                pivotCodeRemap[(numberFormatId, formatCodes[0])] = primaryFinalId;

            for (var index = 1; index < formatCodes.Count; index++)
            {
                var formatCode = formatCodes[index];

                var equivalent = FindEquivalentNumberFormat(numFmts, workbookNs, formatCode);
                if (equivalent is not null && XlsxXmlAttributeReader.ReadIntAttribute(equivalent, "numFmtId") is { } equivalentId)
                {
                    pivotCodeRemap[(numberFormatId, formatCode)] = equivalentId;
                    continue;
                }

                while (usedIds.Contains(nextId))
                    nextId++;
                pivotCodeRemap[(numberFormatId, formatCode)] = nextId;
                usedIds.Add(nextId);
                numFmts.Add(new XElement(
                    workbookNs + "numFmt",
                    new XAttribute("numFmtId", nextId.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("formatCode", formatCode)));
                nextId++;
            }
        }

        numFmts.SetAttributeValue("count", numFmts.Elements(workbookNs + "numFmt").Count().ToString(CultureInfo.InvariantCulture));
        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
        return new PivotNumberFormatIdMap(remap, pivotCodeRemap);
    }

    public static void RemapPivotTableNumberFormats(
        Stream xlsxStream,
        IReadOnlyDictionary<int, int> numberFormatIdMap)
    {
        var effectiveMap = numberFormatIdMap
            .Where(pair => pair.Key != pair.Value)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        if (effectiveMap.Count == 0)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        foreach (var pivotEntry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var pivotXml = XlsxPackageXmlEditor.LoadXml(pivotEntry);
            var changed = false;
            foreach (var dataField in pivotXml.Descendants().Where(element => element.Name.LocalName == "dataField"))
            {
                if (XlsxXmlAttributeReader.ReadIntAttribute(dataField, "numFmtId") is not { } numberFormatId ||
                    !effectiveMap.TryGetValue(numberFormatId, out var mappedId))
                {
                    continue;
                }

                dataField.SetAttributeValue("numFmtId", mappedId.ToString(CultureInfo.InvariantCulture));
                changed = true;
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, pivotEntry.FullName, pivotXml);
        }
    }

    private static (Dictionary<int, string> Catalog, Dictionary<int, List<string>> PivotFormatCodesById) BuildNumberFormatCatalog(Workbook workbook)
    {
        // workbook.NumberFormatCatalog carries every custom numFmt that was present in the
        // ORIGINAL file (loaded wholesale by XlsxWorkbookMetadataReader.LoadNumberFormatCatalog),
        // including ones no longer referenced by any live cell style once the in-app edit session
        // cleared or reassigned a cell's format. Re-emitting the raw dictionary verbatim on every
        // save would resurrect those dead entries as orphaned numFmts forever. Only carry an entry
        // through when its format code is still referenced by a live cell/style-only style or a
        // conditional-format dxf style (R69-io-numfmt-styles-6-1).
        var candidates = new Dictionary<int, string>();
        foreach (var (numberFormatId, formatCode) in workbook.NumberFormatCatalog)
        {
            if (numberFormatId >= 164 && !string.IsNullOrWhiteSpace(formatCode))
                candidates[numberFormatId] = formatCode;
        }

        var catalog = new Dictionary<int, string>();
        if (candidates.Count > 0)
        {
            var liveFormatCodes = CollectLiveNumberFormatCodes(workbook);
            foreach (var (numberFormatId, formatCode) in candidates)
            {
                if (liveFormatCodes.Contains(formatCode))
                    catalog[numberFormatId] = formatCode;
            }
        }

        // R118-io-numfmt-pivot-sentinel-collision: PivotValueFieldPlanner.ResolveNumberFormatState
        // hardcodes numFmtId 164 for EVERY distinct custom format string typed into Value Field Settings,
        // so two data fields with different custom formats legitimately share one NumberFormatId here.
        // `catalog` (a plain id -> single-code dictionary) can only hold one code per id -- tracking every
        // DISTINCT code seen per id separately (in insertion order) lets Save() give each one its own
        // final numFmtId instead of silently letting the last-processed field's code overwrite the rest.
        var pivotFormatCodesById = new Dictionary<int, List<string>>();
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var pivot in sheet.PivotTables)
            {
                foreach (var field in pivot.DataFields)
                {
                    if (field.NumberFormatId is >= 164 and var numberFormatId &&
                        !string.IsNullOrWhiteSpace(field.NumberFormatCode))
                    {
                        if (!pivotFormatCodesById.TryGetValue(numberFormatId, out var formatCodes))
                        {
                            formatCodes = [];
                            pivotFormatCodesById[numberFormatId] = formatCodes;
                        }

                        if (!formatCodes.Contains(field.NumberFormatCode, StringComparer.Ordinal))
                            formatCodes.Add(field.NumberFormatCode);

                        // Referenced directly by a live pivot data field -- always live, no liveness
                        // check needed (unlike the workbook-wide catalog above). Keep the FIRST distinct
                        // code seen at this id as the catalog's representative entry so the main Save()
                        // loop's existing single-id resolution (exact match / equivalent / reallocate)
                        // stays exactly as before for the common (non-colliding) case; any additional
                        // distinct codes are resolved separately from pivotFormatCodesById below.
                        catalog[numberFormatId] = formatCodes[0];
                    }
                }
            }
        }

        return (catalog, pivotFormatCodesById);
    }

    /// <summary>
    /// Collects every custom number-format CODE (not id) still referenced by a live cell style,
    /// style-only run, or conditional-format ("dxf") style anywhere in the workbook. Used to prune
    /// <see cref="Workbook.NumberFormatCatalog"/> down to formats that are still actually in use.
    /// </summary>
    private static HashSet<string> CollectLiveNumberFormatCodes(Workbook workbook)
    {
        var liveStyleIds = new HashSet<StyleId>();
        var liveFormatCodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var (_, cell) in sheet.EnumerateCells())
                liveStyleIds.Add(cell.StyleId);

            foreach (var (_, styleId) in sheet.GetStyleOnlyEntries())
                liveStyleIds.Add(styleId);

            // Conditional-format "dxf" styles carry their own CellStyle instance directly (not a
            // pooled StyleId), so their format code is read straight off the rule.
            foreach (var conditionalFormat in sheet.ConditionalFormats)
            {
                if (conditionalFormat.FormatIfTrue?.NumberFormat is { } numberFormat)
                    liveFormatCodes.Add(numberFormat);
            }
        }

        foreach (var styleId in liveStyleIds)
            liveFormatCodes.Add(workbook.GetStyle(styleId).NumberFormat);

        return liveFormatCodes;
    }

    private static XElement? FindFirstFormatPeer(XElement root, XNamespace workbookNs)
    {
        foreach (var element in root.Elements())
        {
            if (element.Name == workbookNs + "fonts" ||
                element.Name == workbookNs + "fills" ||
                element.Name == workbookNs + "borders" ||
                element.Name == workbookNs + "cellStyleXfs" ||
                element.Name == workbookNs + "cellXfs")
            {
                return element;
            }
        }

        return null;
    }

    private static XElement? FindNumberFormatById(XElement numFmts, XNamespace workbookNs, int numberFormatId)
    {
        foreach (var element in numFmts.Elements(workbookNs + "numFmt"))
        {
            if (XlsxXmlAttributeReader.ReadIntAttribute(element, "numFmtId") == numberFormatId)
                return element;
        }

        return null;
    }

    private static XElement? FindEquivalentNumberFormat(XElement numFmts, XNamespace workbookNs, string formatCode)
    {
        foreach (var element in numFmts.Elements(workbookNs + "numFmt"))
        {
            if (string.Equals(element.Attribute("formatCode")?.Value, formatCode, StringComparison.Ordinal) &&
                XlsxXmlAttributeReader.ReadIntAttribute(element, "numFmtId") is { } equivalentId &&
                equivalentId >= 164)
            {
                return element;
            }
        }

        return null;
    }
}
