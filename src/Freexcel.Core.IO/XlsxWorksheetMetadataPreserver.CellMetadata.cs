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

    private static bool MergeWorksheetCellAttributes(XElement? sourceSheetData, XElement targetRoot, XNamespace workbookNs)
    {
        var sourceCells = sourceSheetData?
            .Descendants(workbookNs + "c")
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Attribute("r")?.Value))
            .ToList();
        if (sourceCells is null || sourceCells.Count == 0)
            return false;

        return MergeWorksheetCellAttributes(
            sourceCells,
            BuildCellLookup(targetRoot.Element(workbookNs + "sheetData"), workbookNs),
            workbookNs);
    }

    private static bool MergeWorksheetCellAttributes(
        IReadOnlyList<XElement>? sourceCells,
        IReadOnlyDictionary<string, XElement> targetCellsByAddress,
        XNamespace workbookNs)
    {
        if (sourceCells is null || sourceCells.Count == 0 || targetCellsByAddress.Count == 0)
            return false;

        var changed = false;
        foreach (var sourceCell in sourceCells)
        {
            var address = sourceCell.Attribute("r")?.Value;
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
        XElement targetRoot,
        ZipArchive targetArchive,
        XNamespace workbookNs)
    {
        var sourceCells = sourceSheetData?
            .Descendants(workbookNs + "c")
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Attribute("r")?.Value))
            .ToList();
        if (sourceCells is null || sourceCells.Count == 0)
            return false;

        return MergeWorksheetInlineStringMetadata(
            sourceCells,
            BuildCellLookup(targetRoot.Element(workbookNs + "sheetData"), workbookNs),
            targetArchive,
            workbookNs);
    }

    private static bool MergeWorksheetInlineStringMetadata(
        IReadOnlyList<XElement>? sourceCells,
        IReadOnlyDictionary<string, XElement> targetCellsByAddress,
        ZipArchive targetArchive,
        XNamespace workbookNs)
    {
        if (sourceCells is null || sourceCells.Count == 0 || targetCellsByAddress.Count == 0)
            return false;

        var sourceInlineStrings = sourceCells
            .Where(cell =>
                string.Equals(cell.Attribute("t")?.Value, "inlineStr", StringComparison.OrdinalIgnoreCase) &&
                cell.Element(workbookNs + "is") is { } inlineString &&
                HasRichInlineStringMetadata(inlineString, workbookNs))
            .ToList();
        if (sourceInlineStrings.Count == 0)
            return false;

        var targetSharedStrings = LoadSharedStringPlainText(targetArchive, workbookNs);

        var changed = false;
        foreach (var sourceCell in sourceInlineStrings)
        {
            var address = sourceCell.Attribute("r")!.Value;
            if (!targetCellsByAddress.TryGetValue(address, out var targetCell) ||
                targetCell.Element(workbookNs + "f") is not null)
            {
                continue;
            }

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
        XElement targetRoot,
        XNamespace workbookNs)
    {
        var sourceCells = sourceSheetData?
            .Descendants(workbookNs + "c")
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Attribute("r")?.Value))
            .ToList();
        if (sourceCells is null || sourceCells.Count == 0)
            return false;

        return MergeWorksheetFormulaMetadata(
            sourceCells,
            BuildCellLookup(targetRoot.Element(workbookNs + "sheetData"), workbookNs),
            workbookNs);
    }

    private static bool MergeWorksheetFormulaMetadata(
        IReadOnlyList<XElement>? sourceCells,
        IReadOnlyDictionary<string, XElement> targetCellsByAddress,
        XNamespace workbookNs)
    {
        if (sourceCells is null || sourceCells.Count == 0 || targetCellsByAddress.Count == 0)
            return false;

        var sourceFormulaCells = sourceCells
            .Where(cell =>
                cell.Element(workbookNs + "f")?.HasAttributes == true)
            .ToList();
        if (sourceFormulaCells.Count == 0)
            return false;

        var changed = false;
        foreach (var sourceCell in sourceFormulaCells)
        {
            var address = sourceCell.Attribute("r")!.Value;
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
