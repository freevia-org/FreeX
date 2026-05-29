using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxDataValidationNativeMetadataMapper
{
    public static IReadOnlyList<DataValidationNativeMetadata> Read(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var dataValidations = worksheetXml.Root?.Element(worksheetNs + "dataValidations");
        if (dataValidations is null)
            return [];

        var containerAttributes = ReadContainerAttributes(dataValidations);
        var containerChildXmls = ReadContainerChildXmls(dataValidations, worksheetNs);
        var tempSheet = SheetId.New();
        var result = new List<DataValidationNativeMetadata>();

        foreach (var validation in dataValidations.Elements(worksheetNs + "dataValidation"))
        {
            var sqref = validation.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref))
                continue;

            try
            {
                var ranges = ParseSqrefRanges(sqref, tempSheet);
                if (ranges.Count == 0)
                    continue;

                result.Add(new DataValidationNativeMetadata(
                    ranges[0],
                    ranges,
                    sqref,
                    ReadModeledAttributes(validation),
                    validation.Element(worksheetNs + "formula1")?.Value,
                    validation.Element(worksheetNs + "formula2")?.Value,
                    ReadAttributes(validation),
                    ReadChildXmls(validation, worksheetNs),
                    containerAttributes,
                    containerChildXmls));
            }
            catch
            {
                // Ignore native metadata for ranges FreeX cannot parse.
            }
        }

        return result;
    }

    public static void Apply(Sheet sheet, IReadOnlyList<DataValidationNativeMetadata> nativeMetadata)
    {
        if (nativeMetadata.Count == 0 || sheet.DataValidations.Count == 0)
            return;

        foreach (var validation in sheet.DataValidations)
        {
            var metadata = nativeMetadata.FirstOrDefault(item =>
                RangesEqual(item.AppliesTo, validation.AppliesTo));
            if (metadata is null)
                continue;

            validation.AdditionalRanges.Clear();
            validation.AdditionalRanges.AddRange(metadata.AppliesToRanges.Skip(1).Select(range =>
                new GridRange(
                    new CellAddress(sheet.Id, range.Start.Row, range.Start.Col),
                    new CellAddress(sheet.Id, range.End.Row, range.End.Col))));

            if (metadata.NativeAttributes.Count > 0)
                validation.NativeAttributes = metadata.NativeAttributes;
            if (metadata.NativeChildXmls.Count > 0)
                validation.NativeChildXmls = metadata.NativeChildXmls;
            if (metadata.NativeContainerAttributes.Count > 0)
                validation.NativeContainerAttributes = metadata.NativeContainerAttributes;
            if (metadata.NativeContainerChildXmls.Count > 0)
                validation.NativeContainerChildXmls = metadata.NativeContainerChildXmls;
        }

        RemoveDuplicateMultiAreaValidations(sheet, nativeMetadata);
    }

    public static bool HasNativeMetadata(DataValidation validation) =>
        (validation.NativeAttributes?.Count ?? 0) > 0 ||
        (validation.NativeChildXmls?.Count ?? 0) > 0 ||
        (validation.NativeContainerAttributes?.Count ?? 0) > 0 ||
        (validation.NativeContainerChildXmls?.Count ?? 0) > 0;

    public static void Save(Stream xlsxStream, Workbook workbook)
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

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                !relTargets.TryGetValue(relId, out var worksheetPath) ||
                !sheet.DataValidations.Any(HasNativeMetadata))
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

            var dataValidations = root.Element(workbookNs + "dataValidations");
            if (dataValidations is null)
                continue;

            var changed = false;
            var containerSource = sheet.DataValidations.FirstOrDefault(validation =>
                (validation.NativeContainerAttributes?.Count ?? 0) > 0 ||
                (validation.NativeContainerChildXmls?.Count ?? 0) > 0);
            if (containerSource is not null)
                changed |= ApplyContainerNativeMetadata(dataValidations, containerSource, workbookNs);

            var validationsByRange = dataValidations
                .Elements(workbookNs + "dataValidation")
                .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("sqref")?.Value))
                .ToDictionary(element => element.Attribute("sqref")!.Value, element => element, StringComparer.Ordinal);

            foreach (var validation in sheet.DataValidations.Where(HasNativeMetadata))
            {
                if (validationsByRange.TryGetValue(ToSqref(validation), out var validationElement) ||
                    validationsByRange.TryGetValue(validation.AppliesTo.ToString(), out validationElement))
                {
                    changed |= ApplyValidationNativeMetadata(validationElement, validation, workbookNs);
                }
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static Dictionary<string, string> ReadAttributes(XElement validation)
    {
        string[] modeledAttributes =
        [
            "type",
            "errorStyle",
            "operator",
            "allowBlank",
            "showDropDown",
            "showInputMessage",
            "showErrorMessage",
            "errorTitle",
            "error",
            "promptTitle",
            "prompt",
            "sqref"
        ];
        return validation.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && !modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, string> ReadModeledAttributes(XElement validation)
    {
        string[] modeledAttributes =
        [
            "type",
            "errorStyle",
            "operator",
            "allowBlank",
            "showDropDown",
            "showInputMessage",
            "showErrorMessage",
            "errorTitle",
            "error",
            "promptTitle",
            "prompt"
        ];
        return validation.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
    }

    private static List<GridRange> ParseSqrefRanges(string sqref, SheetId sheetId)
    {
        var ranges = new List<GridRange>();
        foreach (var reference in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = reference.Contains(':', StringComparison.Ordinal)
                ? GridRange.Parse(reference, sheetId)
                : new GridRange(CellAddress.Parse(reference, sheetId), CellAddress.Parse(reference, sheetId));
            ranges.Add(range);
        }

        return ranges;
    }

    private static string ToSqref(DataValidation validation) =>
        string.Join(' ', new[] { validation.AppliesTo }.Concat(validation.AdditionalRanges).Select(range => range.ToString()));

    private static void RemoveDuplicateMultiAreaValidations(
        Sheet sheet,
        IReadOnlyList<DataValidationNativeMetadata> nativeMetadata)
    {
        foreach (var metadata in nativeMetadata.Where(item => item.AppliesToRanges.Count > 1))
        {
            foreach (var duplicateRange in metadata.AppliesToRanges.Skip(1))
            {
                var duplicate = sheet.DataValidations.FirstOrDefault(validation =>
                    RangesEqual(validation.AppliesTo, duplicateRange) &&
                    validation.AdditionalRanges.Count == 0 &&
                    MatchesNativeValidation(validation, metadata));
                if (duplicate is not null)
                    sheet.DataValidations.Remove(duplicate);
            }
        }
    }

    private static bool MatchesNativeValidation(DataValidation validation, DataValidationNativeMetadata metadata) =>
        MatchesType(validation.Type, metadata.ModeledAttributes.GetValueOrDefault("type")) &&
        string.Equals(validation.Formula1 ?? "", metadata.Formula1 ?? "", StringComparison.Ordinal) &&
        string.Equals(validation.Formula2 ?? "", metadata.Formula2 ?? "", StringComparison.Ordinal);

    private static bool MatchesType(DvType type, string? nativeType) =>
        string.IsNullOrWhiteSpace(nativeType) ||
        string.Equals(nativeType, TypeToNativeName(type), StringComparison.OrdinalIgnoreCase);

    private static string TypeToNativeName(DvType type) => type switch
    {
        DvType.WholeNumber => "whole",
        DvType.TextLength => "textLength",
        _ => type.ToString()
    };

    private static bool RangesEqual(GridRange left, GridRange right) =>
        left.Start.Row == right.Start.Row &&
        left.Start.Col == right.Start.Col &&
        left.End.Row == right.End.Row &&
        left.End.Col == right.End.Col;

    private static List<string> ReadChildXmls(XElement validation, XNamespace worksheetNs)
    {
        XName[] modeledChildren = [worksheetNs + "formula1", worksheetNs + "formula2"];
        return validation.Elements()
            .Where(element => !modeledChildren.Contains(element.Name))
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();
    }

    private static Dictionary<string, string> ReadContainerAttributes(XElement dataValidations)
    {
        string[] modeledAttributes = ["count"];
        return dataValidations.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && !modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
    }

    private static List<string> ReadContainerChildXmls(XElement dataValidations, XNamespace worksheetNs) =>
        dataValidations.Elements()
            .Where(element => element.Name != worksheetNs + "dataValidation")
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();

    private static bool ApplyContainerNativeMetadata(
        XElement dataValidations,
        DataValidation source,
        XNamespace worksheetNs)
    {
        var changed = false;
        foreach (var (name, value) in source.NativeContainerAttributes ?? new Dictionary<string, string>())
        {
            changed |= TrySetNativeAttributeIfMissing(dataValidations, name, value);
        }

        foreach (var nativeChildXml in (source.NativeContainerChildXmls ?? []).Where(xml => !string.IsNullOrWhiteSpace(xml)))
        {
            try
            {
                var nativeChild = XElement.Parse(nativeChildXml);
                if (nativeChild.Name.Namespace == worksheetNs && nativeChild.Name.LocalName != "dataValidation")
                {
                    dataValidations.Add(nativeChild);
                    changed = true;
                }
            }
            catch
            {
                // Ignore malformed native data-validation container payloads from older saves.
            }
        }

        return changed;
    }

    private static bool ApplyValidationNativeMetadata(
        XElement validationElement,
        DataValidation source,
        XNamespace worksheetNs)
    {
        var changed = false;
        foreach (var (name, value) in source.NativeAttributes ?? new Dictionary<string, string>())
        {
            changed |= TrySetNativeAttributeIfMissing(validationElement, name, value);
        }

        foreach (var nativeChildXml in (source.NativeChildXmls ?? []).Where(xml => !string.IsNullOrWhiteSpace(xml)))
        {
            try
            {
                var nativeChild = XElement.Parse(nativeChildXml);
                if (nativeChild.Name.Namespace == worksheetNs)
                {
                    validationElement.Add(nativeChild);
                    changed = true;
                }
            }
            catch
            {
                // Ignore malformed native data-validation payloads from older saves.
            }
        }

        return changed;
    }

    private static bool TrySetNativeAttributeIfMissing(XElement element, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        try
        {
            var attributeName = XName.Get(name);
            if (element.Attribute(attributeName) is not null)
                return false;

            element.SetAttributeValue(attributeName, value);
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
}

internal sealed record DataValidationNativeMetadata(
    GridRange AppliesTo,
    IReadOnlyList<GridRange> AppliesToRanges,
    string Sqref,
    IReadOnlyDictionary<string, string> ModeledAttributes,
    string? Formula1,
    string? Formula2,
    IReadOnlyDictionary<string, string> NativeAttributes,
    IReadOnlyList<string> NativeChildXmls,
    IReadOnlyDictionary<string, string> NativeContainerAttributes,
    IReadOnlyList<string> NativeContainerChildXmls);
