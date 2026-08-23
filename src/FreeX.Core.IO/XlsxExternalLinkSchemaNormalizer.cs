using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxExternalLinkSchemaNormalizer
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XName[] LinkPayloadNames =
    [
        WorkbookNs + "externalBook",
        WorkbookNs + "ddeLink",
        WorkbookNs + "oleLink"
    ];

    public static void NormalizePackage(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(IsExternalLinkXmlEntry).ToList())
        {
            var document = XlsxPackageXmlEditor.LoadXml(entry);
            var root = document.Root;
            if (root is null || root.Name != WorkbookNs + "externalLink")
                continue;

            if (NormalizeExternalLinkRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, document);
        }
    }

    /// <summary>
    /// Reads the cached sheet names, defined names, and cached cell values off a (not-yet-normalized
    /// or already-normalized) <c>externalBook</c> element and populates <paramref name="model"/> with
    /// them, so the formula engine can resolve <c>[Book.xlsx]Sheet1!A1</c>-style references and named
    /// ranges against the values Excel cached at last refresh, even when the source workbook is
    /// unavailable. No-ops for <c>ddeLink</c>/<c>oleLink</c> payload elements, which never carry this
    /// metadata.
    /// </summary>
    public static void PopulateModelFromExternalBook(XElement? externalBook, ExternalLinkModel model)
    {
        if (externalBook is null || externalBook.Name != WorkbookNs + "externalBook")
            return;

        model.SheetNames.Clear();
        model.SheetNames.AddRange(ExternalLinkModel.ParseSheetNames(externalBook.Element(WorkbookNs + "sheetNames")));

        model.DefinedNames.Clear();
        model.DefinedNames.AddRange(ExternalLinkModel.ParseDefinedNames(externalBook.Element(WorkbookNs + "definedNames")));

        model.CachedSheetData.Clear();
        model.CachedSheetData.AddRange(ExternalLinkModel.ParseSheetDataSet(externalBook.Element(WorkbookNs + "sheetDataSet")));
    }

    public static bool NormalizeExternalLinkRoot(XElement externalLink)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(externalLink, Array.Empty<XName>());

        var keptPayload = false;
        var keptExtensionList = false;
        foreach (var child in externalLink.Elements().ToList())
        {
            if (LinkPayloadNames.Contains(child.Name))
            {
                if (keptPayload)
                {
                    child.Remove();
                    changed = true;
                    continue;
                }

                changed |= NormalizeLinkPayloadElement(child);
                if (ShouldRemoveLinkPayloadElement(child))
                {
                    child.Remove();
                    changed = true;
                    continue;
                }

                keptPayload = true;
                continue;
            }

            if (child.Name == WorkbookNs + "extLst")
            {
                if (keptExtensionList)
                {
                    child.Remove();
                    changed = true;
                    continue;
                }

                keptExtensionList = true;
                continue;
            }

            child.Remove();
            changed = true;
        }

        changed |= XlsxXmlNormalizationHelpers.NormalizeChildOrder(externalLink, ExternalLinkChildOrder);
        return changed;
    }

    private static bool NormalizeLinkPayloadElement(XElement payload) =>
        payload.Name == WorkbookNs + "externalBook"
            ? NormalizeExternalBookElement(payload)
            : false;

    private static bool ShouldRemoveLinkPayloadElement(XElement payload) =>
        payload.Name == WorkbookNs + "externalBook" &&
        string.IsNullOrWhiteSpace(payload.Attribute(RelationshipNs + "id")?.Value);

    private static bool NormalizeExternalBookElement(XElement externalBook)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(externalBook, RelationshipNs + "id");
        changed |= XlsxXmlNormalizationHelpers.NormalizeRelationshipId(externalBook, RelationshipNs + "id");

        foreach (var child in externalBook.Elements().ToList())
        {
            if (child.Name == WorkbookNs + "sheetNames")
            {
                changed |= NormalizeSheetNamesElement(child);
                if (!child.Elements(WorkbookNs + "sheetName").Any())
                {
                    child.Remove();
                    changed = true;
                }
                continue;
            }

            if (child.Name == WorkbookNs + "definedNames")
            {
                changed |= NormalizeDefinedNamesElement(child);
                if (!child.Elements(WorkbookNs + "definedName").Any())
                {
                    child.Remove();
                    changed = true;
                }
                continue;
            }

            if (child.Name == WorkbookNs + "sheetDataSet")
                continue;

            child.Remove();
            changed = true;
        }

        changed |= XlsxXmlNormalizationHelpers.NormalizeChildOrder(externalBook, ExternalBookChildOrder);
        return changed;
    }

    private static bool NormalizeSheetNamesElement(XElement sheetNames)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(sheetNames, Array.Empty<XName>());
        foreach (var child in sheetNames.Elements().ToList())
        {
            if (child.Name != WorkbookNs + "sheetName")
            {
                child.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeSheetNameElement(child);
            if (string.IsNullOrWhiteSpace(child.Attribute("val")?.Value))
            {
                child.Remove();
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeSheetNameElement(XElement sheetName)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(sheetName, XName.Get("val"));
        changed |= NormalizeSheetNameValueAttribute(sheetName);
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(sheetName);
        return changed;
    }

    /// <summary>
    /// Removes <c>sheetName/@val</c> only when it is missing or blank; otherwise preserves it
    /// verbatim, including any leading/trailing spaces. Unlike most cached string attributes,
    /// this one is NOT safe to route through <see cref="NormalizeOptionalTextAttribute"/>'s
    /// unconditional trim: Excel permits leading/trailing spaces in sheet names, and the exact
    /// same (untrimmed) name is separately embedded in the quoted sheet-qualifier of any worksheet
    /// formula that references it (e.g. <c>'[1]Sheet 1 '!A1</c>), in a package part this
    /// normalizer never touches. Trimming here would desync the two representations, so on the
    /// next load <see cref="Model.ExternalLinkModel.TryFindSheetIndex"/>'s exact string match
    /// would fail and a previously-resolving external reference would turn into #REF!.
    /// </summary>
    private static bool NormalizeSheetNameValueAttribute(XElement sheetName)
    {
        var attribute = sheetName.Attribute("val");
        if (attribute is null)
            return false;

        if (string.IsNullOrWhiteSpace(attribute.Value))
        {
            attribute.Remove();
            return true;
        }

        return false;
    }

    private static bool NormalizeDefinedNamesElement(XElement definedNames)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(definedNames, Array.Empty<XName>());
        foreach (var child in definedNames.Elements().ToList())
        {
            if (child.Name != WorkbookNs + "definedName")
            {
                child.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeDefinedNameElement(child);
            if (string.IsNullOrWhiteSpace(child.Attribute("name")?.Value))
            {
                child.Remove();
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeDefinedNameElement(XElement definedName)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(
            definedName,
            XName.Get("name"),
            XName.Get("refersTo"),
            XName.Get("sheetId"));
        changed |= NormalizeOptionalTextAttribute(definedName, "name");
        changed |= NormalizeOptionalTextAttribute(definedName, "refersTo");
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(definedName, "sheetId", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(definedName);
        return changed;
    }

    private static bool NormalizeOptionalTextAttribute(XElement element, string attributeName)
    {
        var attribute = element.Attribute(attributeName);
        var trimmed = attribute?.Value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        return XlsxXmlNormalizationHelpers.SetAttributeIfChanged(element, attributeName, trimmed);
    }

    private static int ExternalLinkChildOrder(XElement child) =>
        LinkPayloadNames.Contains(child.Name) ? 0 :
        child.Name == WorkbookNs + "extLst" ? 100 :
        90;

    private static int ExternalBookChildOrder(XElement child) =>
        child.Name == WorkbookNs + "sheetNames" ? 0 :
        child.Name == WorkbookNs + "definedNames" ? 1 :
        child.Name == WorkbookNs + "sheetDataSet" ? 2 :
        90;

    private static bool IsExternalLinkXmlEntry(ZipArchiveEntry entry) =>
        XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/externalLinks/");
}
