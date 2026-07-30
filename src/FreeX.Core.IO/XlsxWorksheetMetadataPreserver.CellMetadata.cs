using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxWorksheetMetadataPreserver
{
    // Worksheet hyperlink, column, row, cell, inline string, formula, and merge-cell metadata preservation.

    private static bool MergeWorksheetHyperlinkMetadata(
        XElement? sourceHyperlinks,
        XElement targetRoot,
        XNamespace workbookNs,
        XNamespace relNs,
        Sheet? sheet,
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sourceWorksheetPath,
        string targetWorksheetPath,
        XNamespace packageRelNs)
    {
        if (sourceHyperlinks is null)
            return false;

        var targetHyperlinks = targetRoot.Element(workbookNs + "hyperlinks");
        var changed = false;
        if (targetHyperlinks is not null)
            changed = MergeMissingAttributes(sourceHyperlinks, targetHyperlinks);

        var targetByReference = targetHyperlinks is null
            ? new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase)
            : targetHyperlinks
                .Elements(workbookNs + "hyperlink")
                .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("ref")?.Value))
                .ToDictionary(
                    element => element.Attribute("ref")!.Value,
                    StringComparer.OrdinalIgnoreCase);

        foreach (var sourceHyperlink in sourceHyperlinks.Elements(workbookNs + "hyperlink"))
        {
            var reference = sourceHyperlink.Attribute("ref")?.Value;
            if (string.IsNullOrWhiteSpace(reference))
                continue;

            if (!targetByReference.TryGetValue(reference, out var targetHyperlink))
            {
                // Whole-column/row and oversized bounded-range hyperlinks are stripped from the
                // ClosedXML-input copy before load (XlsxWorksheetHyperlinkNormalizer.StripRangeHyperlinkRefs)
                // so ClosedXML's regenerated worksheet never has a matching <hyperlink> element for this
                // ref on a full (non-patch) save -- there's nothing to merge attributes onto. Everything
                // else with no target match is a genuine edit (the cells were cleared or the hyperlink was
                // removed) and must NOT be resurrected, so only re-emit refs that match the same load-time
                // strip criteria the loader used to drop them in the first place. The relationship itself
                // (for an external target) is carried over into the target worksheet's own .rels by
                // XlsxPackageMetadataMerger.MergeRelationshipParts, which always preserves external
                // relationships -- but that merge dedups by Type+Target+TargetMode and, on an id collision
                // with one of ClosedXML's own regenerated relationships, renumbers the copied relationship
                // to a fresh id without rebinding this reemitted element (hyperlink is not among the
                // relationship types MergeRelationshipParts tracks for reference rebinding). Rebind the
                // reemitted r:id here the same way <picture>/<legacyDrawing> retained blocks are rebound.
                if (!IsStrippedRangeHyperlinkRef(reference))
                    continue;

                targetHyperlinks ??= CreateAndInsertWorksheetHyperlinksElement(targetRoot, workbookNs);
                var reemitted = new XElement(sourceHyperlink);
                RebindWorksheetElementRelationshipId(
                    reemitted,
                    sourceArchive,
                    targetArchive,
                    sourceWorksheetPath,
                    targetWorksheetPath,
                    relNs,
                    packageRelNs);
                targetHyperlinks.Add(reemitted);
                targetByReference[reference] = reemitted;
                changed = true;
                continue;
            }

            foreach (var attribute in sourceHyperlink.Attributes())
            {
                if (attribute.Name.LocalName == "ref" ||
                    attribute.Name == relNs + "id" ||
                    IsOfficeRevisionAttribute(attribute) ||
                    targetHyperlink.Attribute(attribute.Name) is not null)
                {
                    continue;
                }

                // R79-io-hyperlink-name-5-1: "location" and "tooltip" are the two hyperlink
                // attributes ClosedXML cannot always regenerate on a full save (it has no API to
                // carry a sub-address alongside an external r:id, and it omits "tooltip" entirely
                // when the current ScreenTip is blank) -- but they are also the two attributes the
                // MODEL tracks per-cell (Sheet.HyperlinkMetadata.Bookmark/ScreenTip). Copying them
                // verbatim from this pristine pre-edit source snapshot would silently resurrect a
                // stale value the user has since edited or cleared. Source verbatim only for
                // any other native-only attribute; for these two, defer to the live model instead.
                if (IsHyperlinkModelBackedAttribute(attribute))
                {
                    if (!TryGetCurrentHyperlinkAttributeValue(sheet, reference, attribute.Name.LocalName, out var currentValue))
                        continue;

                    targetHyperlink.SetAttributeValue(attribute.Name, currentValue);
                    changed = true;
                    continue;
                }

                targetHyperlink.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }
        }

        return changed;
    }

    private static bool IsHyperlinkModelBackedAttribute(XAttribute attribute) =>
        attribute.Name.Namespace == XNamespace.None &&
        (attribute.Name.LocalName == "location" || attribute.Name.LocalName == "tooltip");

    // Resolves the live model's current value for a hyperlink's "location" (HyperlinkMetadata.Bookmark)
    // or "tooltip" (HyperlinkMetadata.ScreenTip) at the cell the ref anchors -- a ranged hyperlink's ref
    // (e.g. "A1:B2") is keyed in the model by its top-left cell only, mirroring how XlsxFileAdapter's
    // loader keys Sheet.Hyperlinks/HyperlinkMetadata off the anchor cell. Returns false (skip: no
    // attribute is written) whenever the model has nothing to say -- no sheet, unparsable ref, no
    // tracked metadata for that cell, or the current value is blank -- so a user-cleared location/
    // tooltip stays cleared instead of being backfilled from stale source XML.
    private static bool TryGetCurrentHyperlinkAttributeValue(
        Sheet? sheet,
        string reference,
        string attributeLocalName,
        out string value)
    {
        value = string.Empty;
        if (sheet is null)
            return false;

        var anchorReference = reference.Contains(':', StringComparison.Ordinal)
            ? reference[..reference.IndexOf(':')]
            : reference;
        if (!CellAddress.TryParse(anchorReference, sheet.Id, out var address) ||
            !sheet.HyperlinkMetadata.TryGetValue(address, out var metadata))
        {
            return false;
        }

        value = attributeLocalName switch
        {
            "location" => metadata.Bookmark,
            "tooltip" => metadata.ScreenTip,
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    // Mirrors XlsxWorksheetHyperlinkNormalizer's load-time strip criteria (whole-column/row refs, and
    // bounded ranges above the cell-count cap that ClosedXML would otherwise materialize per-cell). Kept
    // as an independent, narrowly-scoped copy here rather than exposing the normalizer's private helpers,
    // so this file's ownership boundary stays self-contained.
    private const long MaxBoundedHyperlinkRangeCellCount = 100_000;

    private static bool IsStrippedRangeHyperlinkRef(string reference)
    {
        var trimmed = reference.Trim();
        if (trimmed.Length == 0 || trimmed.Contains(' ', StringComparison.Ordinal))
            return false;

        var parts = trimmed.Split(':');
        if (parts.Length != 2)
            return false;

        if (IsWholeColumnOrRowHyperlinkRef(parts[0], parts[1]))
            return true;

        var sheet = SheetId.New();
        if (!CellAddress.TryParse(parts[0], sheet, out var start) ||
            !CellAddress.TryParse(parts[1], sheet, out var end))
        {
            return false;
        }

        return new GridRange(start, end).CellCount > MaxBoundedHyperlinkRangeCellCount;
    }

    private static bool IsWholeColumnOrRowHyperlinkRef(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
            return false;

        if (left.All(char.IsAsciiLetter) && right.All(char.IsAsciiLetter))
            return true;

        return left.All(char.IsAsciiDigit) && right.All(char.IsAsciiDigit);
    }

    // Elements that must follow <hyperlinks> per the CT_Worksheet schema sequence. Used only to find a
    // correct insertion point when re-emitting a fully-stripped range hyperlink onto a worksheet whose
    // regenerated body has no <hyperlinks> element at all left to merge onto.
    private static readonly string[] WorksheetElementsAfterHyperlinks =
    [
        "printOptions",
        "pageMargins",
        "pageSetup",
        "headerFooter",
        "rowBreaks",
        "colBreaks",
        "customProperties",
        "cellWatches",
        "ignoredErrors",
        "singleXmlCells",
        "smartTags",
        "drawing",
        "legacyDrawing",
        "legacyDrawingHF",
        "picture",
        "oleObjects",
        "controls",
        "webPublishItems",
        "tableParts",
        "extLst"
    ];

    private static XElement CreateAndInsertWorksheetHyperlinksElement(XElement targetRoot, XNamespace workbookNs)
    {
        var hyperlinks = new XElement(workbookNs + "hyperlinks");

        XElement? insertionPoint = null;
        foreach (var element in targetRoot.Elements())
        {
            if (element.Name.Namespace == workbookNs &&
                WorksheetElementsAfterHyperlinks.Contains(element.Name.LocalName, StringComparer.Ordinal))
            {
                insertionPoint = element;
                break;
            }
        }

        if (insertionPoint is null)
            targetRoot.Add(hyperlinks);
        else
            insertionPoint.AddBeforeSelf(hyperlinks);

        return hyperlinks;
    }

    internal static bool MergeWorksheetColumnAttributes(XElement? sourceColumns, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceColumns is null)
            return false;

        var targetColumns = targetRoot.Element(workbookNs + "cols");
        if (targetColumns is null)
            return false;

        var changed = MergeMissingAttributes(sourceColumns, targetColumns);

        var targetColumnsByRange = targetColumns
            .Elements(workbookNs + "col")
            .Where(column => !string.IsNullOrWhiteSpace(column.Attribute("min")?.Value) &&
                             !string.IsNullOrWhiteSpace(column.Attribute("max")?.Value))
            .ToDictionary(ColumnRangeKey, StringComparer.OrdinalIgnoreCase);

        foreach (var sourceColumn in sourceColumns.Elements(workbookNs + "col"))
        {
            var key = ColumnRangeKey(sourceColumn);
            if (string.IsNullOrWhiteSpace(key) ||
                !targetColumnsByRange.TryGetValue(key, out var targetColumn))
            {
                continue;
            }

            foreach (var attribute in sourceColumn.Attributes())
            {
                if (IsOfficeRevisionAttribute(attribute) ||
                    IsOutlineStateAttribute(attribute) ||
                    IsStylesheetIndexColumnAttribute(attribute) ||
                    targetColumn.Attribute(attribute.Name) is not null)
                {
                    continue;
                }

                targetColumn.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }
        }

        return changed;

        static string ColumnRangeKey(XElement column)
        {
            var min = column.Attribute("min")?.Value;
            var max = column.Attribute("max")?.Value;
            return string.IsNullOrWhiteSpace(min) || string.IsNullOrWhiteSpace(max)
                ? string.Empty
                : $"{min}:{max}";
        }
    }

    internal static bool MergeWorksheetRowAttributes(XElement? sourceSheetData, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetData is null)
            return false;

        var targetSheetData = targetRoot.Element(workbookNs + "sheetData");
        if (targetSheetData is null)
            return false;

        var changed = MergeMissingAttributes(sourceSheetData, targetSheetData);

        var targetRowsByNumber = targetSheetData
            .Elements(workbookNs + "row")
            .Where(row => !string.IsNullOrWhiteSpace(row.Attribute("r")?.Value))
            .ToDictionary(
                row => row.Attribute("r")!.Value,
                StringComparer.OrdinalIgnoreCase);

        foreach (var sourceRow in sourceSheetData.Elements(workbookNs + "row"))
        {
            var rowNumber = sourceRow.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(rowNumber) ||
                !targetRowsByNumber.TryGetValue(rowNumber, out var targetRow))
            {
                continue;
            }

            foreach (var attribute in sourceRow.Attributes())
            {
                if (IsOfficeRevisionAttribute(attribute) ||
                    IsStylesheetIndexRowAttribute(attribute) ||
                    IsOutlineStateAttribute(attribute) ||
                    targetRow.Attribute(attribute.Name) is not null)
                {
                    continue;
                }

                targetRow.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }

            if (XlsxNativeXmlMerger.MergeExtensionList(sourceRow.Element(workbookNs + "extLst"), targetRow, workbookNs))
                changed = true;

            if (MergeMissingNativeChildren(
                    sourceRow,
                    targetRow,
                    child => child.Name != workbookNs + "c" && child.Name != workbookNs + "extLst"))
            {
                changed = true;
            }
        }

        return changed;
    }

    private static bool IsOutlineStateAttribute(XAttribute attribute) =>
        attribute.Name.Namespace == XNamespace.None &&
        attribute.Name.LocalName is "hidden" or "outlineLevel" or "collapsed";

    private static bool MergeWorksheetCellAttributes(
        XElement? sourceSheetData,
        Func<IReadOnlyDictionary<string, XElement>> getTargetCellsByAddress,
        XNamespace workbookNs)
    {
        if (sourceSheetData is null)
            return false;

        var changed = false;
        IReadOnlyDictionary<string, XElement>? targetCellsByAddress = null;
        foreach (var sourceCell in sourceSheetData
                     .Descendants(workbookNs + "c")
                     .Where(cell => HasCellAddress(cell) && HasPreservableCellNativeMetadata(cell, workbookNs)))
        {
            var address = sourceCell.Attribute("r")?.Value;
            targetCellsByAddress ??= getTargetCellsByAddress();
            if (targetCellsByAddress.Count == 0)
                return false;

            if (!targetCellsByAddress.TryGetValue(address!, out var targetCell))
            {
                continue;
            }

            foreach (var attribute in sourceCell.Attributes())
            {
                if (IsOfficeRevisionAttribute(attribute) ||
                    targetCell.Attribute(attribute.Name) is not null)
                {
                    continue;
                }

                targetCell.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }

            if (XlsxNativeXmlMerger.MergeExtensionList(sourceCell.Element(workbookNs + "extLst"), targetCell, workbookNs))
                changed = true;

            if (MergeMissingNativeChildren(
                    sourceCell,
                    targetCell,
                    child =>
                        child.Name != workbookNs + "f" &&
                        child.Name != workbookNs + "v" &&
                        child.Name != workbookNs + "is" &&
                        child.Name != workbookNs + "extLst"))
            {
                changed = true;
            }
        }

        return changed;
    }

    private static bool MergeWorksheetInlineStringMetadata(
        XElement? sourceSheetData,
        Func<IReadOnlyDictionary<string, XElement>> getTargetCellsByAddress,
        ZipArchive targetArchive,
        XNamespace workbookNs)
    {
        if (sourceSheetData is null)
            return false;

        var changed = false;
        IReadOnlyDictionary<string, XElement>? targetCellsByAddress = null;
        IReadOnlyList<string>? targetSharedStrings = null;
        foreach (var sourceCell in sourceSheetData
                     .Descendants(workbookNs + "c")
                     .Where(cell =>
                         HasCellAddress(cell) &&
                         string.Equals(cell.Attribute("t")?.Value, "inlineStr", StringComparison.OrdinalIgnoreCase) &&
                         cell.Element(workbookNs + "is") is { } inlineString &&
                         HasRichInlineStringMetadata(inlineString, workbookNs)))
        {
            var address = sourceCell.Attribute("r")!.Value;
            targetCellsByAddress ??= getTargetCellsByAddress();
            if (targetCellsByAddress.Count == 0)
                return false;

            if (!targetCellsByAddress.TryGetValue(address, out var targetCell) ||
                targetCell.Element(workbookNs + "f") is not null)
            {
                continue;
            }

            targetSharedStrings ??= LoadSharedStringPlainText(targetArchive, workbookNs);
            var sourceInlineString = sourceCell.Element(workbookNs + "is")!;
            var sourcePlainText = ReadInlineStringPlainText(sourceInlineString, workbookNs);
            if (string.IsNullOrEmpty(sourcePlainText) ||
                !string.Equals(sourcePlainText, ReadCellPlainText(targetCell, targetSharedStrings, workbookNs), StringComparison.Ordinal))
            {
                continue;
            }

            targetCell.SetAttributeValue("t", "inlineStr");
            targetCell.Elements(workbookNs + "v").Remove();
            targetCell.Elements(workbookNs + "is").Remove();
            var replacement = new XElement(sourceInlineString);
            SanitizeRichInlineStringFontNames(replacement, workbookNs);
            targetCell.Add(replacement);
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetFormulaMetadata(
        XElement? sourceSheetData,
        Func<IReadOnlyDictionary<string, XElement>> getTargetCellsByAddress,
        XNamespace workbookNs)
    {
        if (sourceSheetData is null)
            return false;

        var changed = false;
        IReadOnlyDictionary<string, XElement>? targetCellsByAddress = null;
        foreach (var sourceCell in sourceSheetData
                     .Descendants(workbookNs + "c")
                     .Where(cell => HasCellAddress(cell) && cell.Element(workbookNs + "f")?.HasAttributes == true))
        {
            var address = sourceCell.Attribute("r")!.Value;
            targetCellsByAddress ??= getTargetCellsByAddress();
            if (targetCellsByAddress.Count == 0)
                return false;

            if (!targetCellsByAddress.TryGetValue(address, out var targetCell))
                continue;

            var sourceFormula = sourceCell.Element(workbookNs + "f");
            var targetFormula = targetCell.Element(workbookNs + "f");
            if (sourceFormula is null ||
                targetFormula is null ||
                !string.Equals(
                    NormalizeFormulaXmlText(sourceFormula.Value),
                    NormalizeFormulaXmlText(targetFormula.Value),
                    StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var attribute in sourceFormula.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    continue;

                if (string.Equals(targetFormula.Attribute(attribute.Name)?.Value, attribute.Value, StringComparison.Ordinal))
                    continue;

                targetFormula.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }
        }

        return changed;
    }

    private static bool MergeWorksheetCellNativeMetadata(
        XElement? sourceSheetData,
        Func<IReadOnlyDictionary<string, XElement>> getTargetCellsByAddress,
        ZipArchive targetArchive,
        XNamespace workbookNs)
    {
        if (sourceSheetData is null)
            return false;

        var changed = false;
        IReadOnlyDictionary<string, XElement>? targetCellsByAddress = null;
        IReadOnlyList<string>? targetSharedStrings = null;

        // R49-io-cell-metadata-richdata-3-2: a row/column insert or delete performed since the
        // source snapshot was captured shifts affected cells to a new address in the freshly
        // regenerated target sheet -- unlike model-tracked per-cell state (e.g. Sheet.Comments),
        // which RowColumnShiftHelpers relocates in-place when the edit itself is applied, this
        // native-only metadata lives purely in the pristine source XML snapshot, which is never
        // remapped for structural edits. Cells whose address no longer exists in the target sheet
        // at all are queued for a shift-aware fallback pass below instead of being dropped, so
        // native-only metadata (vm/cm rich-value bindings, extLst, etc.) survives the shift.
        List<(XElement SourceCell, SourceCellNativeMetadata NativeMetadata, string OldAddress)>? unmatchedSourceCells = null;
        HashSet<string>? sourceAddresses = null;

        // R82-io-cell-rich-metadata-5-1: a row/column DELETE only shrinks the sheet -- it never frees
        // up a brand-new address the way an INSERT does -- so a middle-row delete leaves every
        // surviving shifted-up cell's OLD address still valid in the target sheet, just now holding a
        // DIFFERENT cell's content. CellValueMatchesCapturedNativeMetadata's t/formula/value equality
        // guard cannot catch this when the shifted-in cell happens to serialize identically to the one
        // that used to live there -- which is exactly what happens for a column of rich-value
        // placeholder cells (Stocks/Geography/IMAGE(), all t="e"/<v>#VALUE!</v> regardless of which
        // distinct entity their vm/cm points to). Count how many native-metadata-bearing source cells
        // share each (type, formula, value) signature; when more than one does, a same-address hit is
        // ambiguous and must not be trusted unless the target sheet has the exact same number of cells
        // sharing that signature (computed lazily below, only once an ambiguous signature is seen).
        Dictionary<CellSignature, int>? sourceRichValueSignatureCounts = null;
        Dictionary<CellSignature, int>? targetSignatureCounts = null;

        foreach (var sourceCell in sourceSheetData.Descendants(workbookNs + "c"))
        {
            var address = sourceCell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(address))
                continue;

            (sourceAddresses ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(address);

            var nativeMetadata = GetSourceCellNativeMetadata(sourceCell, workbookNs);
            if (!nativeMetadata.HasAny)
                continue;

            targetCellsByAddress ??= getTargetCellsByAddress();
            if (targetCellsByAddress.Count == 0)
                return changed;

            if (!targetCellsByAddress.TryGetValue(address, out var targetCell))
            {
                (unmatchedSourceCells ??= []).Add((sourceCell, nativeMetadata, address));
                continue;
            }

            sourceRichValueSignatureCounts ??= BuildRichValueSignatureCounts(sourceSheetData, workbookNs);
            if (sourceRichValueSignatureCounts.Count > 0)
                targetSignatureCounts ??= BuildCellSignatureCounts(targetCellsByAddress, workbookNs);

            if (MergeCellNativeMetadataPair(
                    sourceCell,
                    targetCell,
                    nativeMetadata,
                    targetArchive,
                    workbookNs,
                    ref targetSharedStrings,
                    sourceRichValueSignatureCounts,
                    targetSignatureCounts))
            {
                changed = true;
            }
        }

        if (unmatchedSourceCells is { Count: > 0 } && targetCellsByAddress is { Count: > 0 })
        {
            if (MergeShiftedCellNativeMetadata(
                    unmatchedSourceCells,
                    sourceAddresses!,
                    targetCellsByAddress,
                    targetArchive,
                    workbookNs,
                    ref targetSharedStrings))
            {
                changed = true;
            }
        }

        return changed;
    }

    // Counts native-metadata source cells that carry a vm/cm rich-value attribute, grouped by their
    // (type, formula, value) signature -- the same signature CellValueMatchesCapturedNativeMetadata
    // already compares. A count greater than 1 means the direct-address match below cannot assume
    // address stability: multiple source cells serialize identically, so a delete-shift could have
    // moved a different one of them into this exact address.
    private static Dictionary<CellSignature, int> BuildRichValueSignatureCounts(XElement sourceSheetData, XNamespace workbookNs)
    {
        var counts = new Dictionary<CellSignature, int>();
        foreach (var cell in sourceSheetData.Descendants(workbookNs + "c"))
        {
            if (!cell.Attributes().Any(IsRichValueMetadataAttribute))
                continue;

            var signature = GetCellSignature(cell, workbookNs);
            counts[signature] = counts.TryGetValue(signature, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    // Counts every target cell by (type, formula, value) signature, regardless of whether it carries
    // any native metadata -- used to check whether an ambiguous source signature's cell count is still
    // the same in the target sheet (no delete/insert disturbed that group) before trusting a
    // same-address rich-value reattachment.
    private static Dictionary<CellSignature, int> BuildCellSignatureCounts(
        IReadOnlyDictionary<string, XElement> targetCellsByAddress,
        XNamespace workbookNs)
    {
        var counts = new Dictionary<CellSignature, int>();
        foreach (var cell in targetCellsByAddress.Values)
        {
            var signature = GetCellSignature(cell, workbookNs);
            counts[signature] = counts.TryGetValue(signature, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    // Shift-aware fallback: pairs each still-unmatched source cell to an unclaimed target cell at
    // an address that never existed in the source snapshot (a strong signal it is the recipient
    // of a shift), matching by (type, formula, value) signature -- the same equality check already
    // used to guard reattaching vm/cm at a directly-matched address. When multiple source cells and
    // multiple candidates share an identical signature, disambiguate by relative order (old address
    // for the source side, new address for the candidate side) so each cell's own identity survives
    // the shift instead of depending on dictionary/enumeration order.
    private static bool MergeShiftedCellNativeMetadata(
        List<(XElement SourceCell, SourceCellNativeMetadata NativeMetadata, string OldAddress)> unmatchedSourceCells,
        HashSet<string> sourceAddresses,
        IReadOnlyDictionary<string, XElement> targetCellsByAddress,
        ZipArchive targetArchive,
        XNamespace workbookNs,
        ref IReadOnlyList<string>? targetSharedStrings)
    {
        var dummySheet = SheetId.New();
        var candidatesBySignature = new Dictionary<CellSignature, List<(CellAddress Address, XElement Cell)>>();
        foreach (var (targetAddress, targetCell) in targetCellsByAddress)
        {
            if (sourceAddresses.Contains(targetAddress) ||
                !CellAddress.TryParse(targetAddress, dummySheet, out var parsedAddress))
            {
                continue;
            }

            var signature = GetCellSignature(targetCell, workbookNs);
            if (!candidatesBySignature.TryGetValue(signature, out var list))
                candidatesBySignature[signature] = list = [];

            list.Add((parsedAddress, targetCell));
        }

        if (candidatesBySignature.Count == 0)
            return false;

        var changed = false;
        var sourceEntries = unmatchedSourceCells
            .Select(entry => (
                entry.SourceCell,
                entry.NativeMetadata,
                Signature: GetCellSignature(entry.SourceCell, workbookNs),
                OldAddress: CellAddress.TryParse(entry.OldAddress, dummySheet, out var parsed) ? parsed : (CellAddress?)null))
            .Where(entry => entry.OldAddress is not null)
            .ToList();

        foreach (var group in sourceEntries.GroupBy(entry => entry.Signature))
        {
            if (!candidatesBySignature.TryGetValue(group.Key, out var candidates) || candidates.Count == 0)
                continue;

            var sortedSources = group.OrderBy(entry => entry.OldAddress!.Value).ToList();
            var sortedCandidates = candidates.OrderBy(candidate => candidate.Address).ToList();

            var pairCount = Math.Min(sortedSources.Count, sortedCandidates.Count);
            for (var i = 0; i < pairCount; i++)
            {
                // This pairing is already order-disambiguated among unclaimed cells (never a direct-
                // address hit), so the ambiguous-signature-count guard below is unnecessary here --
                // pass null to leave CellValueMatchesCapturedNativeMetadata's ordinary equality check
                // as the only gate, exactly as before this fix.
                if (MergeCellNativeMetadataPair(
                        sortedSources[i].SourceCell,
                        sortedCandidates[i].Cell,
                        sortedSources[i].NativeMetadata,
                        targetArchive,
                        workbookNs,
                        ref targetSharedStrings,
                        sourceRichValueSignatureCounts: null,
                        targetSignatureCounts: null))
                {
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static bool MergeCellNativeMetadataPair(
        XElement sourceCell,
        XElement targetCell,
        SourceCellNativeMetadata nativeMetadata,
        ZipArchive targetArchive,
        XNamespace workbookNs,
        ref IReadOnlyList<string>? targetSharedStrings,
        IReadOnlyDictionary<CellSignature, int>? sourceRichValueSignatureCounts = null,
        IReadOnlyDictionary<CellSignature, int>? targetSignatureCounts = null)
    {
        var changed = false;

        if (nativeMetadata.HasCellMetadata)
        {
            foreach (var attribute in sourceCell.Attributes())
            {
                if (IsOfficeRevisionAttribute(attribute) ||
                    targetCell.Attribute(attribute.Name) is not null)
                {
                    continue;
                }

                // Rich-value cell metadata (vm/cm index into <valueMetadata>/<cellMetadata> in the
                // rich-value metadata part) is only valid for the exact t/formula/<v> it was captured
                // against. A full-rewrite save regenerates the target cell from the current model, so
                // if the cell's type, formula, or value changed since the source snapshot was taken,
                // reattaching the stale vm/cm would point the edited cell at metadata describing its
                // old value. Drop vm/cm on any mismatch; every other native attribute is unaffected.
                if (IsRichValueMetadataAttribute(attribute) &&
                    !CellValueMatchesCapturedNativeMetadata(
                        sourceCell, targetCell, workbookNs, sourceRichValueSignatureCounts, targetSignatureCounts))
                {
                    continue;
                }

                targetCell.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }

            if (XlsxNativeXmlMerger.MergeExtensionList(sourceCell.Element(workbookNs + "extLst"), targetCell, workbookNs))
                changed = true;

            if (MergeMissingNativeChildren(
                    sourceCell,
                    targetCell,
                    child =>
                        child.Name != workbookNs + "f" &&
                        child.Name != workbookNs + "v" &&
                        child.Name != workbookNs + "is" &&
                        child.Name != workbookNs + "extLst"))
            {
                changed = true;
            }
        }

        if (nativeMetadata.InlineString is not null && targetCell.Element(workbookNs + "f") is null)
        {
            targetSharedStrings ??= LoadSharedStringPlainText(targetArchive, workbookNs);
            var sourcePlainText = ReadInlineStringPlainText(nativeMetadata.InlineString, workbookNs);
            if (!string.IsNullOrEmpty(sourcePlainText) &&
                string.Equals(sourcePlainText, ReadCellPlainText(targetCell, targetSharedStrings, workbookNs), StringComparison.Ordinal))
            {
                targetCell.SetAttributeValue("t", "inlineStr");
                targetCell.Elements(workbookNs + "v").Remove();
                targetCell.Elements(workbookNs + "is").Remove();
                var replacement = new XElement(nativeMetadata.InlineString);
                SanitizeRichInlineStringFontNames(replacement, workbookNs);
                targetCell.Add(replacement);
                changed = true;
            }
        }

        if (nativeMetadata.Formula is not null)
        {
            var targetFormula = targetCell.Element(workbookNs + "f");
            if (targetFormula is null ||
                !string.Equals(
                    NormalizeFormulaXmlText(nativeMetadata.Formula.Value),
                    NormalizeFormulaXmlText(targetFormula.Value),
                    StringComparison.Ordinal))
            {
                return changed;
            }

            foreach (var attribute in nativeMetadata.Formula.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    continue;

                if (string.Equals(targetFormula.Attribute(attribute.Name)?.Value, attribute.Value, StringComparison.Ordinal))
                    continue;

                targetFormula.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }
        }

        return changed;
    }

    private readonly record struct CellSignature(string? Type, string? Formula, string? Value);

    private static CellSignature GetCellSignature(XElement cell, XNamespace workbookNs)
    {
        var formula = cell.Element(workbookNs + "f")?.Value;
        return new CellSignature(
            cell.Attribute("t")?.Value,
            formula is null ? null : NormalizeFormulaXmlText(formula),
            cell.Element(workbookNs + "v")?.Value);
    }

    private static bool IsRichValueMetadataAttribute(XAttribute attribute) =>
        attribute.Name.NamespaceName.Length == 0 &&
        (attribute.Name.LocalName == "vm" || attribute.Name.LocalName == "cm");

    // True when sourceCell's t/formula/<v> -- the cell state the source's vm/cm metadata was captured
    // against -- still match targetCell's. Guards against reattaching stale rich-value metadata (vm/cm)
    // to a cell whose value, type, or formula changed since the source snapshot was taken.
    //
    // R82-io-cell-rich-metadata-5-1: sourceRichValueSignatureCounts/targetSignatureCounts (both null
    // outside the direct-address main loop -- see MergeWorksheetCellNativeMetadata) additionally guard
    // against a false match caused by a row/column DELETE. Rich-value placeholder cells all serialize
    // identically (t="e", no formula, <v>#VALUE!</v>) regardless of which distinct entity their vm/cm
    // points to, so when several such cells share this exact signature, a same-address hit alone cannot
    // prove the target cell is really the SAME cell rather than a same-signature sibling shifted up into
    // this address by a delete. Only trust the match when the target sheet has exactly as many cells
    // sharing that signature as the source did -- a mismatch means a delete (or insert) disturbed this
    // group, and reattaching a guess would risk cross-binding the wrong rich-value entity, so the
    // metadata is safely left off instead.
    private static bool CellValueMatchesCapturedNativeMetadata(
        XElement sourceCell,
        XElement targetCell,
        XNamespace workbookNs,
        IReadOnlyDictionary<CellSignature, int>? sourceRichValueSignatureCounts = null,
        IReadOnlyDictionary<CellSignature, int>? targetSignatureCounts = null)
    {
        if (!string.Equals(sourceCell.Attribute("t")?.Value, targetCell.Attribute("t")?.Value, StringComparison.Ordinal))
            return false;

        var sourceFormula = sourceCell.Element(workbookNs + "f");
        var targetFormula = targetCell.Element(workbookNs + "f");
        if ((sourceFormula is null) != (targetFormula is null))
            return false;

        if (sourceFormula is not null && targetFormula is not null &&
            !string.Equals(
                NormalizeFormulaXmlText(sourceFormula.Value),
                NormalizeFormulaXmlText(targetFormula.Value),
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(
                sourceCell.Element(workbookNs + "v")?.Value,
                targetCell.Element(workbookNs + "v")?.Value,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (sourceRichValueSignatureCounts is not null && targetSignatureCounts is not null)
        {
            var signature = GetCellSignature(sourceCell, workbookNs);
            if (sourceRichValueSignatureCounts.TryGetValue(signature, out var sourceCount) && sourceCount > 1)
            {
                targetSignatureCounts.TryGetValue(signature, out var targetCount);
                if (targetCount != sourceCount)
                    return false;
            }
        }

        return true;
    }

    private static SourceCellNativeMetadata GetSourceCellNativeMetadata(XElement sourceCell, XNamespace workbookNs)
    {
        var hasCellMetadata = false;
        XElement? sourceFormula = null;
        XElement? sourceInlineString = null;
        foreach (var attribute in sourceCell.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration && !IsModeledCellAttribute(attribute))
                hasCellMetadata = true;
        }

        foreach (var child in sourceCell.Elements())
        {
            if (child.Name == workbookNs + "f")
            {
                if (child.HasAttributes)
                    sourceFormula = child;
                continue;
            }

            if (child.Name == workbookNs + "is")
            {
                if (string.Equals(sourceCell.Attribute("t")?.Value, "inlineStr", StringComparison.OrdinalIgnoreCase) &&
                    HasRichInlineStringMetadata(child, workbookNs))
                {
                    sourceInlineString = child;
                }

                continue;
            }

            if (child.Name != workbookNs + "v" &&
                child.Name != workbookNs + "extLst")
            {
                hasCellMetadata = true;
            }
        }

        if (sourceCell.Element(workbookNs + "extLst") is not null)
            hasCellMetadata = true;

        return new SourceCellNativeMetadata(hasCellMetadata, sourceFormula, sourceInlineString);
    }

    private readonly record struct SourceCellNativeMetadata(
        bool HasCellMetadata,
        XElement? Formula,
        XElement? InlineString)
    {
        public bool HasAny => HasCellMetadata || Formula is not null || InlineString is not null;
    }

    private static bool HasCellAddress(XElement cell) =>
        !string.IsNullOrWhiteSpace(cell.Attribute("r")?.Value);

    private static bool HasPreservableCellNativeMetadata(XElement cell, XNamespace workbookNs) =>
        cell.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration && !IsModeledCellAttribute(attribute)) ||
        cell.Element(workbookNs + "extLst") is not null ||
        cell.Elements().Any(child =>
            child.Name != workbookNs + "f" &&
            child.Name != workbookNs + "v" &&
            child.Name != workbookNs + "is" &&
            child.Name != workbookNs + "extLst");

    private static bool IsModeledCellAttribute(XAttribute attribute) =>
        attribute.Name.NamespaceName.Length == 0 &&
        (attribute.Name.LocalName == "r" ||
         attribute.Name.LocalName == "s" ||
         attribute.Name.LocalName == "t");

    private static Dictionary<string, XElement> BuildCellLookup(XElement? sheetData, XNamespace workbookNs)
    {
        if (sheetData is null)
            return new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);

        return sheetData
            .Descendants(workbookNs + "c")
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Attribute("r")?.Value))
            .ToDictionary(
                cell => cell.Attribute("r")!.Value,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeFormulaXmlText(string? formula)
    {
        return (formula ?? string.Empty).Trim().TrimStart('=');
    }

    private static bool MergeWorksheetMergedCellMetadata(
        XElement? sourceMergeCells,
        XElement targetRoot,
        XNamespace workbookNs)
    {
        if (sourceMergeCells is null)
            return false;

        var targetMergeCells = targetRoot.Element(workbookNs + "mergeCells");
        if (targetMergeCells is null)
            return false;

        var changed = false;
        foreach (var attribute in sourceMergeCells.Attributes().Where(attribute =>
                     IsNativeOnlyWorksheetAttribute(attribute, ModeledMergeCellsAttributes)))
        {
            if (string.Equals(targetMergeCells.Attribute(attribute.Name)?.Value, attribute.Value, StringComparison.Ordinal))
                continue;

            targetMergeCells.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var targetMergeCellsByRef = targetMergeCells
            .Elements(workbookNs + "mergeCell")
            .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("ref")?.Value))
            .ToDictionary(
                element => element.Attribute("ref")!.Value,
                StringComparer.OrdinalIgnoreCase);

        foreach (var sourceMergeCell in sourceMergeCells.Elements(workbookNs + "mergeCell"))
        {
            var reference = sourceMergeCell.Attribute("ref")?.Value;
            if (string.IsNullOrWhiteSpace(reference) ||
                !targetMergeCellsByRef.TryGetValue(reference, out var targetMergeCell))
            {
                continue;
            }

            foreach (var attribute in sourceMergeCell.Attributes().Where(attribute =>
                         IsNativeOnlyWorksheetAttribute(attribute, ModeledMergeCellAttributes)))
            {
                if (string.Equals(targetMergeCell.Attribute(attribute.Name)?.Value, attribute.Value, StringComparison.Ordinal))
                    continue;

                targetMergeCell.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }
        }

        return changed;
    }

    private static IReadOnlyList<string> LoadSharedStringPlainText(ZipArchive archive, XNamespace workbookNs)
    {
        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry is null)
            return [];

        var sharedStringsXml = XlsxPackageXmlEditor.LoadXml(sharedStringsEntry);
        return sharedStringsXml.Root?
            .Elements(workbookNs + "si")
            .Select(sharedString => ReadInlineStringPlainText(sharedString, workbookNs))
            .ToList() ?? [];
    }

    private static string ReadCellPlainText(
        XElement cell,
        IReadOnlyList<string> sharedStrings,
        XNamespace workbookNs)
    {
        var type = cell.Attribute("t")?.Value;
        if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase) &&
            cell.Element(workbookNs + "is") is { } inlineString)
        {
            return ReadInlineStringPlainText(inlineString, workbookNs);
        }

        if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(cell.Element(workbookNs + "v")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
            index >= 0 &&
            index < sharedStrings.Count)
        {
            return sharedStrings[index];
        }

        return cell.Element(workbookNs + "v")?.Value ?? string.Empty;
    }

    private static bool HasRichInlineStringMetadata(XElement inlineString, XNamespace workbookNs) =>
        inlineString.Elements(workbookNs + "r").Any() ||
        inlineString.Element(workbookNs + "rPh") is not null ||
        inlineString.Element(workbookNs + "phoneticPr") is not null;

    private static void SanitizeRichInlineStringFontNames(XElement inlineString, XNamespace workbookNs)
    {
        foreach (var richTextFont in inlineString.Descendants(workbookNs + "rFont"))
            XlsxFontNameSanitizer.SanitizeValAttribute(richTextFont);
    }

    private static string ReadInlineStringPlainText(XElement inlineString, XNamespace workbookNs)
    {
        var runs = inlineString.Elements(workbookNs + "r").ToList();
        if (runs.Count > 0)
            return string.Concat(runs.Select(run => run.Element(workbookNs + "t")?.Value ?? string.Empty));

        return inlineString.Element(workbookNs + "t")?.Value ?? string.Empty;
    }
}
