using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxDocumentPropertiesPreserver
{
    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        PreserveDocumentPropertyElements(
            sourceArchive,
            targetArchive,
            "docProps/core.xml",
            [
                XName.Get("subject", "http://purl.org/dc/elements/1.1/"),
                XName.Get("keywords", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"),
                XName.Get("category", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"),
                XName.Get("contentStatus", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"),
                XName.Get("language", "http://purl.org/dc/elements/1.1/"),
                XName.Get("version", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties")
            ]);

        PreserveDocumentPropertyElements(
            sourceArchive,
            targetArchive,
            "docProps/app.xml",
            [
                XName.Get("Application", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
                XName.Get("Company", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
                XName.Get("Manager", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
                XName.Get("PresentationFormat", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
                XName.Get("Template", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties")
            ]);
    }

    private static void PreserveDocumentPropertyElements(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string partName,
        IReadOnlyCollection<XName> stableElementNames)
    {
        var sourceEntry = sourceArchive.GetEntry(partName);
        var targetEntry = targetArchive.GetEntry(partName);
        if (sourceEntry is null)
            return;

        if (targetEntry is null)
        {
            XlsxPackageMetadataMerger.CopyEntry(sourceEntry, targetArchive);
            return;
        }

        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var targetXml = XlsxPackageXmlEditor.LoadXml(targetEntry);
        var sourceRoot = sourceXml.Root;
        var targetRoot = targetXml.Root;
        if (sourceRoot is null || targetRoot is null)
            return;

        var changed = false;
        foreach (var stableElementName in stableElementNames)
        {
            var sourceElement = sourceRoot.Element(stableElementName);
            if (sourceElement is null)
                continue;

            var targetElement = targetRoot.Element(stableElementName);
            if (targetElement is null)
            {
                targetRoot.Add(new XElement(sourceElement));
                changed = true;
                continue;
            }

            if (XNode.DeepEquals(targetElement, sourceElement))
                continue;

            targetElement.ReplaceWith(new XElement(sourceElement));
            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, partName, targetXml);
    }
}
