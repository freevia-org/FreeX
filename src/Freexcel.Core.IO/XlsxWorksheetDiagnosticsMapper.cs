using System.Xml;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxWorksheetDiagnosticsMapper
{
    private const long MaxExpandedIgnoredErrorCells = 16384;
    private static readonly string[] SupportedIgnoredErrorFlags =
    [
        "numberStoredAsText",
        "evalError",
        "formula",
        "formulaRange",
        "unlockedFormula",
        "emptyCellReference",
        "listDataValidation",
        "calculatedColumn",
        "twoDigitTextYear"
    ];

    public static IgnoredErrorLayout ReadIgnoredErrors(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var cells = new List<CellAddress>();
        var existingCellOnlyRanges = new List<GridRange>();
        var tempSheet = SheetId.New();
        foreach (var ignoredError in worksheetXml.Root?
                     .Element(worksheetNs + "ignoredErrors")?
                     .Elements(worksheetNs + "ignoredError") ?? [])
        {
            if (!IsSupportedIgnoredErrorElement(ignoredError))
                continue;

            var sqref = ignoredError.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref))
                continue;

            foreach (var token in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!TryParseSqrefToken(token, tempSheet, out var range))
                    continue;

                if (range.CellCount > MaxExpandedIgnoredErrorCells)
                    existingCellOnlyRanges.Add(range);
                else
                    cells.AddRange(range.AllCells());
            }
        }

        return new IgnoredErrorLayout(cells, existingCellOnlyRanges);
    }

    public static WorksheetIgnoredErrorsMetadataModel? ReadIgnoredErrorsMetadata(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var ignoredErrors = worksheetXml.Root?.Element(worksheetNs + "ignoredErrors");
        if (ignoredErrors is null)
            return null;

        var model = new WorksheetIgnoredErrorsMetadataModel();
        foreach (var attribute in ignoredErrors.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        foreach (var ignoredError in ignoredErrors.Elements(worksheetNs + "ignoredError"))
        {
            var sqref = ignoredError.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref) || !IsSupportedIgnoredErrorElement(ignoredError))
                continue;

            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var attribute in ignoredError.Attributes())
            {
                if (attribute.IsNamespaceDeclaration ||
                    string.Equals(attribute.Name.LocalName, "sqref", StringComparison.Ordinal))
                {
                    continue;
                }

                attributes[attribute.Name.ToString()] = attribute.Value;
            }

            if (attributes.Count > 0)
                model.ErrorNativeAttributes[sqref] = attributes;
        }

        return model.NativeAttributes.Count == 0 && model.ErrorNativeAttributes.Count == 0
            ? null
            : model;
    }

    public static IReadOnlyList<CellAddress> ReadCellWatches(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var watchedCells = new List<CellAddress>();
        var seen = new HashSet<CellAddress>();
        var tempSheet = SheetId.New();
        foreach (var cellWatch in worksheetXml.Root?
                     .Element(worksheetNs + "cellWatches")?
                     .Elements(worksheetNs + "cellWatch") ?? [])
        {
            var reference = cellWatch.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(reference) ||
                !CellAddress.TryParse(reference, tempSheet, out var address) ||
                !seen.Add(address))
            {
                continue;
            }

            watchedCells.Add(address);
        }

        return watchedCells;
    }

    public static WorksheetCellWatchesMetadataModel? ReadCellWatchesMetadata(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var cellWatches = worksheetXml.Root?.Element(worksheetNs + "cellWatches");
        if (cellWatches is null)
            return null;

        var model = new WorksheetCellWatchesMetadataModel();
        foreach (var attribute in cellWatches.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        foreach (var cellWatch in cellWatches.Elements(worksheetNs + "cellWatch"))
        {
            var reference = cellWatch.Attribute("r")?.Value;
            if (!IsSupportedCellWatchReference(reference))
                continue;

            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var attribute in cellWatch.Attributes())
            {
                if (attribute.IsNamespaceDeclaration ||
                    string.Equals(attribute.Name.LocalName, "r", StringComparison.Ordinal))
                {
                    continue;
                }

                attributes[attribute.Name.ToString()] = attribute.Value;
            }

            if (attributes.Count > 0)
                model.WatchNativeAttributes[reference!] = attributes;
        }

        return model.NativeAttributes.Count == 0 && model.WatchNativeAttributes.Count == 0
            ? null
            : model;
    }

    private static bool TryParseSqrefToken(string token, SheetId sheet, out GridRange range)
    {
        range = default;
        var parts = token.Split(':');
        if (parts.Length == 1)
        {
            if (!CellAddress.TryParse(parts[0], sheet, out var address))
                return false;

            range = new GridRange(address, address);
            return true;
        }

        if (parts.Length == 2 &&
            CellAddress.TryParse(parts[0], sheet, out var start) &&
            CellAddress.TryParse(parts[1], sheet, out var end))
        {
            range = new GridRange(start, end);
            return true;
        }

        return false;
    }

    private static bool TryParseSqrefCells(string? sqref, SheetId sheet, out HashSet<CellAddress> cells)
    {
        cells = [];
        if (string.IsNullOrWhiteSpace(sqref))
            return false;

        foreach (var token in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseSqrefToken(token, sheet, out var range))
                return false;
            if (range.CellCount > MaxExpandedIgnoredErrorCells ||
                (long)cells.Count + range.CellCount > MaxExpandedIgnoredErrorCells)
            {
                return false;
            }

            foreach (var cell in range.AllCells())
                cells.Add(cell);
        }

        return cells.Count > 0;
    }

    private static bool IsSupportedIgnoredErrorElement(XElement ignoredError) =>
        SupportedIgnoredErrorFlags.Any(flag => IsTruthy(ignoredError.Attribute(flag)?.Value));

    private static bool TryGetIgnoredErrorNativeAttributes(
        WorksheetIgnoredErrorsMetadataModel? metadata,
        string reference,
        out Dictionary<string, string> attributes)
    {
        attributes = [];
        if (metadata is null)
            return false;

        if (metadata.ErrorNativeAttributes.TryGetValue(reference, out attributes!))
            return true;

        var tempSheet = SheetId.New();
        if (!CellAddress.TryParse(reference, tempSheet, out var address))
            return false;

        foreach (var pair in metadata.ErrorNativeAttributes)
        {
            foreach (var token in pair.Key.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (TryParseSqrefToken(token, tempSheet, out var range) && range.Contains(address))
                {
                    attributes = pair.Value;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsSupportedCellWatchReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        return CellAddress.TryParse(reference, SheetId.New(), out _);
    }

    private static bool MergeMissingAttributes(XElement sourceElement, XElement targetElement)
    {
        var changed = false;
        foreach (var attribute in sourceElement.Attributes())
        {
            if (targetElement.Attribute(attribute.Name) is not null)
                continue;

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool TrySetNativeAttribute(XElement element, string name, string value)
    {
        try
        {
            element.SetAttributeValue(XName.Get(name), value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool IsTruthy(string? value) =>
        value is "1" ||
        value is not null && bool.TryParse(value, out var parsed) && parsed;

    private static void InsertWorksheetMetadataElementInOrder(
        XElement worksheetRoot,
        XNamespace workbookNs,
        XElement metadataElement)
    {
        string[] laterWorksheetElements = metadataElement.Name.LocalName switch
        {
            "cellWatches" =>
            [
                "ignoredErrors",
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
            ],
            _ =>
            [
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
            ]
        };

        var insertionPoint = worksheetRoot.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == workbookNs &&
                laterWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
        if (insertionPoint is null)
            worksheetRoot.Add(metadataElement);
        else
            insertionPoint.AddBeforeSelf(metadataElement);
    }
}

internal sealed record IgnoredErrorLayout(
    IReadOnlyList<CellAddress> ExpandedCells,
    IReadOnlyList<GridRange> ExistingCellOnlyRanges);
