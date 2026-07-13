using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxSharedStringMetadataPreserver
{
    /// <summary>Identifies a worksheet cell by its package part name and cell reference (e.g. "A2").</summary>
    private readonly record struct CellSharedStringRef(string WorksheetEntryName, string Address);

    public static void PreserveRichTextAndPhonetics(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        var sourceEntry = sourceArchive.GetEntry("xl/sharedStrings.xml");
        var targetEntry = targetArchive.GetEntry("xl/sharedStrings.xml");
        if (sourceEntry is null || targetEntry is null)
            return;

        if (!ContainsRichSharedStringMetadata(sourceEntry))
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var targetXml = XlsxPackageXmlEditor.LoadXml(targetEntry);
        var sourceRoot = sourceXml.Root;
        var targetRoot = targetXml.Root;
        if (sourceRoot is null || targetRoot is null)
            return;

        // Shared strings are keyed by plain text because ClosedXML fully regenerates
        // sharedStrings.xml (dedup/reorder), so source and target indices don't align.
        // Plain text that is unique in a document is matched directly. Plain text that
        // collides (rich vs. plain, or two differently-formatted runs rendering the same
        // text) must NOT be silently dropped -- those are matched positionally instead
        // (first source occurrence with that text to the first target occurrence with
        // that text, second to second, etc.), since both documents enumerate shared
        // strings in the same first-use cell order.
        var sourceRichStrings = sourceRoot.Elements(workbookNs + "si")
            .Where(item => HasRichSharedStringMetadata(item, workbookNs))
            .ToList();
        if (sourceRichStrings.Count == 0)
            return;

        var sourceUniqueByText = GetUniqueSharedStringsByPlainText(sourceRichStrings, workbookNs, out var sourceDuplicateTexts);
        var targetElements = targetRoot.Elements(workbookNs + "si").ToList();
        var targetUniqueByText = GetUniqueSharedStringsByPlainText(targetElements, workbookNs, out var targetDuplicateTexts);

        // R40: text-only (or stale-index) matching can pair a rich/phonetic source entry with a
        // target shared-string entry that -- after ClosedXML's own plain-text dedup during a full
        // rebuild -- is now referenced by additional cell(s) that never had this rich content in
        // the source (e.g. a plain "<si><t>Tanaka</t></si>" cell and a phonetic "Tanaka" cell
        // collapse into one target <si> because neither the plain nor the modeled-rich text
        // differ). Overwriting that shared entry in place would silently graft the rich/phonetic
        // metadata onto the unrelated cell too. Build a cell-address-to-shared-index map for both
        // packages so every replacement can be verified against the actual cell(s) that reference
        // it before mutating a shared entry.
        var sourceCellSharedIndex = BuildCellSharedStringIndexMap(sourceArchive);
        var targetCellSharedIndex = BuildCellSharedStringIndexMap(targetArchive);
        var sourceCellsBySharedIndex = InvertCellSharedStringIndexMap(sourceCellSharedIndex);
        var targetCellsBySharedIndex = InvertCellSharedStringIndexMap(targetCellSharedIndex);

        var sourceSiIndexByElement = new Dictionary<XElement, int>();
        var sourceSiIndex = 0;
        foreach (var si in sourceRoot.Elements(workbookNs + "si"))
            sourceSiIndexByElement[si] = sourceSiIndex++;

        var targetSiIndexByElement = new Dictionary<XElement, int>();
        var targetSiIndex = 0;
        foreach (var si in targetRoot.Elements(workbookNs + "si"))
            targetSiIndexByElement[si] = targetSiIndex++;

        var nextNewTargetSiIndex = targetSiIndex;
        var pendingWorksheetEdits = new Dictionary<string, List<(string Address, int NewSharedIndex)>>(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var (plainText, sourceString) in sourceUniqueByText)
        {
            if (!targetUniqueByText.TryGetValue(plainText, out var targetString))
                continue;

            changed |= ApplyRichSharedStringPatch(
                sourceString,
                targetString,
                workbookNs,
                sourceSiIndexByElement,
                targetSiIndexByElement,
                sourceCellsBySharedIndex,
                targetCellsBySharedIndex,
                targetRoot,
                pendingWorksheetEdits,
                ref nextNewTargetSiIndex);
        }

        if (sourceDuplicateTexts is not null || targetDuplicateTexts is not null)
        {
            IEnumerable<string> textsToMatch = sourceDuplicateTexts is null
                ? targetDuplicateTexts!
                : targetDuplicateTexts is null
                    ? sourceDuplicateTexts
                    : sourceDuplicateTexts.Concat(targetDuplicateTexts);

            // R18: raw sharedStrings.xml document order is only a reliable proxy for "first-use
            // cell order" when the SOURCE file was written by a generator that appends entries in
            // first-use order (Excel, and ClosedXML's own regenerated target). A source built by a
            // different generator (e.g. one that sorts/dedups the SST independently of cell usage)
            // can have same-text rich duplicates in a different relative order than the cells that
            // actually use them, so pairing sourceMatches[i] <-> targetMatches[i] by raw document
            // order can swap formatting between two cells that happen to share the same text.
            // Reorder the source matches by their true first-use cell order (computed by scanning
            // the source worksheets) before pairing so each cell keeps its own formatting.
            var sourceFirstUseBySharedIndex = BuildFirstUseOrderBySharedStringIndex(sourceArchive);

            var handledTexts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var text in textsToMatch)
            {
                if (!handledTexts.Add(text))
                    continue;

                var sourceMatches = sourceRichStrings
                    .Where(item => ReadSharedStringPlainText(item, workbookNs) == text)
                    .OrderBy(item => sourceFirstUseBySharedIndex.TryGetValue(sourceSiIndexByElement[item], out var pos)
                        ? pos
                        : int.MaxValue)
                    .ToList();
                if (sourceMatches.Count == 0)
                    continue;

                // P70: pair rich source occurrences only against target occurrences that are
                // ALSO rich (same-text plain si entries in targetElements must never receive a
                // rich replacement positionally — a plain first-use si sharing the text with a
                // later rich si would otherwise get clobbered, silently promoting every plain
                // cell using that string to rich formatting/phonetics it never had).
                var targetMatches = targetElements
                    .Where(item => HasRichSharedStringMetadata(item, workbookNs) && ReadSharedStringPlainText(item, workbookNs) == text)
                    .ToList();

                // R27: the target regenerates its SST from FreeX's own rich-run MODEL, which can
                // fail to capture an rPr sub-element that distinguished two source occurrences
                // (e.g. <scheme>). If that collapses >1 distinct source entries into fewer target
                // entries (or vice versa), a count-mismatched positional pairing would graft one
                // cell's exact rich XML onto a target entry that is now SHARED by other cells,
                // silently overwriting their formatting instead of merely losing the unmodeled
                // detail. Only pair 1:1 when the counts actually line up unambiguously.
                if (sourceMatches.Count != targetMatches.Count)
                    continue;

                for (var i = 0; i < sourceMatches.Count; i++)
                {
                    changed |= ApplyRichSharedStringPatch(
                        sourceMatches[i],
                        targetMatches[i],
                        workbookNs,
                        sourceSiIndexByElement,
                        targetSiIndexByElement,
                        sourceCellsBySharedIndex,
                        targetCellsBySharedIndex,
                        targetRoot,
                        pendingWorksheetEdits,
                        ref nextNewTargetSiIndex);
                }
            }
        }

        if (pendingWorksheetEdits.Count > 0)
            ApplyPendingWorksheetSharedStringEdits(targetArchive, workbookNs, pendingWorksheetEdits);

        if (nextNewTargetSiIndex > targetSiIndex)
        {
            // R40: entries were appended to carry rich/phonetic content that could not be safely
            // grafted onto an existing (now multiply-referenced) shared-string entry. Keep the
            // uniqueCount attribute in sync with the actual <si> child count; the total reference
            // count ("count") is unchanged since we only redirected existing cell references.
            var uniqueCountAttribute = targetRoot.Attribute("uniqueCount");
            if (uniqueCountAttribute is not null &&
                int.TryParse(uniqueCountAttribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uniqueCount))
            {
                uniqueCountAttribute.Value = (uniqueCount + (nextNewTargetSiIndex - targetSiIndex))
                    .ToString(CultureInfo.InvariantCulture);
            }

            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, "xl/sharedStrings.xml", targetXml);
    }

    /// <summary>
    /// Attempts to patch <paramref name="targetString"/> with <paramref name="sourceString"/>'s
    /// rich/phonetic content. If the target shared-string entry is referenced only by cell(s) that
    /// the SOURCE also attributed to this exact entry, the replacement happens in place (matching
    /// prior behavior). Otherwise -- the target entry is now shared with cell(s) that never had
    /// this rich content in the source (a post-rebuild dedup collision, R40) -- a new shared-string
    /// entry is appended instead and only the correctly-attributed cell(s) are redirected to it, so
    /// the unrelated cell(s) keep referencing the original (unmodified) entry.
    /// </summary>
    private static bool ApplyRichSharedStringPatch(
        XElement sourceString,
        XElement targetString,
        XNamespace workbookNs,
        Dictionary<XElement, int> sourceSiIndexByElement,
        Dictionary<XElement, int> targetSiIndexByElement,
        Dictionary<int, List<CellSharedStringRef>> sourceCellsBySharedIndex,
        Dictionary<int, List<CellSharedStringRef>> targetCellsBySharedIndex,
        XElement targetRoot,
        Dictionary<string, List<(string Address, int NewSharedIndex)>> pendingWorksheetEdits,
        ref int nextNewTargetSiIndex)
    {
        var sourceCells = sourceSiIndexByElement.TryGetValue(sourceString, out var sourceIndex) &&
            sourceCellsBySharedIndex.TryGetValue(sourceIndex, out var matchedSourceCells)
                ? matchedSourceCells
                : null;
        var targetCells = targetSiIndexByElement.TryGetValue(targetString, out var targetIndex) &&
            targetCellsBySharedIndex.TryGetValue(targetIndex, out var matchedTargetCells)
                ? matchedTargetCells
                : null;

        // No cell-level attribution available for one side (e.g. a package with no worksheet
        // parts, or an orphaned/unreferenced shared string) -- nothing to cross-check, so fall
        // back to the original unconditional in-place replacement.
        if (sourceCells is null || sourceCells.Count == 0 || targetCells is null || targetCells.Count == 0)
        {
            ReplaceSharedString(targetString, sourceString, workbookNs);
            return true;
        }

        var sourceCellSet = new HashSet<CellSharedStringRef>(sourceCells);
        if (targetCells.All(sourceCellSet.Contains))
        {
            // Every cell referencing the target's shared-string index also referenced this exact
            // source entry -- overwriting in place cannot affect any unrelated cell.
            ReplaceSharedString(targetString, sourceString, workbookNs);
            return true;
        }

        // The target index is referenced by at least one cell that did NOT originate from this
        // source entry. Append a new shared-string entry with the rich/phonetic content and
        // redirect only the cell(s) that actually had it; leave the other cell(s) pointing at the
        // original (still-plain) target entry untouched.
        var cellsToRedirect = targetCells.Where(sourceCellSet.Contains).ToList();
        if (cellsToRedirect.Count == 0)
            return false;

        var replacement = new XElement(sourceString);
        SanitizeRichSharedStringFontNames(replacement, workbookNs);
        targetRoot.Add(replacement);
        var newSharedIndex = nextNewTargetSiIndex++;

        foreach (var cell in cellsToRedirect)
        {
            if (!pendingWorksheetEdits.TryGetValue(cell.WorksheetEntryName, out var edits))
            {
                edits = new List<(string, int)>();
                pendingWorksheetEdits[cell.WorksheetEntryName] = edits;
            }

            edits.Add((cell.Address, newSharedIndex));
        }

        return true;
    }

    private static void ReplaceSharedString(XElement targetString, XElement sourceString, XNamespace workbookNs)
    {
        var replacement = new XElement(sourceString);
        SanitizeRichSharedStringFontNames(replacement, workbookNs);
        targetString.ReplaceWith(replacement);
    }

    private static bool ContainsRichSharedStringMetadata(ZipArchiveEntry sharedStringsEntry)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };

        using var stream = sharedStringsEntry.Open();
        using var reader = XmlReader.Create(stream, settings);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element &&
                reader.NamespaceURI == "http://schemas.openxmlformats.org/spreadsheetml/2006/main" &&
                reader.LocalName is "r" or "rPh" or "phoneticPr")
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, XElement> GetUniqueSharedStringsByPlainText(
        IEnumerable<XElement> sharedStrings,
        XNamespace workbookNs,
        out HashSet<string>? duplicates)
    {
        var unique = new Dictionary<string, XElement>(StringComparer.Ordinal);
        duplicates = null;
        foreach (var element in sharedStrings)
        {
            var text = ReadSharedStringPlainText(element, workbookNs);
            if (string.IsNullOrEmpty(text) || duplicates?.Contains(text) == true)
                continue;

            if (unique.ContainsKey(text))
            {
                unique.Remove(text);
                duplicates ??= new HashSet<string>(StringComparer.Ordinal);
                duplicates.Add(text);
                continue;
            }

            unique.Add(text, element);
        }

        return unique;
    }

    private static bool HasRichSharedStringMetadata(XElement sharedString, XNamespace workbookNs) =>
        sharedString.Elements(workbookNs + "r").Any() ||
        sharedString.Element(workbookNs + "rPh") is not null ||
        sharedString.Element(workbookNs + "phoneticPr") is not null;

    private static void SanitizeRichSharedStringFontNames(XElement sharedString, XNamespace workbookNs)
    {
        foreach (var richTextFont in sharedString.Descendants(workbookNs + "rFont"))
            XlsxFontNameSanitizer.SanitizeValAttribute(richTextFont);
    }

    /// <summary>
    /// Scans every worksheet in <paramref name="archive"/> for <c>&lt;c t="s"&gt;&lt;v&gt;N&lt;/v&gt;&lt;/c&gt;</c>
    /// shared-string references and records, for each shared-string index, the ordinal position at
    /// which it is first referenced across the workbook (worksheet-entry-name order, then document
    /// order within each worksheet). Used to pair same-text rich shared-string duplicates by the
    /// cell that actually uses them instead of trusting the raw <c>sharedStrings.xml</c> array order,
    /// which a non-Excel generator need not have written in first-use order (R18).
    /// </summary>
    private static Dictionary<int, int> BuildFirstUseOrderBySharedStringIndex(ZipArchive archive)
    {
        var firstUse = new Dictionary<int, int>();
        var position = 0;

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };

        var worksheetEntries = archive.Entries
            .Where(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);

        foreach (var worksheetEntry in worksheetEntries)
        {
            using var stream = worksheetEntry.Open();
            using var reader = XmlReader.Create(stream, settings);
            var inSharedStringCell = false;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element ||
                    reader.NamespaceURI != "http://schemas.openxmlformats.org/spreadsheetml/2006/main")
                    continue;

                if (reader.LocalName == "c")
                {
                    inSharedStringCell = reader.GetAttribute("t") == "s";
                }
                else if (inSharedStringCell && reader.LocalName == "v")
                {
                    var text = reader.ReadElementContentAsString();
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex) &&
                        !firstUse.ContainsKey(sharedIndex))
                    {
                        firstUse[sharedIndex] = position++;
                    }

                    inSharedStringCell = false;
                }
            }
        }

        return firstUse;
    }

    /// <summary>
    /// Scans every worksheet in <paramref name="archive"/> for <c>&lt;c r="..." t="s"&gt;&lt;v&gt;N&lt;/v&gt;&lt;/c&gt;</c>
    /// shared-string references and records, for each referencing cell, which shared-string index
    /// it points at. Used (R40) to verify -- before patching rich/phonetic metadata back onto a
    /// shared-string entry -- exactly which cell(s) in each package actually reference it, so a
    /// post-rebuild dedup collision (an unrelated plain-text cell collapsed onto the same target
    /// entry) can be detected instead of blindly grafting the metadata onto every cell that shares
    /// the index.
    /// </summary>
    private static Dictionary<CellSharedStringRef, int> BuildCellSharedStringIndexMap(ZipArchive archive)
    {
        var map = new Dictionary<CellSharedStringRef, int>();

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };

        var worksheetEntries = archive.Entries
            .Where(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

        foreach (var worksheetEntry in worksheetEntries)
        {
            using var stream = worksheetEntry.Open();
            using var reader = XmlReader.Create(stream, settings);
            string? currentAddress = null;
            var inSharedStringCell = false;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element ||
                    reader.NamespaceURI != "http://schemas.openxmlformats.org/spreadsheetml/2006/main")
                    continue;

                if (reader.LocalName == "c")
                {
                    currentAddress = reader.GetAttribute("r");
                    inSharedStringCell = reader.GetAttribute("t") == "s";
                }
                else if (inSharedStringCell && reader.LocalName == "v")
                {
                    var text = reader.ReadElementContentAsString();
                    if (currentAddress is not null &&
                        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex))
                    {
                        map[new CellSharedStringRef(worksheetEntry.FullName, currentAddress)] = sharedIndex;
                    }

                    inSharedStringCell = false;
                }
            }
        }

        return map;
    }

    private static Dictionary<int, List<CellSharedStringRef>> InvertCellSharedStringIndexMap(
        Dictionary<CellSharedStringRef, int> cellToSharedIndex)
    {
        var result = new Dictionary<int, List<CellSharedStringRef>>();
        foreach (var (cell, sharedIndex) in cellToSharedIndex)
        {
            if (!result.TryGetValue(sharedIndex, out var cells))
            {
                cells = new List<CellSharedStringRef>();
                result[sharedIndex] = cells;
            }

            cells.Add(cell);
        }

        return result;
    }

    /// <summary>
    /// Applies cell-reference redirects (R40 split path: a new shared-string entry was appended
    /// because the original target index was shared with an unrelated cell) by rewriting the
    /// affected cell(s)' <c>&lt;v&gt;</c> shared-string index in their owning worksheet part.
    /// </summary>
    private static void ApplyPendingWorksheetSharedStringEdits(
        ZipArchive targetArchive,
        XNamespace workbookNs,
        Dictionary<string, List<(string Address, int NewSharedIndex)>> pendingWorksheetEdits)
    {
        foreach (var (worksheetEntryName, edits) in pendingWorksheetEdits)
        {
            var entry = targetArchive.GetEntry(worksheetEntryName);
            if (entry is null)
                continue;

            var document = XlsxPackageXmlEditor.LoadXml(entry);
            var root = document.Root;
            if (root is null)
                continue;

            var newSharedIndexByAddress = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (address, newSharedIndex) in edits)
                newSharedIndexByAddress[address] = newSharedIndex;

            var modified = false;
            foreach (var cell in root.Descendants(workbookNs + "c"))
            {
                var address = cell.Attribute("r")?.Value;
                if (address is null ||
                    !newSharedIndexByAddress.TryGetValue(address, out var newSharedIndex) ||
                    cell.Attribute("t")?.Value != "s")
                    continue;

                var valueElement = cell.Element(workbookNs + "v");
                if (valueElement is null)
                    continue;

                valueElement.Value = newSharedIndex.ToString(CultureInfo.InvariantCulture);
                modified = true;
            }

            if (modified)
                XlsxPackageXmlEditor.ReplaceXml(targetArchive, worksheetEntryName, document);
        }
    }

    private static string ReadSharedStringPlainText(XElement sharedString, XNamespace workbookNs)
    {
        var textName = workbookNs + "t";
        string? singleRunText = null;
        StringBuilder? builder = null;
        var hasRuns = false;
        foreach (var run in sharedString.Elements(workbookNs + "r"))
        {
            hasRuns = true;
            var text = run.Element(textName)?.Value ?? string.Empty;
            if (builder is not null)
            {
                builder.Append(text);
            }
            else if (singleRunText is null)
            {
                singleRunText = text;
            }
            else
            {
                builder = new StringBuilder(singleRunText.Length + text.Length);
                builder.Append(singleRunText);
                builder.Append(text);
            }
        }

        if (hasRuns)
            return builder?.ToString() ?? singleRunText ?? string.Empty;

        return sharedString.Element(textName)?.Value ?? string.Empty;
    }
}
