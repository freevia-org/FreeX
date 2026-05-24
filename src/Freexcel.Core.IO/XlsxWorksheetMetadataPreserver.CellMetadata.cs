using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static partial class XlsxWorksheetMetadataPreserver
{
    // Worksheet hyperlink, column, row, cell, inline string, formula, and merge-cell metadata preservation.

    private static bool MergeWorksheetHyperlinkMetadata(
        XElement? sourceHyperlinks,
        XElement targetRoot,
        XNamespace workbookNs,
        XNamespace relNs)
    {
        if (sourceHyperlinks is null)
            return false;

        var targetHyperlinks = targetRoot.Element(workbookNs + "hyperlinks");
        if (targetHyperlinks is null)
            return false;

        var changed = MergeMissingAttributes(sourceHyperlinks, targetHyperlinks);

        var targetByReference = targetHyperlinks
            .Elements(workbookNs + "hyperlink")
            .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("ref")?.Value))
            .ToDictionary(
                element => element.Attribute("ref")!.Value,
                StringComparer.OrdinalIgnoreCase);

        foreach (var sourceHyperlink in sourceHyperlinks.Elements(workbookNs + "hyperlink"))
        {
            var reference = sourceHyperlink.Attribute("ref")?.Value;
            if (string.IsNullOrWhiteSpace(reference) ||
                !targetByReference.TryGetValue(reference, out var targetHyperlink))
            {
                continue;
            }

            foreach (var attribute in sourceHyperlink.Attributes())
            {
                if (attribute.Name.LocalName == "ref" ||
                    attribute.Name == relNs + "id" ||
                    targetHyperlink.Attribute(attribute.Name) is not null)
                {
                    continue;
                }

                targetHyperlink.SetAttributeValue(attribute.Name, attribute.Value);
                changed = true;
            }
        }

        return changed;
    }

    private static bool MergeWorksheetColumnAttributes(XElement? sourceColumns, XElement targetRoot, XNamespace workbookNs)
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
                if (targetColumn.Attribute(attribute.Name) is not null)
                    continue;

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

    private static bool MergeWorksheetRowAttributes(XElement? sourceSheetData, XElement targetRoot, XNamespace workbookNs)
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
                if (targetRow.Attribute(attribute.Name) is not null)
                    continue;

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
                if (targetCell.Attribute(attribute.Name) is not null)
                    continue;

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
            targetCell.Add(new XElement(sourceInlineString));
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
        foreach (var sourceCell in sourceSheetData.Descendants(workbookNs + "c"))
        {
            var address = sourceCell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(address))
                continue;

            var hasCellMetadata = HasPreservableCellNativeMetadata(sourceCell, workbookNs);
            var sourceFormula = sourceCell.Element(workbookNs + "f");
            var hasFormulaMetadata = sourceFormula?.HasAttributes == true;
            var sourceInlineString = string.Equals(sourceCell.Attribute("t")?.Value, "inlineStr", StringComparison.OrdinalIgnoreCase)
                ? sourceCell.Element(workbookNs + "is")
                : null;
            var hasInlineStringMetadata = sourceInlineString is not null &&
                                          HasRichInlineStringMetadata(sourceInlineString, workbookNs);
            if (!hasCellMetadata && !hasFormulaMetadata && !hasInlineStringMetadata)
                continue;

            targetCellsByAddress ??= getTargetCellsByAddress();
            if (targetCellsByAddress.Count == 0)
                return changed;

            if (!targetCellsByAddress.TryGetValue(address, out var targetCell))
                continue;

            if (hasCellMetadata)
            {
                foreach (var attribute in sourceCell.Attributes())
                {
                    if (targetCell.Attribute(attribute.Name) is not null)
                        continue;

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

            if (hasInlineStringMetadata && targetCell.Element(workbookNs + "f") is null)
            {
                targetSharedStrings ??= LoadSharedStringPlainText(targetArchive, workbookNs);
                var sourcePlainText = ReadInlineStringPlainText(sourceInlineString!, workbookNs);
                if (!string.IsNullOrEmpty(sourcePlainText) &&
                    string.Equals(sourcePlainText, ReadCellPlainText(targetCell, targetSharedStrings, workbookNs), StringComparison.Ordinal))
                {
                    targetCell.SetAttributeValue("t", "inlineStr");
                    targetCell.Elements(workbookNs + "v").Remove();
                    targetCell.Elements(workbookNs + "is").Remove();
                    targetCell.Add(new XElement(sourceInlineString!));
                    changed = true;
                }
            }

            if (hasFormulaMetadata)
            {
                var targetFormula = targetCell.Element(workbookNs + "f");
                if (targetFormula is null ||
                    !string.Equals(
                        NormalizeFormulaXmlText(sourceFormula!.Value),
                        NormalizeFormulaXmlText(targetFormula.Value),
                        StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var attribute in sourceFormula!.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration)
                        continue;

                    if (string.Equals(targetFormula.Attribute(attribute.Name)?.Value, attribute.Value, StringComparison.Ordinal))
                        continue;

                    targetFormula.SetAttributeValue(attribute.Name, attribute.Value);
                    changed = true;
                }
            }
        }

        return changed;
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

    private static string ReadInlineStringPlainText(XElement inlineString, XNamespace workbookNs)
    {
        var runs = inlineString.Elements(workbookNs + "r").ToList();
        if (runs.Count > 0)
            return string.Concat(runs.Select(run => run.Element(workbookNs + "t")?.Value ?? string.Empty));

        return inlineString.Element(workbookNs + "t")?.Value ?? string.Empty;
    }
}
