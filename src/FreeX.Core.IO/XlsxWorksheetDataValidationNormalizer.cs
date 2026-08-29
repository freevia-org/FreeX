using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetDataValidationNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly HashSet<string> DataValidationsChildren = ["dataValidation"];
    private static readonly HashSet<string> DataValidationChildren = ["formula1", "formula2"];

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var changed = false;
        foreach (var dataValidations in worksheetRoot.Elements(WorksheetNs + "dataValidations").ToList())
            changed |= NormalizeElement(dataValidations);

        return changed;
    }

    internal static bool NormalizeWorksheet(XDocument worksheetXml)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return false;

        return NormalizeWorksheetRoot(root);
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
        changed |= XlsxXmlNormalizationHelpers.NormalizeChildOrder(validation, DataValidationChildOrder);
        changed |= CollapseSingleCellSqref(validation);
        return changed;
    }

    /// <summary>
    /// Rewrites "A1:A1" style sqref tokens to "A1" for single-cell ranges.
    /// Excel itself writes "A1" and the two forms are equivalent, but keeping "A1:A1" in
    /// authored workbooks looks non-canonical and may confuse strict XML comparisons.
    /// </summary>
    private static bool CollapseSingleCellSqref(XElement validation)
    {
        var sqrefAttr = validation.Attribute("sqref");
        if (sqrefAttr is null)
            return false;

        var original = sqrefAttr.Value;
        var tokens   = original.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var changed  = false;

        for (var i = 0; i < tokens.Length; i++)
        {
            var colon = tokens[i].IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
                continue; // already a bare cell ref or special token

            var startPart = tokens[i][..colon];
            var endPart   = tokens[i][(colon + 1)..];

            // Collapse only when start and end are identical (case-insensitive for A1 notation)
            if (string.Equals(startPart, endPart, StringComparison.OrdinalIgnoreCase))
            {
                tokens[i] = startPart;
                changed   = true;
            }
        }

        if (!changed)
            return false;

        sqrefAttr.Value = string.Join(' ', tokens);
        return true;
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

    private static int DataValidationChildOrder(XElement child) =>
        child.Name == WorksheetNs + "formula1" ? 0 :
        child.Name == WorksheetNs + "formula2" ? 10 :
        90;

}
