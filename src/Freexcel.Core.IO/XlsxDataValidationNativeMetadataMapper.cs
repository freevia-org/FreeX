using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

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

            var firstRef = sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstRef))
                continue;

            try
            {
                var appliesTo = firstRef.Contains(':', StringComparison.Ordinal)
                    ? GridRange.Parse(firstRef, tempSheet)
                    : new GridRange(CellAddress.Parse(firstRef, tempSheet), CellAddress.Parse(firstRef, tempSheet));
                result.Add(new DataValidationNativeMetadata(
                    appliesTo,
                    ReadAttributes(validation),
                    ReadChildXmls(validation, worksheetNs),
                    containerAttributes,
                    containerChildXmls));
            }
            catch
            {
                // Ignore native metadata for ranges Freexcel cannot parse.
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
                item.AppliesTo.Start.Row == validation.AppliesTo.Start.Row &&
                item.AppliesTo.Start.Col == validation.AppliesTo.Start.Col &&
                item.AppliesTo.End.Row == validation.AppliesTo.End.Row &&
                item.AppliesTo.End.Col == validation.AppliesTo.End.Col);
            if (metadata is null)
                continue;

            if (metadata.NativeAttributes.Count > 0)
                validation.NativeAttributes = metadata.NativeAttributes;
            if (metadata.NativeChildXmls.Count > 0)
                validation.NativeChildXmls = metadata.NativeChildXmls;
            if (metadata.NativeContainerAttributes.Count > 0)
                validation.NativeContainerAttributes = metadata.NativeContainerAttributes;
            if (metadata.NativeContainerChildXmls.Count > 0)
                validation.NativeContainerChildXmls = metadata.NativeContainerChildXmls;
        }
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
                if (validationsByRange.TryGetValue(validation.AppliesTo.ToString(), out var validationElement))
                    changed |= ApplyValidationNativeMetadata(validationElement, validation, workbookNs);
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
            if (!string.IsNullOrWhiteSpace(name) && dataValidations.Attribute(name) is null)
            {
                dataValidations.SetAttributeValue(name, value);
                changed = true;
            }
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
            if (!string.IsNullOrWhiteSpace(name) && validationElement.Attribute(name) is null)
            {
                validationElement.SetAttributeValue(name, value);
                changed = true;
            }
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
}

internal sealed record DataValidationNativeMetadata(
    GridRange AppliesTo,
    IReadOnlyDictionary<string, string> NativeAttributes,
    IReadOnlyList<string> NativeChildXmls,
    IReadOnlyDictionary<string, string> NativeContainerAttributes,
    IReadOnlyList<string> NativeContainerChildXmls);
