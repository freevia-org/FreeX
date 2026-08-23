using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetDataConsolidationNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly HashSet<string> DataConsolidateAttributes = ["function", "leftLabels", "startLabels", "topLabels", "link"];
    private static readonly HashSet<string> DataReferencesAttributes = ["count"];
    private static readonly HashSet<string> DataReferenceAttributes = ["ref", "name", "sheet"];

    private static readonly HashSet<string> ValidFunctions =
    [
        "average",
        "count",
        "countNums",
        "max",
        "min",
        "product",
        "stdDev",
        "stdDevp",
        "sum",
        "var",
        "varp"
    ];

    public static bool NormalizeElement(XElement dataConsolidate)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(dataConsolidate, DataConsolidateAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(dataConsolidate, WorksheetNs + "dataRefs");
        changed |= MergeDuplicateDataReferences(dataConsolidate);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(dataConsolidate, "function", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ValidFunctions));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(dataConsolidate, "leftLabels", XlsxXmlNormalizationHelpers.NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(dataConsolidate, "startLabels", XlsxXmlNormalizationHelpers.NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(dataConsolidate, "topLabels", XlsxXmlNormalizationHelpers.NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(dataConsolidate, "link", XlsxXmlNormalizationHelpers.NormalizeBoolean);

        foreach (var dataRefs in dataConsolidate.Elements(WorksheetNs + "dataRefs"))
        {
            changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(dataRefs, DataReferencesAttributes);
            changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(dataRefs, WorksheetNs + "dataRef");
            foreach (var dataRef in dataRefs.Elements(WorksheetNs + "dataRef"))
            {
                changed |= RemoveUnknownDataReferenceAttributes(dataRef);
                changed |= XlsxXmlNormalizationHelpers.NormalizeRelationshipId(dataRef, RelationshipNs + "id");
                changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(dataRef);
            }

            var count = dataRefs.Elements(WorksheetNs + "dataRef").Count().ToString(CultureInfo.InvariantCulture);
            changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(dataRefs, "count", count);
        }

        return changed;
    }

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var dataConsolidate = worksheetRoot.Element(WorksheetNs + "dataConsolidate");
        return dataConsolidate is not null && NormalizeElement(dataConsolidate);
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static bool MergeDuplicateDataReferences(XElement dataConsolidate)
    {
        var dataRefs = dataConsolidate.Elements(WorksheetNs + "dataRefs").ToList();
        if (dataRefs.Count <= 1)
            return false;

        var primary = dataRefs[0];
        foreach (var duplicate in dataRefs.Skip(1))
        {
            primary.Add(duplicate.Elements(WorksheetNs + "dataRef").Select(dataRef => new XElement(dataRef)));
            duplicate.Remove();
        }

        return true;
    }

    private static bool RemoveUnknownDataReferenceAttributes(XElement element)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && DataReferenceAttributes.Contains(attribute.Name.LocalName)) ||
                attribute.Name == RelationshipNs + "id")
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

}
