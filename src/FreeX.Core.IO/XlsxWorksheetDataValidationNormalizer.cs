using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetDataValidationNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly HashSet<string> DataValidationsChildren = ["dataValidation"];
    private static readonly HashSet<string> DataValidationChildren = ["formula1", "formula2"];

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            if (!NormalizeWorksheet(worksheetXml))
                continue;

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    internal static bool NormalizeWorksheet(XDocument worksheetXml)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return false;

        var changed = false;
        foreach (var dataValidations in root.Elements(WorksheetNs + "dataValidations").ToList())
            changed |= NormalizeElement(dataValidations);

        return changed;
    }

    internal static bool NormalizeElement(XElement dataValidations)
    {
        var changed = false;
        changed |= RemoveUnexpectedWorksheetChildren(dataValidations, DataValidationsChildren);

        foreach (var validation in dataValidations.Elements(WorksheetNs + "dataValidation").ToList())
            changed |= NormalizeValidationElement(validation);

        return changed;
    }

    private static bool NormalizeValidationElement(XElement validation)
    {
        var changed = false;
        changed |= RemoveUnexpectedWorksheetChildren(validation, DataValidationChildren);
        changed |= NormalizeChildOrder(validation, DataValidationChildOrder);
        return changed;
    }

    private static bool RemoveUnexpectedWorksheetChildren(XElement element, IReadOnlySet<string> allowedLocalNames)
    {
        var changed = false;
        foreach (var child in element.Elements().ToList())
        {
            if (child.Name.Namespace != WorksheetNs ||
                allowedLocalNames.Contains(child.Name.LocalName))
            {
                continue;
            }

            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeChildOrder(XElement element, Func<XElement, int> orderSelector)
    {
        var children = element.Elements()
            .Select((child, index) => new { Child = child, Index = index })
            .OrderBy(item => orderSelector(item.Child))
            .ThenBy(item => item.Index)
            .Select(item => item.Child)
            .ToList();
        if (children.Count == 0 || element.Elements().SequenceEqual(children))
            return false;

        element.ReplaceNodes(children);
        return true;
    }

    private static int DataValidationChildOrder(XElement child) =>
        child.Name == WorksheetNs + "formula1" ? 0 :
        child.Name == WorksheetNs + "formula2" ? 10 :
        90;

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry) =>
        entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
        entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
        !entry.FullName.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
}
